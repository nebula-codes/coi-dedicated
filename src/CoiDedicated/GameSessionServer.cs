using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Threading.Channels;
using CoiDedicated.GameAdapter;
using CoiDedicated.Protocol;
using Mafi;
using Mafi.Core.Input;
using Mafi.Core.Simulation;

namespace CoiDedicated;

internal sealed record AdmittedCommand(ulong RequestId, string Author, byte[] Payload, IInputCommand Command);

/// <summary>TCP transports proposals; the simulation thread alone admits commands and advances the island.</summary>
internal sealed class GameSessionServer : IDisposable
{
    private sealed class SnapshotLease(FileStream file, int step) : IDisposable
    {
        private FileStream? file = file;
        public int Step { get; } = step;
        public FileStream? Take() => Interlocked.Exchange(ref file, null);
        public void Dispose() => Take()?.Dispose();
    }

    private sealed record PresenceSample(PlayerPresenceState State, long ReceivedAt);

    private sealed class Peer(TcpClient client, string name, uint id, CancellationToken cancellation)
    {
        public readonly TcpClient Client = client;
        public readonly string Name = name;
        public readonly uint Id = id;
        public readonly CancellationTokenSource Stop = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        public readonly Channel<ProtocolFrame> Outbound = Channel.CreateBounded<ProtocolFrame>(128);
        public readonly Channel<bool> SendSignal = Channel.CreateBounded<bool>(new BoundedChannelOptions(1) { FullMode = BoundedChannelFullMode.DropWrite });
        public ProtocolFrame? LatestPresence;
        public PresenceSample? Presence;
        public long LastPresenceAt;
        public readonly TaskCompletionSource<SnapshotLease> Snapshot = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public SnapshotLease? PendingSnapshot;
        public readonly Queue<(ulong Sequence, int Step)> AwaitingAck = new();
        public readonly Queue<(ulong Sequence, int Step, byte[] Digest)> AwaitingCheckpoint = new();
        public DateTime JoinedAt = DateTime.UtcNow;
        public volatile bool Active;
        public int SnapshotStep;
        public int QueuedCommands;
        public bool NeedsInitialCheckpoint = true;
        public bool RecoveryPending;
        public bool Closed => Stop.IsCancellationRequested;
        public void Close() { Stop.Cancel(); Client.Dispose(); Outbound.Writer.TryComplete(); SendSignal.Writer.TryComplete(); PendingSnapshot?.Dispose(); }
        public void Send(ProtocolFrame frame) { if (!Outbound.Writer.TryWrite(frame)) Close(); else SendSignal.Writer.TryWrite(true); }
        public void SendPresence(ProtocolFrame frame) { Interlocked.Exchange(ref LatestPresence, frame); SendSignal.Writer.TryWrite(true); }
    }

    private abstract record Incoming(Peer Peer);
    private sealed record Join(Peer Peer) : Incoming(Peer);
    private sealed record Ready(Peer Peer, int Step) : Incoming(Peer);
    private sealed record Acknowledgment(Peer Peer, ulong Sequence, int Step) : Incoming(Peer);
    private sealed record Checkpoint(Peer Peer, ulong Sequence, int Step, byte[] Digest) : Incoming(Peer);
    private sealed record Proposal(Peer Peer, ulong Id, byte[] Payload) : Incoming(Peer);
    private readonly TcpListener listener;
    private readonly CancellationTokenSource lifetime = new();
    private readonly SemaphoreSlim slots = new(8, 8);
    private readonly ConcurrentDictionary<string, Peer> peers = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<int, Task> connections = new();
    private readonly Channel<Incoming> incoming = Channel.CreateBounded<Incoming>(256);
    private readonly List<Peer> worldPeers = new(); // main thread only
    private readonly Queue<Proposal> proposals = new(); // main thread only
    private readonly Queue<IInputCommand> systemCommands = new(); // main thread only
    private readonly string secret;
    private readonly Task acceptTask;
    private readonly Task presenceTask;
    private ulong sequence;
    private int connectionId;
    private readonly string Build;

    public GameSessionServer(string address, int port, string secret, string build)
    {
        this.secret = secret;
        Build = build;
        listener = new TcpListener(IPAddress.Parse(address), port);
        listener.Start(8);
        acceptTask = AcceptLoop();
        presenceTask = PresenceLoop();
        Console.WriteLine($"[game] Authenticated gameplay endpoint: {listener.LocalEndpoint}");
    }

    public int PlayerCount => worldPeers.Count(p => p.Active && !p.Closed);
    public bool WaitingForJoin => worldPeers.Any(p => !p.Active && !p.Closed && !p.RecoveryPending);
    public void ScheduleSystem(IInputCommand command) => systemCommands.Enqueue(command);

