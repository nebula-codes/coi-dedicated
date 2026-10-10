using System.Collections;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using System.Security.Cryptography;
using System.Text.Json;
using Mafi;
using Mafi.Collections.ImmutableCollections;
using Mafi.Core;
using Mafi.Core.Buildings.Settlements;
using Mafi.Core.Entities;
using Mafi.Core.Entities.Dynamic;
using Mafi.Core.Entities.Static;
using Mafi.Core.Entities.Static.Commands;
using Mafi.Core.Entities.Static.Layout;
using Mafi.Core.Input;
using Mafi.Core.Prototypes;
using Mafi.Core.Simulation;
using Mafi.Core.Terrain;

internal static class Program
{
    private const int BuildingCount = 120;
    private const int SoakSteps = 3100;
    private const BindingFlags InstanceFields = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    private static int Main(string[] args)
    {
        var options = Arguments.Parse(args);
        if (Directory.Exists(options.Output)) throw new ArgumentException("Use a fresh isolated output directory.");
        Directory.CreateDirectory(options.Output);
        string fixture = Path.Combine(options.Output, "fixture.save");
        File.Copy(options.Source, fixture);
        AssemblyLoadContext.Default.Resolving += (_, name) =>
        {
            string? path = name.Name?.StartsWith("CoiDedicated", StringComparison.Ordinal) == true
                ? Path.Combine(options.HostDirectory, name.Name + ".dll")
                : name.Name?.StartsWith("Mafi", StringComparison.Ordinal) == true
                    ? Path.Combine(options.GameDirectory, "Captain of Industry_Data", "Managed", name.Name + ".dll") : null;
            return path is not null && File.Exists(path) ? AssemblyLoadContext.Default.LoadFromAssemblyPath(path) : null;
        };
        Environment.CurrentDirectory = options.GameDirectory;
        try { return Run(options, fixture); }
        catch (Exception error)
        {
            while (error is TargetInvocationException { InnerException: not null }) error = error.InnerException;
            File.WriteAllText(Path.Combine(options.Output, "failure.txt"), error.ToString());
            Console.Error.WriteLine(error);
            return 1;
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static int Run(Arguments options, string fixture)
    {
        string hostPath = Path.Combine(options.HostDirectory, "CoiDedicated.dll");
        Assembly host = AssemblyLoadContext.Default.LoadFromAssemblyPath(hostPath);
        Console.WriteLine($"[test] Host={hostPath} SHA256={Hash(hostPath)} fixtureSHA256={Hash(fixture)}");
        object hostOptions = host.GetType("CoiDedicated.HostOptions", true)!.GetMethod("Parse")!.Invoke(null,
            new object[] { new[] { "--game-dir", options.GameDirectory, "--load", fixture, "--data-dir", options.Output, "--steps", "0" } })!;
        Action<DependencyResolver, object> session = (resolver, runner) => Test(resolver, runner, options, hostPath, fixture);
        return (int)host.GetType("CoiDedicated.GameHost", true)!.GetMethod("Run")!.Invoke(null,
            new object[] { hostOptions, session, CancellationToken.None })!;
    }

    private static void Test(DependencyResolver resolver, object runner, Arguments options, string hostPath, string fixture)
    {
        var entities = resolver.Resolve<EntitiesManager>();
        var terrain = resolver.Resolve<TerrainManager>();
        var protos = resolver.Resolve<ProtosDb>();
        var factory = resolver.Resolve<LayoutEntityAddRequestFactory>();
        var context = resolver.Resolve<ConfigSerializationContext>();
        var scheduler = resolver.Resolve<IInputScheduler>();
        var sim = resolver.Resolve<SimLoopEvents>();
        var calendar = resolver.Resolve<ICalendar>();
        var settlements = resolver.Resolve<SettlementsManager>();
        var gameOver = resolver.Resolve<GameOverManager>();
        var update = runner.GetType().GetMethod("Update")!;
        var messages = new List<string>();
        Log.LogReceived += entry => { lock (messages) messages.Add(entry.ToString()); };
        runner.GetType().GetProperty("ExtraSimUpdateMs")!.SetValue(runner, Fix32.Zero);
        sim.SetSimSpeed(1);
        scheduler.ScheduleInputCmd(new SetSimPauseStateCmd(false));
        void Tick() => update.Invoke(runner, new object[] { 100.ToFix32() });
        Tick(); Tick();

        int start = sim.CurrentStep.Value, year = calendar.CurrentDate.Year;
        int originalPopulation = settlements.GetTotalPopulation();
        Vehicle[] originalVehicles = entities.GetAllEntitiesOfType<Vehicle>().ToArray();
        Check(year >= 10, "Fixture must already be in year 10 or later.");
        Check(originalPopulation > 0 && !gameOver.IsGameOver, "Fixture must be alive before testing.");
        Check(originalVehicles.Length > 0 && !originalVehicles.Any(v => v.IsOnWayToDepotForScrap), "Fixture needs vehicles with no pending scrap.");
        string[] startupCallbacks = PirateCallbacks(entities.StaticEntityAdded);
        bool connectedDiagnostic = Log.DebugInfos.Contains("Steam client connected");
        Console.WriteLine($"[test] step={start} year={year} population={originalPopulation} vehicles={originalVehicles.Length} pirateCallbacks={startupCallbacks.Length} steamConnectedDiagnostic={connectedDiagnostic}");

        var storage = protos.All<LayoutEntityProto>().Where(p => p.GetType().Name == "UnitStorageProto"
                && p.IsUnlockedAndAvailable && !p.CannotBeBuiltByPlayer && !p.IsAutoBuiltByParent)
            .OrderBy(p => p.Layout.TilesCount).ThenBy(p => p.Id.Value, StringComparer.Ordinal).First();
        Tile2i center = entities.GetAllEntitiesOfType<StaticEntity>().First().CenterTile.Xy;
        var placements = new List<object>();
        var ids = new List<int>();
        int examinedPositions = 0;
        IEnumerable<TileTransform> Candidates()
        {
            // Native CanAdd performs terrain, collision and ordinary placement validation.
            // No terrain, research, resources, or preview state are changed to make room.
            for (int dx = -320; dx <= 320; dx += 4)
            for (int dy = -320; dy <= 320; dy += 4)
            {
                var xy = new Tile2i(center.X + dx, center.Y + dy);
                if (xy.X < terrain.MinOnLimits.X + 32 || xy.Y < terrain.MinOnLimits.Y + 32
                    || xy.X >= terrain.MaxOnLimitsExcl.X - 32 || xy.Y >= terrain.MaxOnLimitsExcl.Y - 32) continue;
                yield return new TileTransform(new Tile3i(xy, terrain.GetHeight(xy).TilesHeightRounded.Value));
            }
        }
        foreach (TileTransform position in Candidates())
        {
            examinedPositions++;
            var request = factory.CreateRequestFor(storage, new EntityAddRequestData(position));
            bool valid;
            try { valid = !entities.CanAdd(request).IsError; }
            finally { request.ReturnToPool(); }
            if (!valid) continue;
            var command = new BatchCreateStaticEntitiesCmd(new ImmutableArray<EntityConfigData>(new[]
                { new EntityConfigData(storage, context) { Transform = position } }));
            Check(!command.IsFree && !command.AllowValidationSuppression && !command.ApplyConfiguration, "Placement acquired privileged flags.");
            scheduler.ScheduleInputCmd(command); Tick(); Tick();
            Check(command.ResultSet && command.Result && command.IsProcessedAndSynced && !command.HasError,
                "Native storage command failed: " + command.ErrorMessage);
            StaticEntity placed = entities.GetAllEntitiesOfType<StaticEntity>().Single(e => e.Prototype == storage
                && e is ILayoutEntity layout && layout.Transform == position);
            Check(!placed.IsConstructed, "Placement skipped construction.");
            ids.Add(placed.Id.Value);
            placements.Add(new { Id = placed.Id.Value, Position = position.ToString(), Step = sim.CurrentStep.Value });
            if (ids.Count >= BuildingCount && ids.Any(id => id % 100 == 0)) break;
        }
        Check(ids.Count >= BuildingCount && ids.Distinct().Count() == ids.Count, "Could not place enough distinct ordinary storage plans.");
        Check(ids.Any(id => id % 100 == 0), "No placed entity ID reached the native delayed-event trigger boundary.");
        int afterBuild = sim.CurrentStep.Value;
        Console.WriteLine($"[test] Placed {ids.Count} {storage.Id.Value} plans; IDs={ids.Min()}..{ids.Max()}, boundaryIDs={string.Join(',', ids.Where(id => id % 100 == 0))}; soak starts {afterBuild}");

        int maximumScrapping = 0;
        int minimumPopulation = originalPopulation;
        int firstGameOver = -1;
        int updates = 0;
        while (sim.CurrentStep.Value - afterBuild < SoakSteps && updates < SoakSteps * 3)
        {
            Tick(); updates++;
            minimumPopulation = Math.Min(minimumPopulation, settlements.GetTotalPopulation());
            maximumScrapping = Math.Max(maximumScrapping, originalVehicles.Count(v => v.IsOnWayToDepotForScrap));
            if (gameOver.IsGameOver && firstGameOver < 0) firstGameOver = sim.CurrentStep.Value;
            if (options.ExpectTrigger && firstGameOver >= 0 && PirateLogs().Length > 0) break;
            if (updates % 500 == 0) Console.WriteLine($"[test] soak={sim.CurrentStep.Value - afterBuild}/{SoakSteps} population={settlements.GetTotalPopulation()} gameOver={gameOver.IsGameOver}");
        }
        string[] PirateLogs() { lock (messages) return messages.Where(m => m.Contains("Pirates raided our island", StringComparison.OrdinalIgnoreCase)).ToArray(); }
        string[] pirateLogs = PirateLogs();
        string[] pendingCallbacks = PirateCallbacks(sim.Update);
        int finalPopulation = settlements.GetTotalPopulation();
        int finalStep = sim.CurrentStep.Value;
        var report = new
        {
            ExpectTrigger = options.ExpectTrigger, HostPath = hostPath, HostSha256 = Hash(hostPath), FixtureSha256 = Hash(fixture),
            StartStep = start, Year = year, StartPopulation = originalPopulation, EndPopulation = finalPopulation, MinimumPopulation = minimumPopulation,
            OriginalVehicleCount = originalVehicles.Length, MaximumVehiclesPendingScrap = maximumScrapping, FirstGameOverStep = firstGameOver,
            GameOver = gameOver.IsGameOver, SteamConnectedDiagnostic = connectedDiagnostic, StartupCallbacks = startupCallbacks,
            PendingCallbacks = pendingCallbacks, PirateLogs = pirateLogs, BuildingPrototype = storage.Id.Value,
            BuildingsCreated = ids.Count, ExaminedPositions = examinedPositions, BoundaryIds = ids.Where(id => id % 100 == 0).ToArray(),
            BuildEndStep = afterBuild, EndStep = finalStep, SoakSteps = finalStep - afterBuild, UpdateCalls = updates, Placements = placements
        };
        File.WriteAllText(Path.Combine(options.Output, "results.json"), JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
        lock (messages) File.WriteAllLines(Path.Combine(options.Output, "native-game.log"), messages);
        if (options.ExpectTrigger)
        {
            Check(!connectedDiagnostic && startupCallbacks.Length > 0, "Baseline did not reproduce unvalidated startup registration.");
            Check(pirateLogs.Length > 0 && firstGameOver >= 0 && gameOver.IsGameOver && finalPopulation == 0,
                "Expected baseline pirate event and population removal were not reproduced.");
            Console.WriteLine("PASS expected old-host failure reproduced: real buildings triggered native pirate game-over and removed population.");
        }
        else
        {
            Check(connectedDiagnostic && startupCallbacks.Length == 0, "Native successful startup validation was absent or pirate callback was registered.");
            Check(finalStep - afterBuild >= SoakSteps, "Simulation did not advance beyond the full native delayed-trigger interval.");
            Check(pirateLogs.Length == 0 && pendingCallbacks.Length == 0 && !gameOver.IsGameOver && firstGameOver < 0, "Native pirate game-over activated.");
            Check(minimumPopulation > 0 && finalPopulation > 0, "Population was wiped out.");
            Check(maximumScrapping == 0, "The fixture's vehicles were unexpectedly sent for scrap.");
            Console.WriteLine("PASS validated startup: ordinary building commands and full delayed-trigger soak preserved the island.");
        }
    }

    // Read-only diagnostics: never invoke, remove or replace native callbacks.
    private static string[] PirateCallbacks(object eventObject)
    {
        FieldInfo? nonSaveable = FindField(eventObject.GetType(), "m_nonSaveable");
        if (nonSaveable is not null)
        {
            object option = nonSaveable.GetValue(eventObject)!;
            object? inner = option.GetType().GetProperty("ValueOrNull")!.GetValue(option);
            if (inner is null) return Array.Empty<string>();
            eventObject = inner;
        }
        var actions = (IEnumerable)(FindField(eventObject.GetType(), "Actions")
            ?? throw new InvalidOperationException("Native event diagnostic layout changed.")).GetValue(eventObject)!;
        var result = new List<string>();
        foreach (object item in actions)
        {
            var action = (Delegate)(FindField(item.GetType(), "Action")
                ?? throw new InvalidOperationException("Native callback diagnostic layout changed.")).GetValue(item)!;
            if (action.Method.DeclaringType?.FullName?.Contains("TransportsManager", StringComparison.Ordinal) == true
                && action.Method.Name.Contains("initialize", StringComparison.Ordinal))
                result.Add(action.Method.DeclaringType.FullName + "." + action.Method.Name);
        }
        return result.ToArray();
    }
    private static FieldInfo? FindField(Type type, string name)
    { for (Type? current = type; current is not null; current = current.BaseType) if (current.GetField(name, InstanceFields) is { } field) return field; return null; }
    private static void Check(bool value, string reason) { if (!value) throw new InvalidOperationException(reason); }
    private static string Hash(string path) => Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path)));

