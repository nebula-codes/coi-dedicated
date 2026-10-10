using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using CoiDedicated.GameAdapter;
using CoiDedicated.Protocol;
using Mafi;
using Mafi.Collections.ImmutableCollections;
using Mafi.Core;
using Mafi.Core.Buildings.Storages;
using Mafi.Core.Entities;
using Mafi.Core.Entities.Static;
using Mafi.Core.Entities.Static.Commands;
using Mafi.Core.Entities.Static.Layout;
using Mafi.Core.Factory.Machines;
using Mafi.Core.Factory.Recipes;
using Mafi.Core.Input;
using Mafi.Core.Products;
using Mafi.Core.Prototypes;
using Mafi.Core.Terrain;

internal static class Program
{
    private static int Main(string[] args)
    {
        if (args.Length < 2) throw new ArgumentException("Usage: Blueprint.Tests SOURCE_SAVE NEW_ISOLATED_OUTPUT_DIR [GAME_DIR]");
        string source = Path.GetFullPath(args[0]), output = Path.GetFullPath(args[1]);
        string game = Path.GetFullPath(args.Length > 2 ? args[2] : @"F:\SteamLibrary\steamapps\common\Captain of Industry");
        if (Directory.Exists(output)) throw new ArgumentException("Use a new isolated output directory.");
        Directory.CreateDirectory(output);
        string fixture = Path.Combine(output, "fixture.save"); File.Copy(source, fixture);
        AssemblyLoadContext.Default.Resolving += (_, name) => name.Name is "Mafi" or "Mafi.Core" or "Mafi.Base"
            ? AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.Combine(game, "Captain of Industry_Data", "Managed", name.Name + ".dll")) : null;
        Environment.CurrentDirectory = game;
        return Run(fixture, output, game);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static int Run(string fixture, string output, string game)
    {
        Assembly host = Assembly.Load("CoiDedicated");
        object options = host.GetType("CoiDedicated.HostOptions", true)!.GetMethod("Parse")!.Invoke(null,
            new object[] { new[] { "--game-dir", game, "--load", fixture, "--data-dir", output, "--steps", "0" } })!;
        Action<DependencyResolver, object> callback = (resolver, runner) => Test(resolver, runner);
        return (int)host.GetType("CoiDedicated.GameHost", true)!.GetMethod("Run")!.Invoke(null,
            new object[] { options, callback, CancellationToken.None })!;
    }

