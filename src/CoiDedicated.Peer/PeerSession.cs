using System.Net.Sockets;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CoiDedicated.GameAdapter;
using CoiDedicated.Protocol;
using Mafi;
using Mafi.Core;
using Mafi.Core.Entities;
using Mafi.Core.Entities.Static;
using Mafi.Core.Entities.Static.Commands;
using Mafi.Core.Entities.Static.Layout;
using Mafi.Core.Factory.Machines;
using Mafi.Core.Input;
using Mafi.Core.Prototypes;
using Mafi.Core.SaveGame;
using Mafi.Core.Simulation;
using Mafi.Core.Terrain;

namespace CoiDedicated.Peer;

internal static class PeerSession
{
    private const long MaxSnapshotLength = 256L * 1024 * 1024;

    internal static int Run(PeerOptions options, CancellationToken cancellationToken)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(options.TimeoutSeconds));
        CancellationToken token = deadline.Token;
        string secret = File.ReadAllText(options.SecretFile, new UTF8Encoding(false, true)).TrimEnd('\r', '\n');
        using var client = new TcpClient { NoDelay = true };
        client.ConnectAsync(options.Host, options.Port, token).GetAwaiter().GetResult();
        using NetworkStream stream = client.GetStream();
        Authenticate(stream, options.Name, secret, GameBuildIdentity.Compute(Path.Combine(options.GameDirectory, "Captain of Industry_Data", "Managed")), token);
        (string snapshotFile, int snapshotStep) = ReceiveSnapshot(stream, options.DataDirectory, token);
        Console.WriteLine($"[peer] Verified snapshot at step {snapshotStep}; loading {snapshotFile}");
        var hostOptions = new HostOptions(options.GameDirectory, options.DataDirectory, "Sandbox", snapshotFile, 0, false, false);
        return GameHost.Run(hostOptions, (resolver, runner) =>
            Replay(options, stream, snapshotStep, resolver, runner, token));
    }

    private static void Authenticate(Stream stream, string name, string secret, string expectedBuild, CancellationToken token)
    {
        ProtocolFrame challengeFrame = ReadFrame(stream, token);
        var challenge = Welcome(challengeFrame, 0);
        byte[] nonce = challenge.ReadBytes(32);
        string build = challenge.ReadString(128);
        challenge.EnsureEnd();
        if (!StringComparer.Ordinal.Equals(build, expectedBuild))
            throw new ProtocolException("Server simulation binaries or command protocol do not match this installation.");
        byte[] proof = ControlAuthentication.ComputeProof(secret, nonce, name);
        try
        {
            using var hello = new PayloadWriter();
            hello.WriteString(name, 64);
            hello.WriteBytes(proof, 32);
            Send(stream, hello.ToFrame(MessageKind.Hello), token);
        }
        finally { CryptographicOperations.ZeroMemory(proof); }
        var accepted = Welcome(ReadFrame(stream, token), 1);
        if (accepted.ReadString(128) != build) throw new ProtocolException("Server build changed during handshake.");
        accepted.EnsureEnd();
        Console.WriteLine($"[peer] Authenticated with {build}");
    }

    private static (string Path, int Step) ReceiveSnapshot(Stream stream, string directory, CancellationToken token)
    {
        ProtocolFrame begin = ReadFrame(stream, token);
        Expect(begin, MessageKind.SnapshotBegin);
        var metadata = new PayloadReader(begin.Payload);
        int step = metadata.ReadInt32();
        long length = metadata.ReadInt64();
        byte[] expectedHash = metadata.ReadBytes(32);
        metadata.EnsureEnd();
        if (step < 0 || length < 0 || length > MaxSnapshotLength || expectedHash.Length != 32)
            throw new ProtocolException("Invalid snapshot metadata.");
        string path = Path.Combine(directory, "join-" + Guid.NewGuid().ToString("N") + ".save");
        string temporary = path + ".tmp";
        bool ownsTemporary = false;
        try
        {
            using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                ownsTemporary = true;
                using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                long received = 0;
                while (received < length)
                {
                    ProtocolFrame chunk = ReadFrame(stream, token);
                    Expect(chunk, MessageKind.SnapshotChunk);
                    var reader = new PayloadReader(chunk.Payload);
                    byte[] bytes = reader.ReadBytes(65536);
                    reader.EnsureEnd();
                    if (bytes.Length == 0 || bytes.Length > length - received) throw new ProtocolException("Snapshot chunk exceeds advertised length.");
                    output.Write(bytes);
                    hash.AppendData(bytes);
                    received += bytes.Length;
                }
                ProtocolFrame end = ReadFrame(stream, token);
                Expect(end, MessageKind.SnapshotEnd);
                new PayloadReader(end.Payload).EnsureEnd();
                if (!CryptographicOperations.FixedTimeEquals(expectedHash, hash.GetHashAndReset()))
                    throw new ProtocolException("Snapshot hash mismatch.");
                output.Flush(flushToDisk: true);
            }
            token.ThrowIfCancellationRequested();
            File.Move(temporary, path);
            ownsTemporary = false;
            return (path, step);
        }
        finally
        {
            if (ownsTemporary) File.Delete(temporary);
        }
    }

    private static void Replay(PeerOptions options, Stream stream, int snapshotStep, DependencyResolver resolver, object runner, CancellationToken token)
    {
        Type runnerType = runner.GetType();
        runnerType.GetProperty("ExtraSimUpdateMs")!.SetValue(runner, 0.ToFix32());
        runnerType.GetProperty("RunSimulationInBackgroundThread")!.SetValue(runner, false);
        MethodInfo update = runnerType.GetMethod("Update")!;
        var simulation = resolver.Resolve<SimLoopEvents>();
        var scheduler = resolver.Resolve<IInputScheduler>();
        simulation.SetSimSpeed(1);
        if (simulation.CurrentStep.Value != snapshotStep)
            throw new ProtocolException($"Loaded step {simulation.CurrentStep.Value} differs from snapshot step {snapshotStep}.");
        using (var ready = new PayloadWriter())
        {
            ready.WriteInt32(snapshotStep);
            Send(stream, ready.ToFrame(MessageKind.Ack), token);
        }
        Console.WriteLine($"[peer] Ready at step {snapshotStep}");

        ulong lastSequence = 0, nextRequestId = 1;
        bool sawTick = false;
        var commandsSeen = new List<CommandObservation>();
        var commandsRejected = new List<CommandRejection>();
        var pending = new HashSet<ulong>();
        for (int localTick = 1; localTick <= options.Ticks; localTick++)
        {
            token.ThrowIfCancellationRequested();
            ProtocolFrame frame;
            while (true)
            {
                frame = ReadFrame(stream, token);
                if (frame.Kind != MessageKind.CommandRejected) break;
                var rejection = new PayloadReader(frame.Payload);
                ulong rejectedId = rejection.ReadUInt64();
                string reason = rejection.ReadString(65536);
                rejection.EnsureEnd();
                if (!pending.Remove(rejectedId))
                    throw new ProtocolException($"Server rejected an unknown or already completed request {rejectedId}.");
                commandsRejected.Add(new CommandRejection(rejectedId, reason));
                Console.WriteLine($"[peer] Request {rejectedId} rejected: {reason}");
            }
            Expect(frame, MessageKind.Tick);
            var reader = new PayloadReader(frame.Payload);
            ulong sequence = reader.ReadUInt64();
            int beforeStep = reader.ReadInt32();
            int afterStep = reader.ReadInt32();
            bool beforePaused = reader.ReadBoolean();
            ushort count = reader.ReadUInt16();
            if (count > 256) throw new ProtocolException("Tick command count exceeds 256.");
            if (sawTick && sequence != lastSequence + 1) throw new ProtocolException("Tick sequence is not contiguous.");
            if (beforeStep != simulation.CurrentStep.Value)
                throw new ProtocolException($"Tick {sequence}: before-step mismatch, server {beforeStep}, peer {simulation.CurrentStep.Value}.");

            var commands = new List<IInputCommand>(count);
            for (int index = 0; index < count; index++)
            {
                ulong requestId = reader.ReadUInt64();
                string author = reader.ReadString(64);
                byte[] encoded = reader.ReadBytes(65536);
                IInputCommand command = GameCommandCodec.Decode(encoded, resolver);
                commands.Add(command);
                if (commandsSeen.Count >= 10000) throw new ProtocolException("Harness command observation limit reached.");
                commandsSeen.Add(new CommandObservation(sequence, requestId, author, command.GetType().Name));
                if (author == options.Name && requestId != 0) pending.Remove(requestId);
            }
            reader.EnsureEnd();

            simulation.SetSimPause(beforePaused);
            simulation.SetSimSpeed(1);
            foreach (IInputCommand command in commands) scheduler.ScheduleInputCmd(command);
            update.Invoke(runner, new object[] { 100.ToFix32() });
            if (simulation.CurrentStep.Value != afterStep)
                throw new ProtocolException($"Tick {sequence}: after-step mismatch, server {afterStep}, peer {simulation.CurrentStep.Value}.");
            foreach (IInputCommand command in commands)
                if (command.ResultSet && command.HasError)
                    throw new ProtocolException($"Tick {sequence}: replicated command failed: {command.ErrorMessage}");

            using (var acknowledgment = new PayloadWriter())
            {
                acknowledgment.WriteUInt64(sequence);
                acknowledgment.WriteInt32(simulation.CurrentStep.Value);
                Send(stream, acknowledgment.ToFrame(MessageKind.Ack), token);
            }
            lastSequence = sequence;
            sawTick = true;
            if (localTick % 25 == 0 || localTick == options.Ticks)
                Console.WriteLine($"[peer] Verified {localTick} ticks; sequence {sequence}; step {afterStep}; paused {simulation.IsSimPaused}");

            if (options.PauseProposals.TryGetValue(localTick, out bool paused))
            {
                Propose(new SetSimPauseStateCmd(paused), paused ? "pause" : "resume");
            }
            if (options.PlaceMachineAt == localTick)
            {
                CreateStaticEntityCmd placement = FindMachinePlacement(resolver);
                Propose(placement, $"machine {placement.ProtoId.Value} at {placement.Transform.Position}");
            }
            if (options.RejectedSpeedAt == localTick)
                Propose(new GameSpeedChangeCmd(2), "speed 2 (expected policy rejection)");
        }

        if (pending.Count != 0) throw new ProtocolException("Some proposed commands were not observed in authoritative ticks before the harness ended.");
        if (options.RejectedSpeedAt.HasValue && commandsRejected.Count != 1)
            throw new ProtocolException("The speed rejection fixture expected one rejected proposal.");
        // Raw serialization does not invoke BeforeSave, which populates save-only caches.
        // Match the host's normal save preparation before comparing diagnostic digests.
        resolver.Resolve<SaveManager>().SaveGame(Stream.Null, SaveCompressionType.Gzip, fromTest: true);
        var rawState = GameWorldStatus.WriteRawState(resolver, Path.Combine(options.DataDirectory, "peer-state.raw"));
        string report = JsonSerializer.Serialize(new
        {
            name = options.Name,
            snapshotStep,
            verifiedTicks = options.Ticks,
            lastSequence,
            proposalsSent = nextRequestId - 1,
            commandsSeen,
            commandsRejected,
            paused = simulation.IsSimPaused,
            beforeSavePrepared = true,
            rawState,
            world = GameWorldStatus.Read(resolver, includeTerrain: true)
        }, new JsonSerializerOptions { WriteIndented = true });
        string reportPath = Path.Combine(options.DataDirectory, "peer-report.json");
        File.WriteAllText(reportPath, report);
        Console.WriteLine($"[peer] Verified replay report: {reportPath}");

        void Propose(IInputCommand command, string description)
        {
            if (!GameCommandCodec.TryEncode(command, out byte[] encoded, out string reason))
                throw new InvalidOperationException(reason);
            ulong requestId = nextRequestId++;
            using var proposal = new PayloadWriter();
            proposal.WriteUInt64(requestId);
            proposal.WriteBytes(encoded, 65536);
            Send(stream, proposal.ToFrame(MessageKind.Command), token);
            pending.Add(requestId);
            Console.WriteLine($"[peer] Proposed {description}; request {requestId}");
        }
    }

    private static CreateStaticEntityCmd FindMachinePlacement(DependencyResolver resolver)
    {
        var database = resolver.Resolve<ProtosDb>();
        var entities = resolver.Resolve<EntitiesManager>();
        var terrain = resolver.Resolve<TerrainManager>();
        var factory = resolver.Resolve<LayoutEntityAddRequestFactory>();
        MachineProto machine = database.All<MachineProto>()
            .Where(proto => proto.GetType() == typeof(MachineProto) && proto.IsUnlockedAndAvailable
                && !proto.CannotBeBuiltByPlayer && !proto.IsAutoBuiltByParent)
            .OrderBy(proto => proto.Layout.TilesCount).ThenBy(proto => proto.Id.Value, StringComparer.Ordinal)
            .FirstOrDefault() ?? throw new InvalidOperationException("No ordinary unlocked machine prototype is available.");
        StaticEntity anchor = entities.GetAllEntitiesOfType<StaticEntity>().FirstOrDefault()
            ?? throw new InvalidOperationException("Placement fixture requires an island with a starting entity.");
        Tile2i center = anchor.CenterTile.Xy;
        // Read-only placement requests check possible locations without constructing entities or consuming IDs.
        for (int dx = -80; dx <= 80; dx += 4)
        for (int dy = -80; dy <= 80; dy += 4)
        {
            var xy = new Tile2i(center.X + dx, center.Y + dy);
            if (xy.X < terrain.MinOnLimits.X + 12 || xy.Y < terrain.MinOnLimits.Y + 12
                || xy.X >= terrain.MaxOnLimitsExcl.X - 12 || xy.Y >= terrain.MaxOnLimitsExcl.Y - 12) continue;
            var transform = new TileTransform(new Tile3i(xy, terrain.GetHeight(xy).TilesHeightRounded.Value));
            var request = factory.CreateRequestFor(machine, new EntityAddRequestData(transform));
            try
            {
                if (entities.CanAdd(request).IsError) continue;
                var command = new CreateStaticEntityCmd(machine.Id, transform);
                if (!GameCommandCodec.TryEncode(command, out byte[] encoded)) continue;
                _ = GameCommandCodec.Decode(encoded, resolver); // Validate the same network constraints.
                return command;
            }
            finally { request.ReturnToPool(); }
        }
        throw new InvalidOperationException("No valid ordinary-machine placement found within the bounded search area.");
    }

    private static PayloadReader Welcome(ProtocolFrame frame, byte stage)
    {
        Expect(frame, MessageKind.Welcome);
        if (frame.Payload.Length == 0 || frame.Payload.Span[0] != stage) throw new ProtocolException("Unexpected Welcome stage.");
        return new PayloadReader(frame.Payload.Slice(1));
    }

    private static ProtocolFrame ReadFrame(Stream stream, CancellationToken token)
    {
        ProtocolFrame frame = FrameCodec.ReadAsync(stream, token).GetAwaiter().GetResult()
            ?? throw new EndOfStreamException("Game server closed the connection.");
        if (frame.Kind == MessageKind.Error)
        {
            var error = new PayloadReader(frame.Payload);
            string message = error.ReadString(65536);
            error.EnsureEnd();
            throw new ProtocolException($"Server rejected peer operation: {message}");
        }
        return frame;
    }

    private static void Send(Stream stream, ProtocolFrame frame, CancellationToken token) => FrameCodec.WriteAsync(stream, frame, token).GetAwaiter().GetResult();
    private static void Expect(ProtocolFrame frame, MessageKind kind)
    {
        if (frame.Kind != kind) throw new ProtocolException($"Expected {kind}, received {frame.Kind}.");
    }

    private sealed record CommandObservation(ulong TickSequence, ulong RequestId, string Author, string CommandType);
    private sealed record CommandRejection(ulong RequestId, string Reason);
}
