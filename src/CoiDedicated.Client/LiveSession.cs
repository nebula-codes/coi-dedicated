using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using CoiDedicated.Protocol;

namespace CoiDedicated.Client;

internal sealed class LiveCommand
{
    internal ulong RequestId = 0;
    internal string Author = "";
    internal byte[] Payload = Array.Empty<byte>();
}

internal sealed class LiveTick
{
    internal ulong Sequence;
    internal int BeforeStep;
    internal int AfterStep;
    internal bool Paused;
    internal int Speed;
    internal byte[] Checkpoint = Array.Empty<byte>();
    internal LiveCommand[] Commands = Array.Empty<LiveCommand>();
}

internal sealed class LiveRejection
{
    internal ulong RequestId;
    internal string Reason = "";
}

internal sealed class LiveSession : IDisposable
{
    private readonly TcpClient client = new TcpClient { NoDelay = true };
    private readonly CancellationTokenSource lifetime = new CancellationTokenSource();
    private readonly SemaphoreSlim writer = new SemaphoreSlim(1, 1);
    private NetworkStream? stream;
    private int ready;
    private int queuedSends;
    private PlayerPresenceEntry[] presence = Array.Empty<PlayerPresenceEntry>();
    private long presenceReceived;
    private string? failure;
    internal readonly ConcurrentQueue<LiveTick> Ticks = new ConcurrentQueue<LiveTick>();
    internal readonly ConcurrentQueue<LiveRejection> Rejections = new ConcurrentQueue<LiveRejection>();
    internal string Name { get; private set; } = "client-" + Guid.NewGuid().ToString("N").Substring(0, 12);
    internal int SnapshotStep { get; private set; }
    internal string? Failure => Volatile.Read(ref failure);
    internal bool RecoveryRequired { get; private set; }
    internal PlayerPresenceEntry[] Presence => (Stopwatch.GetTimestamp() - Interlocked.Read(ref presenceReceived)) / (double)Stopwatch.Frequency * 1000 <= PresenceCodec.ExpiryMilliseconds
        ? Volatile.Read(ref presence) : Array.Empty<PlayerPresenceEntry>();

    internal static async Task<LiveSession> JoinAsync(string host, int port, string keyPath, string output, string expectedBuild, CancellationToken cancellation, string? clientName = null)
    {
        var session = new LiveSession();
        try
        {
            if (!string.IsNullOrWhiteSpace(clientName))
            {
                if (Encoding.UTF8.GetByteCount(clientName) > 64) throw new ArgumentException("Client name exceeds 64 UTF-8 bytes.");
                session.Name = clientName;
            }
            using var stopConnect = cancellation.Register(() => session.client.Close());
            var key = new FileInfo(keyPath);
            if (!key.Exists || key.Length > 16384) throw new IOException("Join key file is missing or exceeds 16 KiB.");
            string secret = File.ReadAllText(keyPath, new UTF8Encoding(false, true)).TrimEnd('\r', '\n');
            if (secret.Length < 16) throw new IOException("Join key must contain at least 16 characters.");
            await session.client.ConnectAsync(host, port).ConfigureAwait(false);
            session.stream = session.client.GetStream();
            ProtocolFrame greeting = await ReadAsync(session.stream, cancellation).ConfigureAwait(false);
            var challenge = Welcome(greeting, 0);
            byte[] nonce = challenge.ReadBytes(32);
            string build = challenge.ReadString(128);
            challenge.EnsureEnd();
            if (!string.Equals(build, expectedBuild, StringComparison.Ordinal)) throw new ProtocolException("Game assembly fingerprint or protocol differs from the server. Use exactly matching game files.");
            byte[] proof = ControlAuthentication.ComputeProof(secret, nonce, session.Name);
            try
            {
                using var hello = new PayloadWriter();
                hello.WriteString(session.Name, 64);
                hello.WriteBytes(proof, 32);
                await FrameCodec.WriteAsync(session.stream, hello.ToFrame(MessageKind.Hello), cancellation).ConfigureAwait(false);
            }
            finally { CryptographicOperations.ZeroMemory(proof); }
            var welcome = Welcome(await ReadAsync(session.stream, cancellation).ConfigureAwait(false), 1);
            if (welcome.ReadString(128) != build) throw new ProtocolException("Server build changed during handshake.");
            welcome.EnsureEnd();
            session.SnapshotStep = await SnapshotDownload.ReceiveAsync(session.stream, output, cancellation).ConfigureAwait(false);
            return session;
        }
        catch { session.Dispose(); throw; }
    }