    private static void Test(DependencyResolver resolver, object runner)
    {
        var db = resolver.Resolve<ProtosDb>(); var entities = resolver.Resolve<EntitiesManager>();
        var clone = resolver.Resolve<EntitiesCloneConfigHelper>(); var terrain = resolver.Resolve<TerrainManager>();
        var factory = resolver.Resolve<LayoutEntityAddRequestFactory>();
        runner.GetType().GetProperty("ExtraSimUpdateMs")!.SetValue(runner, Fix32.Zero);
        resolver.Resolve<Mafi.Core.Simulation.SimLoopEvents>().SetSimSpeed(1);
        BatchCreateStaticEntitiesCmd Batch(params EntityConfigData[] configs) => new(new ImmutableArray<EntityConfigData>(configs), applyConfiguration: true);
        void Reject(EntityConfigData data, string reason)
        { Check(!GameCommandCodec.TryEncode(Batch(data), out _, out string error), reason); Check(error.Length > 0, "Missing rejection explanation"); }
        void RejectWire(byte[] bytes)
        { try { GameCommandCodec.Decode(bytes, resolver); } catch (ProtocolException) { return; } throw new Exception("Malformed blueprint wire accepted"); }
        byte[] Encode(EntityConfigData data)
        { Check(GameCommandCodec.TryEncode(Batch(data), out byte[] wire, out string reason), reason); return wire; }
        BatchCreateStaticEntitiesCmd RoundTrip(EntityConfigData data)
        {
            byte[] wire = Encode(data); var decoded = (BatchCreateStaticEntitiesCmd)GameCommandCodec.Decode(wire, resolver);
            Check(decoded.ApplyConfiguration && !decoded.IsFree && !decoded.AllowValidationSuppression, "Blueprint flags changed");
            Check(GameCommandCodec.TryEncode(decoded, out byte[] again) && wire.SequenceEqual(again), "Blueprint wire not canonical");
            return decoded;
        }
        TileTransform FindPosition(LayoutEntityProto proto, Tile2i center)
        {
            for (int dx = -160; dx <= 160; dx += 4) for (int dy = -160; dy <= 160; dy += 4)
            {
                var xy = new Tile2i(center.X + dx, center.Y + dy);
                if (xy.X < terrain.MinOnLimits.X + 32 || xy.Y < terrain.MinOnLimits.Y + 32 || xy.X >= terrain.MaxOnLimitsExcl.X - 32 || xy.Y >= terrain.MaxOnLimitsExcl.Y - 32) continue;
                var transform = new TileTransform(new Tile3i(xy, terrain.GetHeight(xy).TilesHeightRounded.Value));
                var request = factory.CreateRequestFor(proto, new EntityAddRequestData(transform));
                try { if (!entities.CanAdd(request).IsError) return transform; } finally { request.ReturnToPool(); }
            }
            throw new Exception("No valid fixture building placement for " + proto.Id);
        }
        StaticEntity Execute(EntityConfigData data)
        {
            var command = RoundTrip(data); resolver.Resolve<IInputScheduler>().ScheduleInputCmd(command);
            for (int i = 0; i < 2; i++) runner.GetType().GetMethod("Update")!.Invoke(runner, new object[] { 100.ToFix32() });
            Check(command.ResultSet && command.Result && command.IsProcessedAndSynced && !command.HasError, "Native blueprint failed: " + command.ErrorMessage);
            return entities.GetAllEntitiesOfType<StaticEntity>().Single(x => x.Prototype == data.Prototype.Value && x is ILayoutEntity layout && layout.Transform == data.Transform!.Value);
        }
        EntityConfigData Supported(IEntity entity)
        {
            var data = clone.CreateConfigFrom(entity);
            Check(GameCommandCodec.TryEncode(Batch(data), out _, out string reason), "Native copied config rejected for " + entity.Prototype.Id + ": " + reason);
            return data;
        }

        var storage = entities.GetAllEntitiesOfType<Storage>().First(x => x.Prototype.GetType().Name == "UnitStorageProto"
            && GameCommandCodec.TryEncode(Batch(clone.CreateConfigFrom(x)), out _));
        var storageData = Supported(storage);
        storageData.Transform = FindPosition((LayoutEntityProto)storage.Prototype, storage.CenterTile.Xy);
        var product = db.All<ProductProto>().First(p => p.IsAvailable && storage.Prototype.IsProductSupported(p));
        storageData.SetStorageStoredProduct(product);
        storageData.SetStorageImportUntilPercent(Percent.Zero); storageData.SetStorageExportFromPercent(Percent.FromRaw(90000));
        storageData.SetStorageTransportFromPercent(Percent.FromRaw(20000)); storageData.SetStorageTransportUntilPercent(Percent.FromRaw(80000));
        storageData.SetAlertWhenAboveEnabled(true); storageData.SetAlertWhenAbove(Percent.FromRaw(85000));
        storageData.CustomTitle = "Copied storage settings";
        var createdStorage = (Storage)Execute(storageData);
        Check(!createdStorage.IsConstructed && createdStorage.StoredProduct.Value == product, "Blueprint skipped construction or lost product");
        Check(createdStorage.ImportUntilPercent.RawValue == 0 && createdStorage.ExportFromPercent.RawValue == 90000
            && createdStorage.TransportFromPercent.RawValue == 20000 && createdStorage.TransportUntilPercent.RawValue == 80000
            && createdStorage.AlertWhenAboveEnabled && createdStorage.AlertWhenAbove.RawValue == 85000,
            $"Storage settings differ: import={createdStorage.ImportUntilPercent.RawValue}, export={createdStorage.ExportFromPercent.RawValue}, from={createdStorage.TransportFromPercent.RawValue}, until={createdStorage.TransportUntilPercent.RawValue}, alert={createdStorage.AlertWhenAboveEnabled}/{createdStorage.AlertWhenAbove.RawValue}");
        Console.WriteLine("PASS native copied storage placement, product, four sliders and alert settings");

        var machine = entities.GetAllEntitiesOfType<Machine>().First(x => x.RecipesAssigned.IsNotEmpty()
            && GameCommandCodec.TryEncode(Batch(clone.CreateConfigFrom(x)), out _));
        var machineData = Supported(machine);
        machineData.Transform = FindPosition((LayoutEntityProto)machine.Prototype, machine.CenterTile.Xy);
        machineData.IsPaused = true; machineData.GeneralPriority = 4;
        var createdMachine = (Machine)Execute(machineData);
        Check(!createdMachine.IsConstructed && createdMachine.IsPaused && createdMachine.GeneralPriority == 4, "Machine common settings lost");
        Check(createdMachine.RecipesAssigned.Select(x => x.Id.Value).SequenceEqual(machineData.Recipes!.Value.Select(x => x.Id.Value)), "Machine recipe selection lost");
        Console.WriteLine("PASS native copied machine placement, recipes, pause and priority");

        // Applying another copy at the same location retains native identity handling and
        // changes settings without creating a second building or bypassing construction.
        storageData.SetStorageImportUntilPercent(Percent.FromRaw(30000)); storageData.SetStorageExportFromPercent(Percent.Hundred); Execute(storageData);
        Check(createdStorage.ImportUntilPercent.RawValue == 30000, "Native identity paste did not change existing settings");
        Console.WriteLine("PASS native existing-building settings application preserves identity");

        byte[] valid = Encode(storageData);
        for (int length = 0; length < valid.Length; length++) RejectWire(valid.Take(length).ToArray());
        RejectWire(valid.Concat(new byte[] { 0 }).ToArray());
        var unknown = storageData.CreateCopy(); unknown.SetInt("UnsupportedModConfig", 1); Reject(unknown, "Unknown config field accepted");
        var external = storageData.CreateCopy(); external.AssignedInputs = new ImmutableArray<EntityId>(new[] { machine.Id }); Reject(external, "External entity graph accepted");
        var cheat = storageData.CreateCopy(); cheat.SetInt("StorageCheatMode", 1); Reject(cheat, "Storage cheat accepted");
        var tooMany = machineData.CreateCopy(); tooMany.Recipes = new ImmutableArray<RecipeProto>(Enumerable.Repeat(machineData.Recipes!.Value[0], GameCommandCodec.MaxBlueprintProtoEntries + 1).ToArray()); Reject(tooMany, "Oversize recipe array accepted");
        var badPercent = storageData.CreateCopy(); badPercent.SetInt("StorageImportUntilPercent", 100001); Reject(badPercent, "Out-of-range percent accepted");
        var badMode = machineData.CreateCopy(); badMode.SetInt("LogisticsInputMode", 3); Reject(badMode, "Invalid logistics mode accepted");
        Check(!GameCommandCodec.TryEncode(Batch(storageData, storageData), out _), "Duplicate original ID accepted");
        Check(!GameCommandCodec.TryEncode(new BatchCreateStaticEntitiesCmd(Batch(storageData).ConfigData, isFree: true, applyConfiguration: true), out _), "Free building flag accepted");
        Console.WriteLine("PASS all truncations, trailing bytes, unknown/cheat fields, external graphs, bounds and duplicate IDs rejected");
    }
    private static void Check(bool value, string reason) { if (!value) throw new Exception(reason); }
}
