using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using System.Text.Json;
using CoiDedicated.GameAdapter;
using CoiDedicated.Protocol;
using Mafi;
using Mafi.Collections.ImmutableCollections;
using Mafi.Core;
using Mafi.Core.Entities;
using Mafi.Core.Entities.Static;
using Mafi.Core.Entities.Static.Commands;
using Mafi.Core.Entities.Static.Layout;
using Mafi.Core.Input;
using Mafi.Core.Prototypes;
using Mafi.Core.Terrain;

internal static class Program
{
    private static int Main(string[] args)
    {
        if (args.Length < 2) throw new ArgumentException("Usage: BuildingPlacement.Tests SOURCE_SAVE ISOLATED_OUTPUT_DIR [GAME_DIR]");
        string source = Path.GetFullPath(args[0]);
        string output = Path.GetFullPath(args[1]);
        string game = Path.GetFullPath(args.Length > 2 ? args[2] : Environment.GetEnvironmentVariable("COI_GAME_DIR")
            ?? @"F:\SteamLibrary\steamapps\common\Captain of Industry");
        if (!File.Exists(source)) throw new FileNotFoundException("An existing CoI 0.8.7d fixture save is required.", source);
        if (Directory.Exists(output)) throw new ArgumentException("Use a new isolated output directory.");
        Directory.CreateDirectory(output);
        AssemblyLoadContext.Default.Resolving += (_, name) => name.Name is "Mafi" or "Mafi.Core" or "Mafi.Base"
            ? AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.Combine(game, "Captain of Industry_Data", "Managed", name.Name + ".dll")) : null;
        Environment.CurrentDirectory = game;
        return Run(source, output, game);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static int Run(string source, string output, string game)
    {
        // Use the production headless loader while confining all writes to a fresh
        // directory. The fixture save is opened read-only by GameHost.
        Assembly host = Assembly.Load("CoiDedicated");
        object options = host.GetType("CoiDedicated.HostOptions", true)!.GetMethod("Parse")!.Invoke(null,
            new object[] { new[] { "--game-dir", game, "--load", source, "--data-dir", output, "--steps", "0" } })!;
        Action<DependencyResolver, object> callback = (resolver, runner) => Test(resolver, runner, output);
        return (int)host.GetType("CoiDedicated.GameHost", true)!.GetMethod("Run")!.Invoke(null,
            new object[] { options, callback, CancellationToken.None })!;
    }