    public void Pump(Func<string> save, ISimLoopEvents simulation)
    {
        foreach (Peer peer in worldPeers)
            if (!peer.Active && DateTime.UtcNow - peer.JoinedAt > GameSessionTiming.JoinTimeout) peer.Close();
        worldPeers.RemoveAll(p => p.Closed);
        for (int i = 0; i < 64 && incoming.Reader.TryRead(out Incoming? message); i++)
        {
            Peer peer = message.Peer;
            if (peer.Closed || peer.RecoveryPending) continue;
            switch (message)
            {
                case Join:
                    try
                    {
                        // Open the exact generation before the next autosave can atomically replace it.
                        var snapshot = new FileStream(save(), FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete, 65536, FileOptions.Asynchronous);
                        peer.SnapshotStep = simulation.CurrentStep.Value;
                        worldPeers.Add(peer);
                        var lease = new SnapshotLease(snapshot, peer.SnapshotStep);
                        peer.PendingSnapshot = lease;
                        if (peer.Closed || !peer.Snapshot.TrySetResult(lease)) lease.Dispose();
                        Console.WriteLine($"[game] {peer.Name} loading step {peer.SnapshotStep}; simulation waits up to {GameSessionTiming.JoinTimeout.TotalSeconds:0}s.");
                    }
                    catch (Exception error) { peer.Snapshot.TrySetException(error); peer.Close(); }
                    break;
                case Ready ready:
                    if (peer.Active || ready.Step != peer.SnapshotStep) { peer.Close(); break; }
                    peer.Active = true;
                    Console.WriteLine($"[game] {peer.Name} joined at step {ready.Step}.");
                    break;
                case Acknowledgment ack:
                    if (!peer.Active || !peer.AwaitingAck.TryDequeue(out var expected) || expected.Sequence != ack.Sequence || expected.Step != ack.Step)
                    {
                        Console.WriteLine($"[game] {peer.Name} disconnected: tick acknowledgment mismatch.");
                        peer.Close();
                    }
                    break;
                case Checkpoint check:
                    if (!peer.Active || !peer.AwaitingCheckpoint.TryDequeue(out var expectedCheck)
                        || expectedCheck.Sequence != check.Sequence || expectedCheck.Step != check.Step)
                    { peer.Close(); break; }
                    if (!CryptographicOperations.FixedTimeEquals(expectedCheck.Digest, check.Digest))
                    {
                        Console.WriteLine($"[game] {peer.Name}: gameplay checkpoint mismatch at sequence {check.Sequence}, step {check.Step}; clean snapshot reload required.");
                        peer.Active = false;
                        peer.RecoveryPending = true;
                        using var reason = new PayloadWriter();
                        reason.WriteString($"Gameplay checkpoint mismatch at step {check.Step}. Reload a fresh server snapshot.", 1024);
                        peer.Send(reason.ToFrame(MessageKind.ResyncRequired));
                    }
                    else Console.WriteLine($"[game] {peer.Name}: gameplay checkpoint verified at sequence {check.Sequence}, step {check.Step}.");
                    break;
                case Proposal proposal:
                    if (!peer.Active) peer.Close();
                    else proposals.Enqueue(proposal);
                    break;
            }
        }
    }

    public List<AdmittedCommand> Admit(DependencyResolver resolver)
    {
        var result = new List<AdmittedCommand>();
        // Leave room for frame metadata and future protocol fields. Deferred proposals stay in order.
        const int payloadBudget = 512 * 1024;
        int payloadBytes = 0;
        for (int i = 0; i < 64 && systemCommands.TryPeek(out IInputCommand? command); i++)
        {
            if (!GameCommandCodec.TryEncode(command, out byte[] payload, out string reason)) throw new InvalidOperationException(reason);
            if (payloadBytes + payload.Length + 80 > payloadBudget) break;
            systemCommands.Dequeue();
            payloadBytes += payload.Length + 80;
            result.Add(new(0, "server", payload, command));
        }
        for (int i = 0; i < 32 && proposals.TryPeek(out Proposal? proposal); i++)
        {
            bool isolated = !proposal.Peer.Closed && GameCommandCodec.RequiresIsolatedTick(proposal.Payload);
            // Preserve proposal order and validate index mutations against the post-tick world.
            // A second removal must not be decoded against the first removal's old list length.
            if (isolated && result.Count != 0) break;
            if (!proposal.Peer.Closed && payloadBytes + proposal.Payload.Length + 80 > payloadBudget) break;
            proposals.Dequeue();
            Interlocked.Decrement(ref proposal.Peer.QueuedCommands);
            if (proposal.Peer.Closed) continue;
            try
            {
                IInputCommand command = GameCommandCodec.Decode(proposal.Payload, resolver);
                if (command is GameSpeedChangeCmd speed && !DeterministicSimulation.IsSupportedSpeed(speed.NewSpeedMultiplier))
                    throw new ProtocolException($"Multiplayer supports game speeds 1 through {DeterministicSimulation.MaxSpeed}.");
                result.Add(new(proposal.Id, proposal.Peer.Name, proposal.Payload, command));
                payloadBytes += proposal.Payload.Length + 80;
                if (isolated) break;
            }
            catch (Exception error) when (error is ProtocolException or ArgumentException or InvalidOperationException)
            {
                using var payload = new PayloadWriter();
                payload.WriteUInt64(proposal.Id);
                payload.WriteString(error.Message, 65536);
                proposal.Peer.Send(payload.ToFrame(MessageKind.CommandRejected));
            }
        }
        foreach (AdmittedCommand command in result) resolver.Resolve<IInputScheduler>().ScheduleInputCmd(command.Command);
        return result;
    }

