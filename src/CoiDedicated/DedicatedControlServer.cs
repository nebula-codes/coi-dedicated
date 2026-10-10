using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using CoiDedicated.Protocol;

namespace CoiDedicated;

internal sealed class ControlReply(string message, FileStream? snapshot = null, int snapshotStep = 0) : IDisposable
{
    private FileStream? snapshot = snapshot;
    public string Message { get; } = message;
    public int SnapshotStep { get; } = snapshotStep;
    public FileStream? TakeSnapshot() => Interlocked.Exchange(ref snapshot, null);
    public void Dispose() => TakeSnapshot()?.Dispose();
}
internal sealed record ControlRequest(string Command, CancellationToken Cancellation, TaskCompletionSource<ControlReply> Completion);

/// <summary>Network I/O stays off the simulation thread; all game work is queued to Pump.</summary>
internal sealed class DedicatedControlServer : IDisposable
{
    private readonly TcpListener listener;
    private readonly CancellationTokenSource lifetime = new();
    private readonly SemaphoreSlim slots = new(8, 8);
    private readonly ConcurrentQueue<ControlRequest> requests = new();
    private readonly ConcurrentDictionary<int, Task> clients = new();
    private readonly string secret;
    private readonly string build;
    private readonly Task acceptTask;
    private int nextClient;

    public DedicatedControlServer(string bindAddress, int port, string secret, string build)
    {
        this.secret = secret;
        this.build = build;
        listener = new TcpListener(IPAddress.Parse(bindAddress), port);
        listener.Start(16);
        acceptTask = AcceptLoop();
        Console.WriteLine($"[server] Authenticated control endpoint: {listener.LocalEndpoint}");
    }

    public void Pump(Func<string, ControlReply> execute)
    {
        // Bound each pump so remote requests cannot monopolize the simulation.
        for (int i = 0; i < 8 && requests.TryDequeue(out var request); i++)
        {
            if (request.Cancellation.IsCancellationRequested) { request.Completion.TrySetCanceled(); continue; }
            try
            {
                ControlReply reply = execute(request.Command);
                // Cancellation can win while the simulation is saving. The
                // abandoned reply still owns its opened snapshot generation.
                if (!request.Completion.TrySetResult(reply)) reply.Dispose();
            }
            catch (Exception error) { request.Completion.TrySetException(error); }
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
                int id = Interlocked.Increment(ref nextClient);
                var task = Handle(client);
                clients[id] = task;
                _ = task.ContinueWith(_ => { clients.TryRemove(id, out var ignored); slots.Release(); }, TaskScheduler.Default);
            }
        }
        catch (OperationCanceledException) { }
        catch (SocketException) when (lifetime.IsCancellationRequested) { }
    }

    private async Task Handle(TcpClient client)
    {
        using (client)
        using (var deadline = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token))
        {
            NetworkStream stream = client.GetStream();
            try
            {
                deadline.CancelAfter(TimeSpan.FromSeconds(10));
                byte[] nonce = RandomNumberGenerator.GetBytes(32);
                using (var challenge = new PayloadWriter())
                {
                    challenge.WriteBytes(nonce, 32);
                    challenge.WriteString(build, 128);
                    await SendWelcome(stream, 0, challenge, deadline.Token);
                }
                var hello = await FrameCodec.ReadAsync(stream, deadline.Token) ?? throw new EndOfStreamException();
                if (hello.Kind != MessageKind.Hello) throw new ProtocolException("Expected Hello.");
                var reader = new PayloadReader(hello.Payload);
                string name = reader.ReadString(64);
                byte[] proof = reader.ReadBytes(32);
                reader.EnsureEnd();
                if (proof.Length != 32 || !ControlAuthentication.VerifyProof(secret, nonce, name, proof))
                    throw new ProtocolException("Authentication failed.");
                using (var welcome = new PayloadWriter())
                {
                    welcome.WriteString(build, 128);
                    await SendWelcome(stream, 1, welcome, deadline.Token);
                }
                deadline.CancelAfter(TimeSpan.FromSeconds(30));
                var frame = await FrameCodec.ReadAsync(stream, deadline.Token) ?? throw new EndOfStreamException();
                if (frame.Kind != MessageKind.Command) throw new ProtocolException("Expected Command.");
                reader = new PayloadReader(frame.Payload);
                uint requestId = reader.ReadUInt32();
                string command = reader.ReadString(32);
                reader.EnsureEnd();
                if (command is not ("status" or "pause" or "resume" or "save" or "snapshot" or "shutdown"))
                    throw new ProtocolException("Unknown command.");
                var completion = new TaskCompletionSource<ControlReply>(TaskCreationOptions.RunContinuationsAsynchronously);
                using var cancelReply = deadline.Token.Register(() => completion.TrySetCanceled(deadline.Token));
                requests.Enqueue(new(command, deadline.Token, completion));
                using ControlReply reply = await completion.Task;
                using (var ack = new PayloadWriter())
                {
                    ack.WriteUInt32(requestId);
                    ack.WriteBoolean(true);
                    ack.WriteString(reply.Message, 65536);
                    await FrameCodec.WriteAsync(stream, ack.ToFrame(MessageKind.Ack), deadline.Token);
                }
                FileStream? snapshot = reply.TakeSnapshot();
                if (snapshot is not null)
                {
                    deadline.CancelAfter(TimeSpan.FromSeconds(120));
                    await SendSnapshot(stream, snapshot, reply.SnapshotStep, deadline.Token);
                }
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or OperationCanceledException or ProtocolException or InvalidOperationException or ArgumentException or NotSupportedException)
            {
                if (error is not OperationCanceledException)
                {
                    try
                    {
                        using var fault = new PayloadWriter();
                        fault.WriteString(error.Message, 65536);
                        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(1));
                        await FrameCodec.WriteAsync(stream, fault.ToFrame(MessageKind.Error), timeout.Token);
                    }
                    catch { /* The peer may have disconnected. */ }
                }
            }
        }
    }

    private static async Task SendWelcome(Stream stream, byte stage, PayloadWriter contents, CancellationToken cancellation)
    {
        var frame = contents.ToFrame(MessageKind.Welcome);
        byte[] payload = new byte[frame.Payload.Length + 1];
        payload[0] = stage;
        frame.Payload.Span.CopyTo(payload.AsSpan(1));
        await FrameCodec.WriteAsync(stream, new ProtocolFrame(MessageKind.Welcome, payload), cancellation);
    }

    private static async Task SendSnapshot(Stream stream, FileStream file, int step, CancellationToken cancellation)
    {
        using var lease = file;
        if (file.Length > 256L * 1024 * 1024) throw new InvalidOperationException("Snapshot exceeds the transfer limit.");
        byte[] digest = await SHA256.HashDataAsync(file, cancellation);
        file.Position = 0;
        using (var begin = new PayloadWriter())
        {
            begin.WriteInt32(step);
            begin.WriteInt64(file.Length);
            begin.WriteBytes(digest, 32);
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
        try { Task.WhenAll(clients.Values.Append(acceptTask)).Wait(TimeSpan.FromSeconds(3)); } catch { }
        while (requests.TryDequeue(out var request)) request.Completion.TrySetCanceled();
        lifetime.Dispose();
    }
}