    private static void Test(DependencyResolver resolver, object runner, string output)
    {
        var db = resolver.Resolve<ProtosDb>();
        var manager = resolver.Resolve<EntitiesManager>();
        var terrain = resolver.Resolve<TerrainManager>();
        var factory = resolver.Resolve<LayoutEntityAddRequestFactory>();
        var context = resolver.Resolve<ConfigSerializationContext>();
        var results = new List<object>();
        runner.GetType().GetProperty("ExtraSimUpdateMs")!.SetValue(runner, Fix32.Zero);
        resolver.Resolve<Mafi.Core.Simulation.SimLoopEvents>().SetSimSpeed(1);
        Tile2i center = manager.GetAllEntitiesOfType<StaticEntity>().First().CenterTile.Xy;
        void Update() => runner.GetType().GetMethod("Update")!.Invoke(runner, new object[] { 100.ToFix32() });
        BatchCreateStaticEntitiesCmd Batch(LayoutEntityProto prototype, params TileTransform[] transforms) => new(
            new ImmutableArray<EntityConfigData>(transforms.Select(transform => new EntityConfigData(prototype, context) { Transform = transform }).ToArray()));
        BatchCreateStaticEntitiesCmd RoundTrip(BatchCreateStaticEntitiesCmd command)
        {
            Check(GameCommandCodec.TryEncode(command, out byte[] wire, out string reason), reason);
            var decoded = (BatchCreateStaticEntitiesCmd)GameCommandCodec.Decode(wire, resolver);
            Check(!decoded.IsFree && !decoded.AllowValidationSuppression && !decoded.ApplyConfiguration, "Privileged or copied-config flags changed.");
            Check(decoded.ConfigData.Length == command.ConfigData.Length && decoded.BuildMiniZippers == command.BuildMiniZippers, "Batch shape changed.");
            Check(!decoded.IsProcessed && !decoded.ResultSet, "Decoded command already has a result.");
            Check(GameCommandCodec.TryEncode(decoded, out byte[] rebuilt) && rebuilt.SequenceEqual(wire), "Batch wire is not canonical.");
            return decoded;
        }
        TileTransform FindPosition(LayoutEntityProto prototype)
        {
            for (int dx = -128; dx <= 128; dx += 4)
            for (int dy = -128; dy <= 128; dy += 4)
            {
                var xy = new Tile2i(center.X + dx, center.Y + dy);
                if (xy.X < terrain.MinOnLimits.X + 24 || xy.Y < terrain.MinOnLimits.Y + 24
                    || xy.X >= terrain.MaxOnLimitsExcl.X - 24 || xy.Y >= terrain.MaxOnLimitsExcl.Y - 24) continue;
                var transform = new TileTransform(new Tile3i(xy, terrain.GetHeight(xy).TilesHeightRounded.Value));
                var request = factory.CreateRequestFor(prototype, new EntityAddRequestData(transform));
                try { if (!manager.CanAdd(request).IsError) return transform; }
                finally { request.ReturnToPool(); }
            }
            throw new Exception("No valid placement found for " + prototype.Id.Value);
        }
        string[] families = { "MachineProto", "UnitStorageProto", "LooseStorageProto", "FluidStorageProto", "ResearchLabProto", "VehicleDepotProto", "FarmProto", "MineTowerProto", "ForestryTowerProto" };
        foreach (string family in families)
        {
            LayoutEntityProto prototype = db.All<LayoutEntityProto>().Where(x => x.GetType().Name == family && x.IsAvailable
                    && !x.CannotBeBuiltByPlayer && !x.IsAutoBuiltByParent)
                .OrderBy(x => x.Layout.TilesCount).ThenBy(x => x.Id.Value, StringComparer.Ordinal).First();
            bool initiallyLocked = !prototype.IsUnlockedAndAvailable;
            if (initiallyLocked)
            {
                Check(GameCommandCodec.TryEncode(Batch(prototype, new TileTransform(new Tile3i(center, 3))), out byte[] locked), "Locked prototype must reach world validation.");
                Reject(() => GameCommandCodec.Decode(locked, resolver));
                // Fixture-only setup after proving the normal network path rejects locked prototypes.
                resolver.Resolve<UnlockedProtosDb>().Unlock(prototype);
            }
            TileTransform position = FindPosition(prototype);
            var command = RoundTrip(Batch(prototype, position));
            resolver.Resolve<IInputScheduler>().ScheduleInputCmd(command);
            Update();
            // GameRunner synchronizes input results at the start of the following update.
            Update();
            Check(command.ResultSet && command.Result && command.IsProcessed && command.IsProcessedAndSynced && !command.HasError,
                $"Native batch failed for {prototype.Id.Value}: resultSet={command.ResultSet}, processed={command.IsProcessed}, synced={command.IsProcessedAndSynced}, error={command.ErrorMessage}");
            StaticEntity created = manager.GetAllEntitiesOfType<StaticEntity>().Single(entity => entity.Prototype == prototype && entity is ILayoutEntity layout && layout.Transform == position);
            Check(!created.IsConstructed, "Batch bypassed construction costs.");
            results.Add(new { Family = family, Prototype = prototype.Id.Value, created.Id.Value, Transform = position.ToString(), InitiallyLocked = initiallyLocked, command.Result, command.IsProcessedAndSynced });
            Console.WriteLine("PASS native batch placement: " + family + " / " + prototype.Id.Value);
        }

        LayoutEntityProto unitStorage = db.All<LayoutEntityProto>().First(p => p.GetType().Name == "UnitStorageProto" && p.IsUnlockedAndAvailable && !p.CannotBeBuiltByPlayer);
        TileTransform free = FindPosition(unitStorage);
        // Same position twice deliberately exercises native identity handling. Both
        // items succeed, creating only one entity, and the batch has one bool result.
        var duplicate = RoundTrip(Batch(unitStorage, free, free));
        resolver.Resolve<IInputScheduler>().ScheduleInputCmd(duplicate);
        Update();
        Update();
        Check(duplicate.ResultSet && duplicate.Result && duplicate.IsProcessedAndSynced, "Native duplicate batch identity handling was lost.");
        Check(manager.GetAllEntitiesOfType<StaticEntity>().Count(e => e.Prototype == unitStorage && e is ILayoutEntity layout && layout.Transform == free) == 1, "Batch created duplicate buildings.");
        Console.WriteLine("PASS native batch duplicate identity and single completion result");

        var max = Batch(unitStorage, Enumerable.Repeat(free, GameCommandCodec.MaxBuildingsPerBatch).ToArray());
        RoundTrip(max);
        Check(!GameCommandCodec.TryEncode(Batch(unitStorage, Enumerable.Repeat(free, GameCommandCodec.MaxBuildingsPerBatch + 1).ToArray()), out _), "Oversize batch accepted.");
        foreach (BuildMiniZippersMode mode in Enum.GetValues<BuildMiniZippersMode>())
        {
            var withMode = new BatchCreateStaticEntitiesCmd(max.ConfigData, mode);
            Check(RoundTrip(withMode).BuildMiniZippers == mode, "Mini-zipper setting was not preserved.");
        }
        // Missing transforms and non-default ignored clone settings are deliberately
        // different: the first is malformed; the latter is safe when ApplyConfiguration=false.
        var missing = new BatchCreateStaticEntitiesCmd(new ImmutableArray<EntityConfigData>(new[] { new EntityConfigData(unitStorage, context) }));
        Check(!GameCommandCodec.TryEncode(missing, out _), "Missing placement transform accepted.");
        var copied = new BatchCreateStaticEntitiesCmd(max.ConfigData, applyConfiguration: true);
        Check(GameCommandCodec.TryEncode(copied, out byte[] copiedWire), "Supported storage blueprint was rejected.");
        Check(((BatchCreateStaticEntitiesCmd)GameCommandCodec.Decode(copiedWire, resolver)).ApplyConfiguration,
            "Copied configuration flag was silently discarded.");
        var railConfig = new EntityConfigData(unitStorage, context) { Transform = free, TrackPolesBitmap = 1 };
        Check(!GameCommandCodec.TryEncode(new BatchCreateStaticEntitiesCmd(new ImmutableArray<EntityConfigData>(new[] { railConfig })), out _), "Specialized pole data was discarded.");
        Check(GameCommandCodec.TryEncode(Batch(unitStorage, new TileTransform(new Tile3i(0, 0, 3))), out byte[] outside), "Expected scalar-valid out-of-map payload.");
        Reject(() => GameCommandCodec.Decode(outside, resolver));
        Console.WriteLine("PASS batch bounds, mini-zipper modes, map validation, and unsupported-config rejection");
        File.WriteAllText(Path.Combine(output, "building-results.json"), JsonSerializer.Serialize(results, new JsonSerializerOptions { WriteIndented = true }));
        GameplayCommandTests.Run(resolver, runner, output);
    }

    private static void Check(bool value, string message) { if (!value) throw new Exception(message); }
    private static void Reject(Action action)
    {
        try { action(); } catch (ProtocolException) { return; }
        throw new Exception("Expected rejected placement.");
    }
}