    public void Publish(int beforeStep, int afterStep, bool beforePaused, int beforeSpeed, IReadOnlyList<AdmittedCommand> commands, DependencyResolver resolver)
    {
        ++sequence;
        Peer[] active = worldPeers.Where(p => p.Active && !p.Closed).ToArray();
        // Keep advancing the authoritative sequence while nobody is connected, but
        // avoid building network frames or scanning the entire terrain for nobody.
        if (active.Length == 0) return;
        using var payload = new PayloadWriter();
        payload.WriteUInt64(sequence);
        payload.WriteInt32(beforeStep);
        payload.WriteInt32(afterStep);
        payload.WriteBoolean(beforePaused);
        payload.WriteInt32(beforeSpeed);
        payload.WriteUInt16(checked((ushort)commands.Count));
        foreach (AdmittedCommand command in commands)
        {
            payload.WriteUInt64(command.RequestId);
            payload.WriteString(command.Author, 64);
            payload.WriteBytes(command.Payload, 65536);
        }
        bool checkpointDue = sequence % GameplayCheckpoint.IntervalTicks == 0 || active.Any(p => p.NeedsInitialCheckpoint);
        byte[]? digest = checkpointDue ? GameplayCheckpoint.Compute(resolver) : null;
        payload.WriteBytes(digest ?? Array.Empty<byte>(), 32);
        ProtocolFrame frame = payload.ToFrame(MessageKind.Tick);
        foreach (Peer peer in active)
        {
            if (peer.AwaitingAck.Count >= 100) { peer.Close(); continue; }
            peer.AwaitingAck.Enqueue((sequence, afterStep));
            if (digest != null)
            {
                if (peer.AwaitingCheckpoint.Count >= 3) { peer.Close(); continue; }
                peer.NeedsInitialCheckpoint = false;
                peer.AwaitingCheckpoint.Enqueue((sequence, afterStep, digest));
            }
            peer.Send(frame);
        }
    }

    private async Task AcceptLoop()
    {
        try
        {
            while (!lifetime.IsCancellationRequested)
            {
                TcpClient client = await listener.AcceptTcpClientAsync(lifetime.Token);
                if (!slots.Wait(0)) { client.Dispose(); continue; }
                int id = Interlocked.Increment(ref connectionId);
                Task task = Handle(client, checked((uint)id));
                connections[id] = task;
                _ = task.ContinueWith(_ => { connections.TryRemove(id, out var ignored); slots.Release(); }, TaskScheduler.Default);
            }
        }
        catch (OperationCanceledException) { }
        catch (SocketException) when (lifetime.IsCancellationRequested) { }
    }