    internal void SendReady(int step)
    {
        if (step != SnapshotStep) throw new InvalidOperationException("Loaded world step differs from snapshot.");
        if (Interlocked.Exchange(ref ready, 1) != 0) throw new InvalidOperationException("Already joined.");
        using var payload = new PayloadWriter();
        payload.WriteInt32(step);
        _ = SendReadyAndReadAsync(payload.ToFrame(MessageKind.Ack));
    }

    private async Task SendReadyAndReadAsync(ProtocolFrame frame)
    {
        try
        {
            await SendAsync(frame).ConfigureAwait(false);
            ulong? lastSequence = null;
            while (!lifetime.IsCancellationRequested)
            {
                ProtocolFrame incoming = await FrameCodec.ReadAsync(stream!, lifetime.Token).ConfigureAwait(false)
                    ?? throw new EndOfStreamException("Dedicated server disconnected.");
                if (incoming.Kind == MessageKind.PresenceSnapshot)
                {
                    Interlocked.Exchange(ref presence, PresenceCodec.DecodeSnapshot(incoming.Payload));
                    Interlocked.Exchange(ref presenceReceived, Stopwatch.GetTimestamp());
                    continue;
                }
                if (incoming.Kind == MessageKind.CommandRejected)
                {
                    var errorData = new PayloadReader(incoming.Payload);
                    ulong requestId = errorData.ReadUInt64();
                    string reason = errorData.ReadString(65536);
                    errorData.EnsureEnd();
                    if (requestId == 0 || Rejections.Count >= 128)
                        throw new ProtocolException("Invalid command rejection.");
                    Rejections.Enqueue(new LiveRejection { RequestId = requestId, Reason = reason });
                    continue;
                }
                if (incoming.Kind == MessageKind.Error) ThrowServerError(incoming);
                if (incoming.Kind == MessageKind.ResyncRequired)
                {
                    var reason = new PayloadReader(incoming.Payload);
                    string message = reason.ReadString(1024); reason.EnsureEnd();
                    RecoveryRequired = true;
                    throw new ProtocolException(message);
                }
                if (incoming.Kind != MessageKind.Tick) throw new ProtocolException("Expected an authoritative Tick.");
                var data = new PayloadReader(incoming.Payload);
                var tick = new LiveTick
                {
                    Sequence = data.ReadUInt64(), BeforeStep = data.ReadInt32(),
                    AfterStep = data.ReadInt32(), Paused = data.ReadBoolean(), Speed = data.ReadInt32()
                };
                ushort count = data.ReadUInt16();
                if (count > 128 || tick.BeforeStep < 0 || tick.AfterStep < tick.BeforeStep || (long)tick.AfterStep > (long)tick.BeforeStep + 4 || tick.Speed < 1 || tick.Speed > 4)
                    throw new ProtocolException("Invalid authoritative tick bounds.");
                if (lastSequence.HasValue && tick.Sequence != lastSequence.Value + 1)
                    throw new ProtocolException("Authoritative tick sequence gap.");
                tick.Commands = new LiveCommand[count];
                for (int i = 0; i < count; i++)
                    tick.Commands[i] = new LiveCommand { RequestId = data.ReadUInt64(), Author = data.ReadString(64), Payload = data.ReadBytes(65536) };
                tick.Checkpoint = data.ReadBytes(32);
                if (tick.Checkpoint.Length != 0 && tick.Checkpoint.Length != 32) throw new ProtocolException("Invalid checkpoint length.");
                data.EnsureEnd();
                if (Ticks.Count >= 128) throw new ProtocolException("Client fell too far behind the server.");
                Ticks.Enqueue(tick);
                lastSequence = tick.Sequence;
            }
        }
        catch (Exception error) { RecordFailure(error); }
    }

