using System.Text.Json;
using CoiDedicated.GameAdapter;
using CoiDedicated.Protocol;
using Mafi;
using Mafi.Collections.ImmutableCollections;
using Mafi.Core;
using Mafi.Core.Buildings.RuinedBuildings;
using Mafi.Core.Buildings.Storages;
using Mafi.Core.Buildings.Mine;
using Mafi.Core.Buildings.Forestry;
using Mafi.Core.Buildings.Farms;
using Mafi.Core.Buildings.VehicleDepots;
using Mafi.Core.Entities;
using Mafi.Core.Entities.Dynamic;
using Mafi.Core.Entities.Static;
using Mafi.Core.Input;
using Mafi.Core.Products;
using Mafi.Core.Prototypes;
using Mafi.Core.Research;
using Mafi.Core.Terrain;
using Mafi.Core.Terrain.Designation;
using Mafi.Core.Terrain.Trees;
using Mafi.Core.Vehicles;
using Mafi.Core.Vehicles.Commands;
using Mafi.Core.World;

internal static class GameplayCommandTests
{
    internal static void Run(DependencyResolver resolver, object runner, string output)
    {
        var entities = resolver.Resolve<EntitiesManager>();
        var db = resolver.Resolve<ProtosDb>();
        var scheduler = resolver.Resolve<IInputScheduler>();
        var terrain = resolver.Resolve<TerrainManager>();
        var results = new List<object>();
        var center = entities.GetAllEntitiesOfType<StaticEntity>().First().CenterTile.Xy;
        void Execute(IInputCommand original, bool requireSuccess = true)
        {
            if (!GameCommandCodec.TryEncode(original, out byte[] wire, out string reason)) throw new Exception(reason);
            IInputCommand decoded = GameCommandCodec.Decode(wire, resolver);
            if (decoded.GetType() != original.GetType() || !GameCommandCodec.TryEncode(decoded, out byte[] rebuilt)
                || !wire.SequenceEqual(rebuilt)) throw new Exception("Command fields changed in transit: " + original.GetType().Name);
            scheduler.ScheduleInputCmd(decoded);
            for (int i=0; i<2; i++) runner.GetType().GetMethod("Update")!.Invoke(runner, new object[] { 100.ToFix32() });
            if (!decoded.ResultSet || !decoded.IsProcessedAndSynced || (requireSuccess && decoded.HasError))
                throw new Exception("Native command did not complete: " + decoded.GetType().Name + ": " + decoded.ErrorMessage);
            results.Add(new { Command=decoded.GetType().Name, HasError=decoded.HasError, Message=decoded.ErrorMessage, Bytes=wire.Length });
            Console.WriteLine("PASS native command round trip/completion: " + decoded.GetType().Name + (decoded.HasError ? " (normal game rejection: " + decoded.ErrorMessage + ")" : ""));
        }
        var ruin = entities.GetAllEntitiesOfType<Ruins>().FirstOrDefault();
        if (ruin != null) { Execute(new RuinsScrapCmd(ruin.Id, true)); Execute(new RuinsScrapCmd(ruin.Id, false)); }
        Execute(new FleetLoadCrewCmd(), requireSuccess:false);
        Execute(new FleetUnloadCrewCmd(), requireSuccess:false);
        Execute(new DesignateHarvestedTreesCmd(new RectangleTerrainArea2i(center, new RelTile2i(16,16)), true, null));
        Execute(new DesignateHarvestedTreesCmd(new RectangleTerrainArea2i(center, new RelTile2i(16,16)), false, null));
        var vehicle = entities.GetAllEntitiesOfType<Vehicle>().First();
        Execute(new NavigateVehicleToPositionCmd(vehicle.Id, center), requireSuccess:false);
        var pausable = entities.GetAllEntitiesOfType<StaticEntity>().First(e=>e.CanBePaused);
        Execute(new ToggleEnabledCmd(pausable.Id)); Execute(new ToggleEnabledCmd(pausable.Id));
        var storage = entities.GetAllEntitiesOfType<Storage>().First(s=>s.Prototype.GetType().Name=="UnitStorageProto");
        var wood = db.All<ProductProto>().First(p=>p.IsAvailable && storage.Prototype.IsProductSupported(p));
        Execute(new StorageSetProductCmd(storage.Id, wood.Id));
        Execute(new StorageSetSliderStepCmd(storage.Id, (int?)5, (int?)10, (int?)0, (int?)10));
        Execute(new StorageAlertSetEnabledCmd(storage.Id, true, false));
        Execute(new StorageDisableLogisticsToggleCmd(storage.Id, true, true));
        Execute(new StorageDisableLogisticsToggleCmd(storage.Id, true, false));
        Execute(new StorageClearProductCmd(storage));
        Execute(new SetPlanningModeEnabledCmd(true)); Execute(new SetPlanningModeEnabledCmd(false));
        Execute(new ResearchStopCmd());
        var farm=entities.GetAllEntitiesOfType<Farm>().First();
        Execute(new FarmAssignCropCmd(farm.Id, null, 0));
        var depot=entities.GetAllEntitiesOfType<VehicleDepotBase>().First(d=>d.Prototype.GetType().Name=="VehicleDepotProto");
        var truck=db.All<DrivingEntityProto>().First(p=>p.IsAvailable && depot.Prototype.BuildableEntities.Contains(p));
        if(!truck.IsUnlockedAndAvailable)
        {
            GameCommandCodec.TryEncode(new AddVehicleToBuildQueueCmd(truck.Id,depot.Id,1),out byte[] locked);
            try { GameCommandCodec.Decode(locked,resolver); throw new Exception("Locked vehicle was accepted."); }
            catch(ProtocolException) { }
            resolver.Resolve<UnlockedProtosDb>().Unlock(truck);
        }
        Execute(new AddVehicleToBuildQueueCmd(truck.Id,depot.Id,1));
        var mine=entities.GetAllEntitiesOfType<MineTower>().First();
        var forestry=entities.GetAllEntitiesOfType<ForestryTower>().First();
        var area=PolygonTerrainArea2i.FromRectOriginSize(center,new RelTile2i(16,16));
        Execute(new MineTowerAreaChangeCmd(mine.Id,area));
        Execute(new ForestryTowerAreaChangeCmd(forestry.Id,area));
        Execute(new MineTowerAreaChangeCmd(mine.Id,PolygonTerrainArea2i.FromVertices(ImmutableArray<Mafi.Vector2i>.Empty)));
        var designation = db.All<TerrainDesignationProto>().First(p=>p.IsAvailable);
        var origin = new Tile2i(center.X & ~3, center.Y & ~3);
        int h = terrain.GetHeight(origin).TilesHeightRounded.Value;
        Execute(new AddTerrainDesignationsCmd(designation.Id, new ImmutableArray<DesignationData>(new[] {
            new DesignationData(origin, new HeightTilesI(h)) })), requireSuccess:false);
        Execute(new RemoveDesignationsCmd(new ImmutableArray<Tile2i>(new[] { origin })));
        File.WriteAllText(Path.Combine(output,"gameplay-results.json"), JsonSerializer.Serialize(results,new JsonSerializerOptions {WriteIndented=true}));
    }
}
