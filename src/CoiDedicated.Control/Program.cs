using System.Globalization;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using CoiDedicated.Protocol;

namespace CoiDedicated.Control;

internal static class Program
{
    private const long MaxSnapshotLength = 256L * 1024 * 1024;
    private const int MaxResponseByteLength = 65536;
    private const int MaxChunkLength = 65536;
    private const uint RequestId = 1;
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);
    private static readonly HashSet<string> Commands = new(StringComparer.Ordinal)
    {
        "status", "pause", "resume", "save", "snapshot", "shutdown"
    };

    public static async Task<int> Main(string[] args)
    {
        using var userCancellation = new CancellationTokenSource();
        ConsoleCancelEventHandler cancelHandler = (_, e) => { e.Cancel = true; userCancellation.Cancel(); };
        Console.CancelKeyPress += cancelHandler;
        try
        {
            Options options = Options.Parse(args);
            if (options.Help) { PrintUsage(); return 0; }
            string secret = await ReadSecretAsync(options.SecretFile!, userCancellation.Token);
            int timeoutSeconds = options.Command == "snapshot" ? 120 : 30;
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(userCancellation.Token);
            timeout.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));
            await RunAsync(options, secret, timeout.Token);
            return 0;
        }
        catch (OperationCanceledException)
        {
            Console.Error.WriteLine(userCancellation.IsCancellationRequested
                ? "Control request canceled."
                : "Control request timed out (30 seconds; 120 seconds for snapshots).");
            return 1;
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or SocketException or UnauthorizedAccessException or CryptographicException)
        {
            Console.Error.WriteLine($"Control request failed: {ex.Message}");
            return 1;
        }
        finally { Console.CancelKeyPress -= cancelHandler; }
    }

    private static async Task RunAsync(Options options, string secret, CancellationToken cancellationToken)
    {
        if (options.Output is not null && File.Exists(options.Output))
            throw new IOException("Snapshot destination already exists; choose a new --output path.");
        using var client = new TcpClient { NoDelay = true };
        await client.ConnectAsync(options.Host, options.Port, cancellationToken);
        await using NetworkStream stream = client.GetStream();

        var challenge = ReadWelcome(await ReadFrameAsync(stream, cancellationToken), expectedStage: 0);
        byte[] nonce = challenge.ReadBytes(ControlAuthentication.NonceLength);
        if (nonce.Length != ControlAuthentication.NonceLength)
            throw new ProtocolException("Server supplied an invalid control nonce.");
        string advertisedBuild = challenge.ReadString(128);
        challenge.EnsureEnd();

        byte[] proof = ControlAuthentication.ComputeProof(secret, nonce, options.Name);
        try
        {
            using var hello = new PayloadWriter();
            hello.WriteString(options.Name, ControlAuthentication.MaxClientNameByteLength);
            hello.WriteBytes(proof, ControlAuthentication.ProofLength);
            await FrameCodec.WriteAsync(stream, hello.ToFrame(MessageKind.Hello), cancellationToken);
        }
        finally { CryptographicOperations.ZeroMemory(proof); }

        var accepted = ReadWelcome(await ReadFrameAsync(stream, cancellationToken), expectedStage: 1);
        string authenticatedBuild = accepted.ReadString(128);
        accepted.EnsureEnd();
        if (!string.Equals(advertisedBuild, authenticatedBuild, StringComparison.Ordinal))
            throw new ProtocolException("Server build changed during the control handshake.");

        using (var command = new PayloadWriter())
        {
            command.WriteUInt32(RequestId);
            command.WriteString(options.Command, 32);
            await FrameCodec.WriteAsync(stream, command.ToFrame(MessageKind.Command), cancellationToken);
        }

        ProtocolFrame acknowledgement = await ReadFrameAsync(stream, cancellationToken);
        ExpectKind(acknowledgement, MessageKind.Ack);
        var ack = new PayloadReader(acknowledgement.Payload);
        uint requestId = ack.ReadUInt32();
        bool success = ack.ReadBoolean();
        string response = ack.ReadString(MaxResponseByteLength);
        ack.EnsureEnd();
        if (requestId != RequestId) throw new ProtocolException("Response does not match the command request ID.");
        if (!success) throw new ProtocolException($"Server rejected {options.Command}: {response}");
        Console.WriteLine(response);

        if (options.Command == "snapshot")
            await ReceiveSnapshotAsync(stream, options.Output!, cancellationToken);
    }

    private static PayloadReader ReadWelcome(ProtocolFrame frame, byte expectedStage)
    {
        ExpectKind(frame, MessageKind.Welcome);
        if (frame.Payload.Length < 1 || frame.Payload.Span[0] != expectedStage)
            throw new ProtocolException($"Expected Welcome stage {expectedStage}.");
        return new PayloadReader(frame.Payload.Slice(1));
    }

    private static async Task ReceiveSnapshotAsync(Stream stream, string output, CancellationToken cancellationToken)
    {
        ProtocolFrame begin = await ReadFrameAsync(stream, cancellationToken);
        ExpectKind(begin, MessageKind.SnapshotBegin);
        var metadata = new PayloadReader(begin.Payload);
        int simulationStep = metadata.ReadInt32();
        long length = metadata.ReadInt64();
        byte[] expectedHash = metadata.ReadBytes(32);
        metadata.EnsureEnd();
        if (length < 0 || length > MaxSnapshotLength)
            throw new ProtocolException("Snapshot length exceeds the 256 MiB limit or is negative.");
        if (expectedHash.Length != 32) throw new ProtocolException("Snapshot SHA-256 must be 32 bytes.");

        Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        string temporary = output + ".tmp";
        bool ownsTemporary = false;
        try
        {
            byte[] actualHash;
            await using (var destination = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 65536, FileOptions.Asynchronous | FileOptions.SequentialScan))
            {
                ownsTemporary = true;
                using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                long received = 0;
                while (received < length)
                {
                    ProtocolFrame frame = await ReadFrameAsync(stream, cancellationToken);
                    ExpectKind(frame, MessageKind.SnapshotChunk);
                    var reader = new PayloadReader(frame.Payload);
                    byte[] bytes = reader.ReadBytes(MaxChunkLength);
                    reader.EnsureEnd();
                    if (bytes.Length == 0 || bytes.Length > length - received)
                        throw new ProtocolException("Snapshot chunk is empty or exceeds the advertised snapshot length.");
                    await destination.WriteAsync(bytes, cancellationToken);
                    hash.AppendData(bytes);
                    received += bytes.Length;
                }

                ProtocolFrame end = await ReadFrameAsync(stream, cancellationToken);
                ExpectKind(end, MessageKind.SnapshotEnd);
                new PayloadReader(end.Payload).EnsureEnd();
                actualHash = hash.GetHashAndReset();
                if (!CryptographicOperations.FixedTimeEquals(expectedHash, actualHash))
                    throw new ProtocolException("Snapshot SHA-256 verification failed.");
                await destination.FlushAsync(cancellationToken);
            }

            cancellationToken.ThrowIfCancellationRequested();
            // The sibling staging file makes this a rename on the same filesystem.
            // A destination created concurrently is preserved rather than overwritten.
            File.Move(temporary, output, overwrite: false);
            ownsTemporary = false;
            Console.WriteLine($"Snapshot saved: {output}");
            Console.WriteLine($"Step {simulationStep}; {length} bytes; SHA-256 {Convert.ToHexString(actualHash).ToLowerInvariant()}");
        }
        finally
        {
            if (ownsTemporary)
            {
                try { File.Delete(temporary); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    Console.Error.WriteLine($"Could not remove incomplete snapshot file: {temporary}");
                }
            }
        }
    }

    private static async Task<ProtocolFrame> ReadFrameAsync(Stream stream, CancellationToken cancellationToken)
    {
        ProtocolFrame frame = await FrameCodec.ReadAsync(stream, cancellationToken)
            ?? throw new EndOfStreamException("The server closed the control connection before completing its response.");
        if (frame.Kind == MessageKind.Error)
        {
            var reader = new PayloadReader(frame.Payload);
            string message = reader.ReadString(MaxResponseByteLength);
            reader.EnsureEnd();
            throw new ProtocolException($"Server error: {message}");
        }
        return frame;
    }

    private static void ExpectKind(ProtocolFrame frame, MessageKind kind)
    {
        if (frame.Kind != kind) throw new ProtocolException($"Expected {kind}, received {frame.Kind}.");
    }

    private static async Task<string> ReadSecretAsync(string path, CancellationToken cancellationToken)
    {
        var file = new FileInfo(path);
        if (!file.Exists) throw new FileNotFoundException("Control secret file does not exist.", path);
        if (file.Length > 16384) throw new IOException("Control secret file exceeds 16 KiB.");
        string secret = (await File.ReadAllTextAsync(path, StrictUtf8, cancellationToken)).TrimEnd('\r', '\n');
        if (string.IsNullOrEmpty(secret)) throw new IOException("Control secret file is empty.");
        return secret;
    }

    private static void PrintUsage()
    {
        Console.WriteLine("CoI dedicated host control client");
        Console.WriteLine("  --secret-file PATH                 Required UTF-8 control secret file");
        Console.WriteLine("  --host HOST                        Default: 127.0.0.1");
        Console.WriteLine("  --port PORT                        Default: 28550");
        Console.WriteLine("  --name NAME                        Default: coi-control; max 64 UTF-8 bytes");
        Console.WriteLine("  --command COMMAND                  Default: status");
        Console.WriteLine("                                     status, pause, resume, save, snapshot, shutdown");
        Console.WriteLine("  --output PATH                      Required for snapshot; must not already exist");
        Console.WriteLine("  --help                             Show this help");
    }

    private sealed class Options
    {
        public string Host { get; private set; } = "127.0.0.1";
        public int Port { get; private set; } = 28550;
        public string Name { get; private set; } = "coi-control";
        public string Command { get; private set; } = "status";
        public string? SecretFile { get; private set; }
        public string? Output { get; private set; }
        public bool Help { get; private set; }

        public static Options Parse(string[] args)
        {
            var result = new Options();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            for (int index = 0; index < args.Length; index++)
            {
                string option = args[index];
                if (!seen.Add(option)) throw new ArgumentException($"Duplicate option: {option}");
                if (option is "--help" or "-h") { result.Help = true; continue; }
                if (option is not ("--host" or "--port" or "--name" or "--command" or "--secret-file" or "--output"))
                    throw new ArgumentException($"Unknown option: {option}. Use --help for usage.");
                if (++index >= args.Length || args[index].StartsWith("--", StringComparison.Ordinal))
                    throw new ArgumentException($"Missing value for {option}.");
                string value = args[index];
                switch (option)
                {
                    case "--host": result.Host = value; break;
                    case "--name": result.Name = value; break;
                    case "--command": result.Command = value; break;
                    case "--secret-file": result.SecretFile = Path.GetFullPath(value); break;
                    case "--output": result.Output = Path.GetFullPath(value); break;
                    case "--port":
                        if (!int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out int port) || port is < 1 or > 65535)
                            throw new ArgumentException("Port must be an integer from 1 to 65535.");
                        result.Port = port;
                        break;
                }
            }
            if (result.Help) return result;
            if (result.SecretFile is null) throw new ArgumentException("--secret-file is required. Use --help for usage.");
            if (string.IsNullOrWhiteSpace(result.Host)) throw new ArgumentException("Host cannot be empty.");
            if (string.IsNullOrWhiteSpace(result.Name) || StrictUtf8.GetByteCount(result.Name) > ControlAuthentication.MaxClientNameByteLength)
                throw new ArgumentException("Client name must be nonempty and at most 64 UTF-8 bytes.");
            if (!Commands.Contains(result.Command)) throw new ArgumentException("Command must be status, pause, resume, save, snapshot, or shutdown.");
            if (result.Command == "snapshot" && result.Output is null) throw new ArgumentException("--output is required for snapshot.");
            if (result.Command != "snapshot" && result.Output is not null) throw new ArgumentException("--output is only valid for snapshot.");
            return result;
        }
    }
}