    private async Task Handle(TcpClient client, uint peerId)
    {
        Peer? peer = null;
        using (client)
        using (var deadline = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token))
        {
            try
            {
                client.NoDelay = true;
                NetworkStream stream = client.GetStream();
                deadline.CancelAfter(TimeSpan.FromSeconds(10));
                byte[] nonce = RandomNumberGenerator.GetBytes(32);
                using (var challenge = new PayloadWriter())
                {
                    challenge.WriteBytes(nonce, 32);
                    challenge.WriteString(Build, 128);
                    await Welcome(stream, 0, challenge, deadline.Token);
                }
                var frame = await FrameCodec.ReadAsync(stream, deadline.Token) ?? throw new EndOfStreamException();
                if (frame.Kind != MessageKind.Hello) throw new ProtocolException("Expected Hello.");
                var reader = new PayloadReader(frame.Payload);
                string name = reader.ReadString(64);
                byte[] proof = reader.ReadBytes(32);
                reader.EnsureEnd();
                if (name.Length == 0 || name == "server" || name.Any(c => !(char.IsAsciiLetterOrDigit(c) || c is '_' or '-')))
                    throw new ProtocolException("Player name must use ASCII letters, digits, underscore or hyphen, and cannot be 'server'.");
                if (!ControlAuthentication.VerifyProof(secret, nonce, name, proof)) throw new ProtocolException("Authentication failed.");
                peer = new Peer(client, name, peerId, lifetime.Token);
                if (!peers.TryAdd(name, peer)) throw new ProtocolException("Player name already connected.");
                using (var welcome = new PayloadWriter())
                {
                    welcome.WriteString(Build, 128);
                    await Welcome(stream, 1, welcome, deadline.Token);
                }
                deadline.CancelAfter(GameSessionTiming.JoinTimeout);
                Enqueue(new Join(peer));
                using var snapshot = await peer.Snapshot.Task.WaitAsync(deadline.Token);
                using (FileStream file = snapshot.Take() ?? throw new OperationCanceledException("Joining peer disconnected."))
                    await SendSnapshot(stream, file, snapshot.Step, deadline.Token);
                frame = await FrameCodec.ReadAsync(stream, deadline.Token) ?? throw new EndOfStreamException();
                if (frame.Kind != MessageKind.Ack) throw new ProtocolException("Expected snapshot-ready acknowledgment.");
                reader = new PayloadReader(frame.Payload);
                int readyStep = reader.ReadInt32();
                reader.EnsureEnd();
                Enqueue(new Ready(peer, readyStep));
                deadline.CancelAfter(Timeout.InfiniteTimeSpan);
                Task sender = SendLoop(peer, stream);
                try
                {
                    ulong lastRequest = 0;
                    while (!peer.Closed)
                    {
                        using var readDeadline = CancellationTokenSource.CreateLinkedTokenSource(peer.Stop.Token);
                        readDeadline.CancelAfter(GameSessionTiming.PeerIdleTimeout); // Also covers a different player's join pause.
                        frame = await FrameCodec.ReadAsync(stream, readDeadline.Token) ?? throw new EndOfStreamException();
                        reader = new PayloadReader(frame.Payload);
                        if (frame.Kind == MessageKind.Ack)
                        {
                            ulong seq = reader.ReadUInt64();
                            int step = reader.ReadInt32();
                            reader.EnsureEnd();
                            Enqueue(new Acknowledgment(peer, seq, step));
                        }
                        else if (frame.Kind == MessageKind.CheckpointReply)
                        {
                            var check = CheckpointCodec.Decode(frame.Payload);
                            Enqueue(new Checkpoint(peer, check.Sequence, check.Step, check.Digest));
                        }
                        else if (frame.Kind == MessageKind.PresenceUpdate)
                        {
                            // Purely cosmetic state bypasses the simulation/input queue entirely.
                            // Drop over-rate updates before decoding; no backlog and no identity supplied by the player.
                            if (frame.Payload.Length > PresenceCodec.MaxUpdateBytes) throw new ProtocolException("Presence update too large.");
                            long now = Environment.TickCount64;
                            if (now - peer.LastPresenceAt < PresenceCodec.IntervalMilliseconds) continue;
                            peer.LastPresenceAt = now;
                            Interlocked.Exchange(ref peer.Presence, new PresenceSample(PresenceCodec.DecodeUpdate(frame.Payload), now));
                        }
                        else if (frame.Kind == MessageKind.Command)
                        {
                            ulong id = reader.ReadUInt64();
                            byte[] payload = reader.ReadBytes(GameCommandCodec.MaxCommandBytes);
                            reader.EnsureEnd();
                            if (id <= lastRequest) throw new ProtocolException("Command IDs must increase.");
                            lastRequest = id;
                            if (Interlocked.Increment(ref peer.QueuedCommands) > 64) throw new ProtocolException("Too many queued commands.");
                            Enqueue(new Proposal(peer, id, payload));
                        }
                        else throw new ProtocolException("Expected tick acknowledgment, game command, or presence update.");
                    }
                }
                finally { peer.Close(); try { await sender; } catch (Exception) { } }
            }
            catch (Exception error) when (error is IOException or SocketException or OperationCanceledException or ProtocolException or InvalidOperationException or ObjectDisposedException)
            {
                if (error is not OperationCanceledException && !lifetime.IsCancellationRequested)
                    Console.WriteLine($"[game] {peer?.Name ?? "Connection"}: {error.Message}");
            }
            finally
            {
                if (peer is not null)
                {
                    peer.Close();
                    if (peers.TryGetValue(peer.Name, out Peer? existing) && ReferenceEquals(existing, peer)) peers.TryRemove(peer.Name, out _);
                }
            }
        }
    }

    private void Enqueue(Incoming value)
    {
        if (!incoming.Writer.TryWrite(value)) { value.Peer.Close(); throw new ProtocolException("Server input queue full."); }
    }

    private static async Task SendLoop(Peer peer, Stream stream)
    {
        try
        {
            await foreach (bool signal in peer.SendSignal.Reader.ReadAllAsync(peer.Stop.Token))
            {
                // Authoritative traffic has priority. At most one cosmetic frame is retained.
                while (peer.Outbound.Reader.TryRead(out ProtocolFrame? frame))
                {
                    await FrameCodec.WriteAsync(stream, frame, peer.Stop.Token);
                    if (frame.Kind == MessageKind.ResyncRequired)
                    {
                        // Finish the reply before closing the receive half. An immediate close
                        // with a concurrent ACK can reset TCP and hide the recovery explanation.
                        peer.Client.Client.Shutdown(SocketShutdown.Send);
                        await Task.Delay(2000, peer.Stop.Token);
                        return;
                    }
                }
                ProtocolFrame? presence = Interlocked.Exchange(ref peer.LatestPresence, null);
                if (presence != null) await FrameCodec.WriteAsync(stream, presence, peer.Stop.Token);
            }
        }
        finally { peer.Close(); }
    }

    private async Task PresenceLoop()
    {
        try
        {
            using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(PresenceCodec.IntervalMilliseconds));
            while (await timer.WaitForNextTickAsync(lifetime.Token))
            {
                long now = Environment.TickCount64;
                Peer[] active = peers.Values.Where(p => p.Active && !p.Closed).ToArray();
                var entries = new List<PlayerPresenceEntry>(PresenceCodec.MaxPeers);
                foreach (Peer peer in active)
                {
                    PresenceSample? sample = Volatile.Read(ref peer.Presence);
                    if (sample != null && sample.State.Visible && now - sample.ReceivedAt <= PresenceCodec.ExpiryMilliseconds)
                        entries.Add(new PlayerPresenceEntry { PeerId = peer.Id, Name = peer.Name, State = sample.State });
                }
                ProtocolFrame snapshot = PresenceCodec.EncodeSnapshot(entries);
                foreach (Peer peer in active) peer.SendPresence(snapshot);
            }
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
    }

    private static async Task Welcome(Stream stream, byte stage, PayloadWriter writer, CancellationToken cancellation)
    {
        byte[] contents = writer.ToArray();
        byte[] payload = new byte[contents.Length + 1];
        payload[0] = stage;
        contents.CopyTo(payload, 1);
        await FrameCodec.WriteAsync(stream, new ProtocolFrame(MessageKind.Welcome, payload), cancellation);
    }

    private static async Task SendSnapshot(Stream stream, FileStream file, int step, CancellationToken cancellation)
    {
        if (file.Length > 256L * 1024 * 1024) throw new ProtocolException("Snapshot exceeds transfer limit.");
        byte[] hash = await SHA256.HashDataAsync(file, cancellation);
        file.Position = 0;
        using (var begin = new PayloadWriter())
        {
            begin.WriteInt32(step);
            begin.WriteInt64(file.Length);
            begin.WriteBytes(hash, 32);
            await FrameCodec.WriteAsync(stream, begin.ToFrame(MessageKind.SnapshotBegin), cancellation);
        }
        byte[] buffer = new byte[65536];
        int count;
        while ((count = await file.ReadAsync(buffer, cancellation)) > 0)
        {
            using var chunk = new PayloadWriter();
            chunk.WriteBytes(buffer.AsSpan(0, count), 65536);
            await FrameCodec.WriteAsync(stream, chunk.ToFrame(MessageKind.SnapshotChunk), cancellation);
        }
        await FrameCodec.WriteAsync(stream, new ProtocolFrame(MessageKind.SnapshotEnd, Array.Empty<byte>()), cancellation);
    }

    public void Dispose()
    {
        lifetime.Cancel();
        listener.Stop();
        foreach (Peer peer in peers.Values) peer.Close();
        try { Task.WhenAll(connections.Values.Append(acceptTask).Append(presenceTask)).Wait(TimeSpan.FromSeconds(3)); } catch { }
    }
}