    internal void Propose(ulong requestId, byte[] command)
    {
        using var payload = new PayloadWriter();
        payload.WriteUInt64(requestId);
        payload.WriteBytes(command, 65536);
        QueueSend(payload.ToFrame(MessageKind.Command));
    }

    internal void Acknowledge(ulong sequence, int step)
    {
        using var payload = new PayloadWriter();
        payload.WriteUInt64(sequence);
        payload.WriteInt32(step);
        QueueSend(payload.ToFrame(MessageKind.Ack));
    }

    internal void Checkpoint(ulong sequence, int step, byte[] digest) => QueueSend(CheckpointCodec.Encode(sequence, step, digest));

    // A busy writer drops this cosmetic update. Never queue presence behind game input.
    internal void SendPresence(PlayerPresenceState state)
    {
        if (lifetime.IsCancellationRequested || Volatile.Read(ref ready) == 0 || Volatile.Read(ref queuedSends) != 0 || !writer.Wait(0)) return;
        ProtocolFrame frame;
        try { frame = PresenceCodec.EncodeUpdate(state); }
        catch { writer.Release(); throw; }
        _ = SendPresenceAsync(frame);
    }

    private async Task SendPresenceAsync(ProtocolFrame frame)
    {
        try { await FrameCodec.WriteAsync(stream!, frame, lifetime.Token).ConfigureAwait(false); }
        catch (Exception error) { RecordFailure(error); }
        finally { writer.Release(); }
    }

    private void QueueSend(ProtocolFrame frame)
    {
        if (Interlocked.Increment(ref queuedSends) > 256)
        {
            Interlocked.Decrement(ref queuedSends);
            RecordFailure(new IOException("Client network send queue exceeded its limit."));
            return;
        }
        _ = SendQueuedAsync(frame);
    }

    private async Task SendQueuedAsync(ProtocolFrame frame)
    {
        try { await SendAsync(frame).ConfigureAwait(false); }
        catch (Exception error) { RecordFailure(error); }
        finally { Interlocked.Decrement(ref queuedSends); }
    }

    private async Task SendAsync(ProtocolFrame frame)
    {
        await writer.WaitAsync(lifetime.Token).ConfigureAwait(false);
        try { await FrameCodec.WriteAsync(stream!, frame, lifetime.Token).ConfigureAwait(false); }
        finally { writer.Release(); }
    }

    private static PayloadReader Welcome(ProtocolFrame frame, byte stage)
    {
        if (frame.Kind != MessageKind.Welcome || frame.Payload.Length == 0 || frame.Payload.Span[0] != stage)
            throw new ProtocolException("Unexpected join authentication response.");
        return new PayloadReader(frame.Payload.Slice(1));
    }

    private static async Task<ProtocolFrame> ReadAsync(Stream stream, CancellationToken cancellation)
    {
        ProtocolFrame frame = await FrameCodec.ReadAsync(stream, cancellation).ConfigureAwait(false)
            ?? throw new EndOfStreamException("Dedicated server disconnected.");
        if (frame.Kind == MessageKind.Error)
            ThrowServerError(frame);
        return frame;
    }

    private static void ThrowServerError(ProtocolFrame frame)
    {
        var data = new PayloadReader(frame.Payload);
        string message = data.ReadString(65536);
        data.EnsureEnd();
        throw new ProtocolException("Server: " + message);
    }

    private void RecordFailure(Exception error)
    {
        if (!lifetime.IsCancellationRequested) Interlocked.CompareExchange(ref failure, error.Message, null);
        Dispose();
    }

    public void Dispose()
    {
        if (!lifetime.IsCancellationRequested) lifetime.Cancel();
        client.Close();
    }
}