    private sealed record Arguments(string Source, string Output, string HostDirectory, string GameDirectory, bool ExpectTrigger)
    {
        public static Arguments Parse(string[] args)
        {
            string? source = null, output = null, host = null;
            string game = Environment.GetEnvironmentVariable("COI_GAME_DIR") ?? @"F:\SteamLibrary\steamapps\common\Captain of Industry";
            bool expected = false;
            for (int i = 0; i < args.Length; i++)
            {
                if (args[i] == "--expect-trigger") { expected = true; continue; }
                string value = i + 1 < args.Length ? args[++i] : throw new ArgumentException("Missing option value.");
                switch (args[i - 1])
                {
                    case "--source-save": source = value; break;
                    case "--output": output = value; break;
                    case "--host-dir": host = value; break;
                    case "--game-dir": game = value; break;
                    default: throw new ArgumentException("Unknown option: " + args[i - 1]);
                }
            }
            if (source is null || output is null || host is null)
                throw new ArgumentException("Required: --source-save SAVE --output FRESH_DIR --host-dir HOST_DIR [--game-dir GAME_DIR] [--expect-trigger]");
            return new(Path.GetFullPath(source), Path.GetFullPath(output), Path.GetFullPath(host), Path.GetFullPath(game), expected);
        }
    }
}
