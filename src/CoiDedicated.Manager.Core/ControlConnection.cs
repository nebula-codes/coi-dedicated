using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using CoiDedicated.Protocol;

namespace CoiDedicated.Manager.Core;

public sealed record ControlResponse(string Build, string Message);
public sealed record SnapshotDownload(int Step, long Length, string Sha256, string Path);

/// <summary>One authenticated, bounded control request per connection.</summary>
public sealed class ControlConnection
{
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);
    private static readonly HashSet<string> Commands = new(StringComparer.Ordinal)
        { "status", "pause", "resume", "save", "snapshot", "shutdown" };
    private readonly TimeSpan _timeout;

    public ControlConnection(TimeSpan? timeout = null) => _timeout = timeout ?? TimeSpan.FromSeconds(10);

    public async Task<ControlResponse> ExecuteAsync(string host, int port, string secretFile, string command, CancellationToken cancellationToken = default)
    {
        if (!Commands.Contains(command) || command == "snapshot") throw new ArgumentException("Unsupported control command.", nameof(command));
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(_timeout);
        try
        {
            using var client = new TcpClient { NoDelay = true };
            await client.ConnectAsync(host, port, deadline.Token).ConfigureAwait(false);
            await using var stream = client.GetStream();
            return await AuthenticateAndRequestAsync(stream, secretFile, command, deadline.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        { throw new TimeoutException($"The server did not answer the {command} request in time."); }
    }

    public async Task<SnapshotDownload> DownloadSnapshotAsync(string host, int port, string secretFile, string outputPath, CancellationToken cancellationToken = default)
    {
        string output = Path.GetFullPath(outputPath);
        if (File.Exists(output)) throw new IOException("Backup destination already exists.");
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(120));
        string temporary = output + "." + Guid.NewGuid().ToString("N") + ".partial";
        bool ownsTemporary = false;
        try
        {
            using var client = new TcpClient { NoDelay = true };
            await client.ConnectAsync(host, port, deadline.Token).ConfigureAwait(false);
            await using var stream = client.GetStream();
            await AuthenticateAndRequestAsync(stream, secretFile, "snapshot", deadline.Token).ConfigureAwait(false);
            var begin = await ReadFrameAsync(stream, deadline.Token).ConfigureAwait(false);
            ExpectKind(begin, MessageKind.SnapshotBegin);
            var metadata = new PayloadReader(begin.Payload);
            int step = metadata.ReadInt32();
            long length = metadata.ReadInt64();
            byte[] expectedHash = metadata.ReadBytes(32);
            metadata.EnsureEnd();
            if (length < 0 || length > 256L * 1024 * 1024 || expectedHash.Length != 32)
                throw new ProtocolException("Server supplied invalid backup metadata.");
            Directory.CreateDirectory(Path.GetDirectoryName(output)!);
            byte[] actualHash;
            await using (var file = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 65536, FileOptions.Asynchronous | FileOptions.SequentialScan))
            {
                ownsTemporary = true;
                using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                long received = 0;
                while (received < length)
                {
                    var frame = await ReadFrameAsync(stream, deadline.Token).ConfigureAwait(false);
                    ExpectKind(frame, MessageKind.SnapshotChunk);
                    var payload = new PayloadReader(frame.Payload);
                    byte[] chunk = payload.ReadBytes(65536);
                    payload.EnsureEnd();
                    if (chunk.Length == 0 || chunk.Length > length - received)
                        throw new ProtocolException("Backup chunk exceeds the announced length or is empty.");
                    await file.WriteAsync(chunk, deadline.Token).ConfigureAwait(false);
                    hash.AppendData(chunk);
                    received += chunk.Length;
                }
                var end = await ReadFrameAsync(stream, deadline.Token).ConfigureAwait(false);
                ExpectKind(end, MessageKind.SnapshotEnd);
                new PayloadReader(end.Payload).EnsureEnd();
                actualHash = hash.GetHashAndReset();
                if (!CryptographicOperations.FixedTimeEquals(actualHash, expectedHash))
                    throw new ProtocolException("Backup checksum verification failed.");
                await file.FlushAsync(deadline.Token).ConfigureAwait(false);
                file.Flush(flushToDisk: true);
            }
            deadline.Token.ThrowIfCancellationRequested();
            File.Move(temporary, output, overwrite: false);
            ownsTemporary = false;
            return new SnapshotDownload(step, length, Convert.ToHexString(actualHash).ToLowerInvariant(), output);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        { throw new TimeoutException("The server did not finish the backup within two minutes."); }
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

    private static async Task<ControlResponse> AuthenticateAndRequestAsync(Stream stream, string secretFile, string command, CancellationToken cancellationToken)
    {
        var file = new FileInfo(secretFile);
        if (!file.Exists) throw new FileNotFoundException("The server control key has not been created yet.", secretFile);
        if (file.Length > 16384) throw new IOException("The server control key file exceeds 16 KiB.");
        string secret = (await File.ReadAllTextAsync(secretFile, StrictUtf8, cancellationToken).ConfigureAwait(false)).TrimEnd('\r', '\n');
        if (secret.Length < 16) throw new IOException("The server control key file is too short.");
        var challenge = ReadWelcome(await ReadFrameAsync(stream, cancellationToken).ConfigureAwait(false), 0);
        byte[] nonce = challenge.ReadBytes(ControlAuthentication.NonceLength);
        if (nonce.Length != ControlAuthentication.NonceLength) throw new ProtocolException("Invalid authentication challenge.");
        string build = challenge.ReadString(128);
        challenge.EnsureEnd();
        const string name = "coi-server-manager";
        byte[] proof = ControlAuthentication.ComputeProof(secret, nonce, name);
        try
        {
            using var hello = new PayloadWriter();
            hello.WriteString(name, 64);
            hello.WriteBytes(proof, 32);
            await FrameCodec.WriteAsync(stream, hello.ToFrame(MessageKind.Hello), cancellationToken).ConfigureAwait(false);
        }
        finally { CryptographicOperations.ZeroMemory(proof); }
        var accepted = ReadWelcome(await ReadFrameAsync(stream, cancellationToken).ConfigureAwait(false), 1);
        string confirmedBuild = accepted.ReadString(128);
        accepted.EnsureEnd();
        if (!StringComparer.Ordinal.Equals(build, confirmedBuild)) throw new ProtocolException("Server identity changed during authentication.");
        using (var request = new PayloadWriter())
        {
            request.WriteUInt32(1);
            request.WriteString(command, 32);
            await FrameCodec.WriteAsync(stream, request.ToFrame(MessageKind.Command), cancellationToken).ConfigureAwait(false);
        }
        var reply = await ReadFrameAsync(stream, cancellationToken).ConfigureAwait(false);
        ExpectKind(reply, MessageKind.Ack);
        var ack = new PayloadReader(reply.Payload);
        uint id = ack.ReadUInt32();
        bool success = ack.ReadBoolean();
        string message = ack.ReadString(65536);
        ack.EnsureEnd();
        if (id != 1) throw new ProtocolException("Server returned an unexpected request ID.");
        if (!success) throw new ProtocolException($"Server rejected {command}: {message}");
        return new ControlResponse(build, message);
    }

    private static PayloadReader ReadWelcome(ProtocolFrame frame, byte stage)
    {
        ExpectKind(frame, MessageKind.Welcome);
        if (frame.Payload.Length < 1 || frame.Payload.Span[0] != stage) throw new ProtocolException("Unexpected authentication stage.");
        return new PayloadReader(frame.Payload.Slice(1));
    }

    private static async Task<ProtocolFrame> ReadFrameAsync(Stream stream, CancellationToken cancellationToken)
    {
        var frame = await FrameCodec.ReadAsync(stream, cancellationToken).ConfigureAwait(false)
            ?? throw new EndOfStreamException("The server closed its control connection before completing the response.");
        if (frame.Kind == MessageKind.Error)
        {
            var error = new PayloadReader(frame.Payload);
            string message = error.ReadString(65536);
            error.EnsureEnd();
            throw new ProtocolException($"Server error: {message}");
        }
        return frame;
    }

    private static void ExpectKind(ProtocolFrame frame, MessageKind kind)
    {
        if (frame.Kind != kind) throw new ProtocolException($"Expected {kind}, received {frame.Kind}.");
    }
}
