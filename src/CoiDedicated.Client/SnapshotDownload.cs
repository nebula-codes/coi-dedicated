using System;
using System.IO;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using CoiDedicated.Protocol;

namespace CoiDedicated.Client;

internal static class SnapshotDownload
{
    private const long MaximumLength = 256L * 1024 * 1024;

    internal static async Task<int> DownloadAsync(string host, int port, string keyPath, string output, string expectedBuild, CancellationToken cancellation)
    {
        var keyFile = new FileInfo(keyPath);
        if (!keyFile.Exists || keyFile.Length > 16384)
            throw new IOException("Shared key file is missing or exceeds 16 KiB.");
        string secret = File.ReadAllText(keyPath, new UTF8Encoding(false, true)).TrimEnd('\r', '\n');
        if (secret.Length < 16) throw new IOException("Shared key must contain at least 16 characters.");

        using var client = new TcpClient { NoDelay = true };
        using var cancelConnection = cancellation.Register(() => client.Close());
        await client.ConnectAsync(host, port).ConfigureAwait(false);
        using NetworkStream stream = client.GetStream();
        var challenge = Welcome(await ReadAsync(stream, cancellation).ConfigureAwait(false), 0);
        byte[] nonce = challenge.ReadBytes(ControlAuthentication.NonceLength);
        string build = challenge.ReadString(128);
        challenge.EnsureEnd();
        if (!string.Equals(build, expectedBuild, StringComparison.Ordinal))
            throw new ProtocolException("Game assembly fingerprint or protocol differs from the server. Use exactly matching game files.");

        const string name = "coi-snapshot-preview";
        byte[] proof = ControlAuthentication.ComputeProof(secret, nonce, name);
        try
        {
            using var hello = new PayloadWriter();
            hello.WriteString(name, ControlAuthentication.MaxClientNameByteLength);
            hello.WriteBytes(proof, ControlAuthentication.ProofLength);
            await FrameCodec.WriteAsync(stream, hello.ToFrame(MessageKind.Hello), cancellation).ConfigureAwait(false);
        }
        finally { CryptographicOperations.ZeroMemory(proof); }

        var accepted = Welcome(await ReadAsync(stream, cancellation).ConfigureAwait(false), 1);
        if (!string.Equals(build, accepted.ReadString(128), StringComparison.Ordinal))
            throw new ProtocolException("Server build changed during handshake.");
        accepted.EnsureEnd();
        using (var command = new PayloadWriter())
        {
            command.WriteUInt32(1);
            command.WriteString("snapshot", 32);
            await FrameCodec.WriteAsync(stream, command.ToFrame(MessageKind.Command), cancellation).ConfigureAwait(false);
        }
        var ack = new PayloadReader((await ExpectAsync(stream, MessageKind.Ack, cancellation).ConfigureAwait(false)).Payload);
        uint requestId = ack.ReadUInt32();
        bool success = ack.ReadBoolean();
        string response = ack.ReadString(65536);
        ack.EnsureEnd();
        if (requestId != 1 || !success) throw new ProtocolException("Snapshot request failed: " + response);

        return await ReceiveAsync(stream, output, cancellation).ConfigureAwait(false);
    }

    internal static async Task<int> ReceiveAsync(Stream stream, string output, CancellationToken cancellation)
    {
        var begin = new PayloadReader((await ExpectAsync(stream, MessageKind.SnapshotBegin, cancellation).ConfigureAwait(false)).Payload);
        int step = begin.ReadInt32();
        long length = begin.ReadInt64();
        byte[] expectedDigest = begin.ReadBytes(32);
        begin.EnsureEnd();
        if (step < 0 || length <= 32 || length > MaximumLength || expectedDigest.Length != 32)
            throw new ProtocolException("Invalid snapshot metadata.");

        Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        string temporary = output + ".part";
        bool ownsTemporary = false;
        try
        {
            using (var file = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 65536, true))
            using (var digest = IncrementalHash.CreateHash(HashAlgorithmName.SHA256))
            {
                ownsTemporary = true;
                long received = 0;
                while (received < length)
                {
                    var chunk = new PayloadReader((await ExpectAsync(stream, MessageKind.SnapshotChunk, cancellation).ConfigureAwait(false)).Payload);
                    byte[] bytes = chunk.ReadBytes(65536);
                    chunk.EnsureEnd();
                    if (bytes.Length == 0 || bytes.Length > length - received)
                        throw new ProtocolException("Snapshot chunk exceeds advertised length or is empty.");
                    await file.WriteAsync(bytes, 0, bytes.Length, cancellation).ConfigureAwait(false);
                    digest.AppendData(bytes);
                    received += bytes.Length;
                }
                new PayloadReader((await ExpectAsync(stream, MessageKind.SnapshotEnd, cancellation).ConfigureAwait(false)).Payload).EnsureEnd();
                if (!CryptographicOperations.FixedTimeEquals(expectedDigest, digest.GetHashAndReset()))
                    throw new ProtocolException("Snapshot SHA-256 did not match.");
                await file.FlushAsync(cancellation).ConfigureAwait(false);
            }
            cancellation.ThrowIfCancellationRequested();
            File.Move(temporary, output);
            ownsTemporary = false;
            return step;
        }
        finally
        {
            if (ownsTemporary)
            {
                try { File.Delete(temporary); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        }
    }

    private static PayloadReader Welcome(ProtocolFrame frame, byte stage)
    {
        if (frame.Kind != MessageKind.Welcome || frame.Payload.Length < 1 || frame.Payload.Span[0] != stage)
            throw new ProtocolException("Unexpected authentication response.");
        return new PayloadReader(frame.Payload.Slice(1));
    }

    private static async Task<ProtocolFrame> ExpectAsync(Stream stream, MessageKind kind, CancellationToken cancellation)
    {
        ProtocolFrame frame = await ReadAsync(stream, cancellation).ConfigureAwait(false);
        if (frame.Kind != kind) throw new ProtocolException($"Expected {kind}, received {frame.Kind}.");
        return frame;
    }

    private static async Task<ProtocolFrame> ReadAsync(Stream stream, CancellationToken cancellation)
    {
        ProtocolFrame frame = await FrameCodec.ReadAsync(stream, cancellation).ConfigureAwait(false)
            ?? throw new EndOfStreamException("Server disconnected before snapshot completed.");
        if (frame.Kind == MessageKind.Error)
        {
            var error = new PayloadReader(frame.Payload);
            string message = error.ReadString(65536);
            error.EnsureEnd();
            throw new ProtocolException("Server: " + message);
        }
        return frame;
    }
}
