using System.Diagnostics;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using CoiDedicated.GameAdapter;
using Mafi;
using Mafi.Core.Input;
using Mafi.Core.SaveGame;
using Mafi.Core.Simulation;

namespace CoiDedicated;

internal static class DedicatedService
{
    public static void Run(HostOptions options, DependencyResolver resolver, object runner, CancellationToken cancellationToken = default)
    {
        MethodInfo update = runner.GetType().GetMethod("Update")!;
        ISimLoopEvents simulation = resolver.Resolve<ISimLoopEvents>();
        string secretPath = options.SecretFile ?? Path.Combine(options.DataDirectory, "control.key");
        string secret = ReadSecret(secretPath);
        string joinSecretPath = options.JoinSecretFile ?? Path.Combine(options.DataDirectory, "join.key");
        string build = GameBuildIdentity.Compute(Path.Combine(options.GameDirectory, "Captain of Industry_Data", "Managed"));
        using var game = new GameSessionServer(options.BindAddress, options.GamePort, ReadSecret(joinSecretPath), build);
        using var server = new DedicatedControlServer(options.BindAddress, options.Port, secret, build);
        Console.WriteLine($"[server] Control key file: {secretPath}");
        Console.WriteLine($"[server] Player join key file: {joinSecretPath}");
        Console.WriteLine("[server] Simulation continues with no players. Joining briefly holds ticks while the snapshot loads. Ctrl+C or SIGTERM saves and stops.");
        resolver.Resolve<SimLoopEvents>().SetSimSpeed(1);
        runner.GetType().GetProperty("ExtraSimUpdateMs")!.SetValue(runner, Fix32.Zero);
        game.ScheduleSystem(new SetSimPauseStateCmd(false));
        var clock = Stopwatch.StartNew();
        long nextTick = 0, nextSave = options.AutosaveSeconds * 1000L;
        bool shutdownRequested = false;
        while (!cancellationToken.IsCancellationRequested && !shutdownRequested && (options.RunSeconds == 0 || clock.Elapsed.TotalSeconds < options.RunSeconds))
        {
            server.Pump(command =>
            {
                switch (command)
                {
                    case "pause": game.ScheduleSystem(new SetSimPauseStateCmd(true)); break;
                    case "resume": game.ScheduleSystem(new SetSimPauseStateCmd(false)); break;
                    case "save": Save(options, resolver); break;
                    case "snapshot":
                        string path = Save(options, resolver);
                        // Pin this exact save generation before the simulation
                        // can advance and another save replaces the path.
                        var snapshot = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete, 65536, FileOptions.Asynchronous);
                        return new ControlReply("Snapshot ready", snapshot, simulation.CurrentStep.Value);
                    case "shutdown": shutdownRequested = true; return new ControlReply("Saving and shutting down");
                }
                return new ControlReply(JsonSerializer.Serialize(new { command, step = simulation.CurrentStep.Value, paused = simulation.IsSimPaused, players = game.PlayerCount, joining = game.WaitingForJoin, uptimeSeconds = (int)clock.Elapsed.TotalSeconds, world = GameWorldStatus.Read(resolver) }));
            });
            game.Pump(() => Save(options, resolver), simulation);
            if (!game.WaitingForJoin && clock.ElapsedMilliseconds >= nextTick)
            {
                int beforeStep = simulation.CurrentStep.Value;
                bool beforePaused = simulation.IsSimPaused;
                var commands = game.Admit(resolver);
                update.Invoke(runner, new object[] { 100.ToFix32() });
                game.Publish(beforeStep, simulation.CurrentStep.Value, beforePaused, commands);
                // Do not accumulate an unbounded catch-up backlog on a slow island.
                nextTick = Math.Max(nextTick + 100, clock.ElapsedMilliseconds);
            }
            if (clock.ElapsedMilliseconds >= nextSave)
            {
                Save(options, resolver);
                nextSave = clock.ElapsedMilliseconds + options.AutosaveSeconds * 1000L;
            }
            Thread.Sleep(5);
        }
        Save(options, resolver);
        File.WriteAllText(Path.Combine(options.DataDirectory, "world-status.json"), JsonSerializer.Serialize(GameWorldStatus.Read(resolver, includeTerrain: true), new JsonSerializerOptions { WriteIndented = true }));
        if (options.Diagnostics)
            File.WriteAllText(Path.Combine(options.DataDirectory, "raw-state.json"), JsonSerializer.Serialize(GameWorldStatus.WriteRawState(resolver, Path.Combine(options.DataDirectory, "server-state.raw"))));
        if (shutdownRequested) Thread.Sleep(100); // Allow the queued shutdown acknowledgment to leave.
    }

    private static string ReadSecret(string path)
    {
        if (!File.Exists(path))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            using var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            using var writer = new StreamWriter(file);
            writer.Write(Convert.ToHexString(RandomNumberGenerator.GetBytes(32)));
        }
        string secret = File.ReadAllText(path).TrimEnd('\r', '\n');
        if (secret.Length < 16) throw new ArgumentException("Authentication secrets must contain at least 16 characters.");
        return secret;
    }

    internal static string Save(HostOptions options, DependencyResolver resolver)
    {
        string output = Path.Combine(options.DataDirectory, "island.save");
        string temporary = output + ".tmp";
        using (var stream = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            resolver.Resolve<SaveManager>().SaveGame(stream, SaveCompressionType.Gzip, fromTest: true);
            stream.Flush(flushToDisk: true);
        }
        if (File.Exists(output))
        {
            // Windows ReplaceFile cannot overwrite an open backup generation.
            // A share-delete snapshot keeps its bytes while its old backup name is retired.
            File.Delete(output + ".previous");
            File.Replace(temporary, output, output + ".previous", ignoreMetadataErrors: true);
        }
        else File.Move(temporary, output);
        Console.WriteLine($"[server] Saved step {resolver.Resolve<ISimLoopEvents>().CurrentStep.Value}: {new FileInfo(output).Length:N0} bytes");
        return output;
    }
}
