using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using CoiDedicated;
using CoiDedicated.Protocol;

class Tests
{
    static async Task Main()
    {
        string directory = Path.Combine(Path.GetTempPath(), "coi-control-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try { await TestControlLeases(directory); }
        finally
        {
            foreach (string file in Directory.EnumerateFiles(directory)) File.Delete(file);
            Directory.Delete(directory);
        }
    }

    static async Task TestControlLeases(string directory)
    {
        string path = Path.Combine(directory, "island.save");
        byte[] original = Encoding.UTF8.GetBytes("original snapshot generation at step seven");
        File.WriteAllBytes(path, original);
        const string secret = "review-fixture-secret-32-bytes";
        using var server = new DedicatedControlServer("127.0.0.1", 0, secret, "fixture");
        var listener = (TcpListener)typeof(DedicatedControlServer).GetField("listener", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(server)!;
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var queue = (ConcurrentQueue<ControlRequest>)typeof(DedicatedControlServer).GetField("requests", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(server)!;
        Task download = Task.Run(async () =>
        {
            using var client = await Request(port, secret);
            var stream = client.GetStream();
            var ack = await FrameCodec.ReadAsync(stream) ?? throw new Exception("Missing acknowledgment");
            if (ack.Kind != MessageKind.Ack) throw new Exception("Expected acknowledgment, received " + ack.Kind + ": " + new PayloadReader(ack.Payload).ReadString(65536));
            var begin = new PayloadReader((await FrameCodec.ReadAsync(stream))!.Payload);
            if (begin.ReadInt32() != 7 || begin.ReadInt64() != original.Length) throw new Exception("Metadata generation changed");
            byte[] expectedHash = begin.ReadBytes(32); begin.EnsureEnd();
            using var content = new MemoryStream();
            while (true)
            {
                var frame = await FrameCodec.ReadAsync(stream) ?? throw new Exception("Snapshot incomplete");
                if (frame.Kind == MessageKind.SnapshotEnd) break;
                var reader = new PayloadReader(frame.Payload); content.Write(reader.ReadBytes(65536)); reader.EnsureEnd();
            }
            if (!content.ToArray().SequenceEqual(original) || !SHA256.HashData(original).SequenceEqual(expectedHash)) throw new Exception("Snapshot read replacement generation");
        });
        await WaitFor(() => !queue.IsEmpty);
        FileStream? pinned = null;
        server.Pump(_ =>
        {
            pinned = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete);
            string replacement = path + ".replacement";
            File.WriteAllText(replacement, "later generation after simulation advanced");
            File.Replace(replacement, path, path + ".previous");
            // A second autosave replaces the backup that now contains the pinned
            // generation, matching the production save rotation on Windows.
            File.WriteAllText(replacement, "second later generation");
            File.Delete(path + ".previous");
            File.Replace(replacement, path, path + ".previous");
            return new ControlReply("ready", pinned, 7);
        });
        await download.WaitAsync(TimeSpan.FromSeconds(10));
        await WaitFor(() => pinned!.SafeFileHandle.IsClosed);
        Console.WriteLine("PASS snapshot bytes and step retain pinned generation after atomic path replacement");

        var abandoned = new TaskCompletionSource<ControlReply>();
        FileStream? canceledLease = null;
        queue.Enqueue(new ControlRequest("snapshot", CancellationToken.None, abandoned));
        server.Pump(_ =>
        {
            canceledLease = File.OpenRead(path);
            abandoned.TrySetCanceled(); // Deadline wins while simulation creates its reply.
            return new ControlReply("abandoned", canceledLease, 8);
        });
        if (!canceledLease!.SafeFileHandle.IsClosed) throw new Exception("Canceled completion leaked its snapshot lease");
        Console.WriteLine("PASS cancellation during reply creation disposes snapshot lease");

        using (var dead = await Request(port, secret))
        {
            await WaitFor(() => !queue.IsEmpty);
            dead.LingerState = new LingerOption(true, 0);
            dead.Close();
        }
        await Task.Delay(50);
        FileStream? failedAckLease = null;
        server.Pump(_ => new ControlReply("dead", failedAckLease = File.OpenRead(path), 9));
        await WaitFor(() => failedAckLease!.SafeFileHandle.IsClosed);
        Console.WriteLine("PASS disconnected client disposes reply lease when acknowledgment fails");

        using var denied = await Request(port, secret);
        await WaitFor(() => !queue.IsEmpty);
        server.Pump(_ => throw new UnauthorizedAccessException("Fixture save is not writable."));
        var fault = await FrameCodec.ReadAsync(denied.GetStream()) ?? throw new Exception("Save failure silently disconnected the client.");
        if (fault.Kind != MessageKind.Error) throw new Exception("Expected explicit operational error.");
        var faultReader = new PayloadReader(fault.Payload);
        if (faultReader.ReadString(65536) != "Fixture save is not writable.") throw new Exception("Save failure detail was lost.");
        faultReader.EnsureEnd();
        Console.WriteLine("PASS ordinary save failure sends an explicit error response");
    }
    static async Task<TcpClient> Request(int port, string secret)
    {
        var client = new TcpClient(); await client.ConnectAsync(IPAddress.Loopback, port);
        var stream = client.GetStream();
        var challenge = (await FrameCodec.ReadAsync(stream))!;
        var reader = new PayloadReader(challenge.Payload.Slice(1));
        byte[] nonce = reader.ReadBytes(32); reader.ReadString(128); reader.EnsureEnd();
        using (var hello = new PayloadWriter())
        {
            hello.WriteString("review", 64); hello.WriteBytes(ControlAuthentication.ComputeProof(secret, nonce, "review"), 32);
            await FrameCodec.WriteAsync(stream, hello.ToFrame(MessageKind.Hello));
        }
        await FrameCodec.ReadAsync(stream);
        using (var command = new PayloadWriter())
        {
            command.WriteUInt32(1); command.WriteString("snapshot", 32);
            await FrameCodec.WriteAsync(stream, command.ToFrame(MessageKind.Command));
        }
        return client;
    }
    static async Task WaitFor(Func<bool> condition)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        while (!condition()) await Task.Delay(10, timeout.Token);
    }
}
