using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using CoiDedicated.Manager.Core;
using CoiDedicated.Protocol;

public static class ControlConnectionTests
{
    public static async Task RunAsync()
    {
        string directory = Path.Combine(Path.GetTempPath(), "coi-manager-control-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        string key = Path.Combine(directory, "control.key");
        const string secret = "manager-test-secret-not-for-real-use";
        await File.WriteAllTextAsync(key, secret);
        try
        {
            await WithServerAsync(async stream =>
            {
                string command = await AuthenticateAsync(stream, secret);
                Equal("status", command, "command");
                await AckAsync(stream, 1, "{\"step\":42}");
            }, async port =>
            {
                var response = await new ControlConnection().ExecuteAsync("127.0.0.1", port, key, "status");
                Equal("test-build", response.Build, "build");
                Equal("{\"step\":42}", response.Message, "status response");
            });
            Console.WriteLine("PASS control authenticates and reads status");

            await WithServerAsync(async stream =>
            {
                await AuthenticateAsync(stream, secret);
                await AckAsync(stream, 7, "wrong request");
            }, async port => await ThrowsAsync<ProtocolException>(() => new ControlConnection().ExecuteAsync("127.0.0.1", port, key, "save")));
            Console.WriteLine("PASS control rejects mismatched request IDs");

            byte[] bytes = "verified island bytes"u8.ToArray();
            string output = Path.Combine(directory, "backup.save");
            await WithServerAsync(async stream =>
            {
                Equal("snapshot", await AuthenticateAsync(stream, secret), "snapshot command");
                await AckAsync(stream, 1, "snapshot ready");
                await SnapshotAsync(stream, bytes, SHA256.HashData(bytes));
            }, async port =>
            {
                var downloaded = await new ControlConnection().DownloadSnapshotAsync("127.0.0.1", port, key, output);
                Equal(42, downloaded.Step, "snapshot step");
                Equal((long)bytes.Length, downloaded.Length, "snapshot length");
                if (!(await File.ReadAllBytesAsync(output)).SequenceEqual(bytes)) throw new Exception("Snapshot bytes differ.");
            });
            Console.WriteLine("PASS control verifies and writes snapshots");

            string corrupt = Path.Combine(directory, "corrupt.save");
            await WithServerAsync(async stream =>
            {
                await AuthenticateAsync(stream, secret);
                await AckAsync(stream, 1, "snapshot ready");
                await SnapshotAsync(stream, bytes, new byte[32]);
            }, async port => await ThrowsAsync<ProtocolException>(() => new ControlConnection().DownloadSnapshotAsync("127.0.0.1", port, key, corrupt)));
            if (File.Exists(corrupt) || Directory.GetFiles(directory, "*.partial").Length != 0)
                throw new Exception("Corrupt backup left an output or partial file.");
            Console.WriteLine("PASS corrupt snapshots are rejected without a completed backup");

            await WithServerAsync(async stream =>
            {
                await AuthenticateAsync(stream, secret);
                await AckAsync(stream, 1, "snapshot ready");
                using var metadata = new PayloadWriter();
                metadata.WriteInt32(1); metadata.WriteInt64(256L * 1024 * 1024 + 1); metadata.WriteBytes(new byte[32], 32);
                await FrameCodec.WriteAsync(stream, metadata.ToFrame(MessageKind.SnapshotBegin));
            }, async port => await ThrowsAsync<ProtocolException>(() => new ControlConnection().DownloadSnapshotAsync("127.0.0.1", port, key, corrupt)));
            Console.WriteLine("PASS oversized snapshot metadata is rejected");
        }
        finally
        {
            string resolved = Path.GetFullPath(directory);
            string temp = Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if (resolved.StartsWith(temp, StringComparison.OrdinalIgnoreCase) && Path.GetFileName(resolved).StartsWith("coi-manager-control-", StringComparison.Ordinal))
                Directory.Delete(resolved, recursive: true);
        }
    }

    internal static async Task<string> AuthenticateAsync(Stream stream, string secret)
    {
        byte[] nonce = RandomNumberGenerator.GetBytes(32);
        using (var challenge = new PayloadWriter())
        {
            challenge.WriteBytes(nonce, 32); challenge.WriteString("test-build", 128);
            await FrameCodec.WriteAsync(stream, Welcome(0, challenge));
        }
        var frame = await FrameCodec.ReadAsync(stream) ?? throw new EndOfStreamException("Client disconnected.");
        Equal(MessageKind.Hello, frame.Kind, "hello kind");
        var hello = new PayloadReader(frame.Payload);
        string name = hello.ReadString(64);
        byte[] proof = hello.ReadBytes(32);
        hello.EnsureEnd();
        if (!ControlAuthentication.VerifyProof(secret, nonce, name, proof)) throw new Exception("Client authentication proof was invalid.");
        using (var welcome = new PayloadWriter())
        {
            welcome.WriteString("test-build", 128);
            await FrameCodec.WriteAsync(stream, Welcome(1, welcome));
        }
        var request = await FrameCodec.ReadAsync(stream) ?? throw new EndOfStreamException("Client disconnected.");
        Equal(MessageKind.Command, request.Kind, "command kind");
        var command = new PayloadReader(request.Payload);
        Equal(1u, command.ReadUInt32(), "request ID");
        string result = command.ReadString(32); command.EnsureEnd();
        return result;
    }

    internal static async Task AckAsync(Stream stream, uint id, string message)
    {
        using var reply = new PayloadWriter();
        reply.WriteUInt32(id); reply.WriteBoolean(true); reply.WriteString(message, 65536);
        await FrameCodec.WriteAsync(stream, reply.ToFrame(MessageKind.Ack));
    }

    private static ProtocolFrame Welcome(byte stage, PayloadWriter payload)
    {
        byte[] value = payload.ToFrame(MessageKind.Welcome).Payload.ToArray();
        byte[] combined = new byte[value.Length + 1]; combined[0] = stage;
        value.CopyTo(combined, 1);
        return new ProtocolFrame(MessageKind.Welcome, combined);
    }

    private static async Task SnapshotAsync(Stream stream, byte[] bytes, byte[] hash)
    {
        using (var metadata = new PayloadWriter())
        {
            metadata.WriteInt32(42); metadata.WriteInt64(bytes.Length); metadata.WriteBytes(hash, 32);
            await FrameCodec.WriteAsync(stream, metadata.ToFrame(MessageKind.SnapshotBegin));
        }
        using (var chunk = new PayloadWriter())
        {
            chunk.WriteBytes(bytes, 65536);
            await FrameCodec.WriteAsync(stream, chunk.ToFrame(MessageKind.SnapshotChunk));
        }
        await FrameCodec.WriteAsync(stream, new ProtocolFrame(MessageKind.SnapshotEnd, Array.Empty<byte>()));
    }

    private static async Task WithServerAsync(Func<Stream, Task> serve, Func<int, Task> client)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start();
        try
        {
            var server = Task.Run(async () =>
            {
                using var accepted = await listener.AcceptTcpClientAsync(timeout.Token);
                await using var stream = accepted.GetStream();
                await serve(stream);
            });
            await client(((IPEndPoint)listener.LocalEndpoint).Port).WaitAsync(timeout.Token);
            await server.WaitAsync(timeout.Token);
        }
        finally { listener.Stop(); }
    }

    private static async Task ThrowsAsync<T>(Func<Task> action) where T : Exception
    {
        try { await action(); }
        catch (T) { return; }
        throw new Exception($"Expected {typeof(T).Name}.");
    }

    private static void Equal<T>(T expected, T actual, string message)
    { if (!EqualityComparer<T>.Default.Equals(expected, actual)) throw new Exception($"{message}: expected {expected}, got {actual}."); }
}
