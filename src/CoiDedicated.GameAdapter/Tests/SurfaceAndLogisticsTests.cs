using CoiDedicated.GameAdapter;
using CoiDedicated.Protocol;
using Mafi;
using Mafi.Collections.ImmutableCollections;
using Mafi.Core;
using Mafi.Core.Buildings.Farms;
using Mafi.Core.Buildings.Storages;
using Mafi.Core.Entities;
using Mafi.Core.Entities.Commands;
using Mafi.Core.Input;
using Mafi.Core.Prototypes;
using Mafi.Core.Terrain;
using Mafi.Core.Terrain.Designation;
using Mafi.Core.Vehicles;

internal static class SurfaceAndLogisticsTests
{
    internal static void Run()
    {
        var area=new RectangleTerrainArea2i(new Tile2i(128,132),new RelTile2i(4,4));
        IInputCommand[] valid = {
            new AddSurfaceDesignationsCmd(new Proto.ID("SurfacePlacing"),new ImmutableArray<SurfaceDesignationData>(new[]{new SurfaceDesignationData(area.Origin,0xFF00,new TileSurfaceSlimId(2))})),
            new RemoveSurfaceDesignationsCmd(new ImmutableArray<Tile2i>(new[]{area.Origin}),area),
            new AddSurfaceDecalCmd(new Proto.ID("Arrow"),area,Rotation90.Deg90,true,7),
            new RemoveSurfaceDecalCmd(area),new ChangeSurfaceDecalColorCmd(area,7),
            new RemoveLogisticsZoneCmd(new LogisticsZoneId(1000000)),
            new RenameLogisticsZoneCmd(new LogisticsZoneId(1000000),"Forest zone"),
            new SetLogisticsZoneColorCmd(new LogisticsZoneId(1),0),
            new SetVehicleLogisticsZoneCmd(null,new EntityId(17)),
            new ToggleLogisticsZoneConstructionCmd(new LogisticsZoneId(1),new LogisticsZoneId(0)),
            new TruckJobsAllowCmd(new Proto.ID("Truck"),new LogisticsZoneId(0)),
            new TruckJobsForbidCmd(new Proto.ID("Truck"),new LogisticsZoneId(0)),
            new TruckConstructionRuleSetCmd(new Proto.ID("Truck"),new LogisticsZoneId(0),TruckConstructionRule.Forbid),
            new EntityDisableLogisticsToggleCmd(new EntityId(17),true,EntityLogisticsMode.Auto),
            new FarmSetFertilityTargetCmd(new EntityId(17),Percent.FromRaw(140000)),
            new StorageAlertSetThresholdCmd(new EntityId(17),Percent.FromRaw(25321),false)
        };
        var empty=new DependencyResolverBuilder().BuildAndClear();
        foreach(var command in valid)
        {
            Check(GameCommandCodec.TryEncode(command,out var wire,out string reason),reason);
            // All reads must reject truncated/trailing input before consulting the world.
            for(int i=0;i<wire.Length;i++) Reject(()=>GameCommandCodec.Decode(wire.AsMemory(0,i),empty));
            Reject(()=>GameCommandCodec.Decode(wire.Concat(new byte[]{0}).ToArray(),empty));
        }
        var threshold=valid[^1];
        Check(GameCommandCodec.TryEncode(threshold,out byte[] fixture),"Threshold encoder failed.");
        Check(Convert.ToHexString(fixture)=="0100CA0011000000E962000000","Percentage must retain raw precision, not rounded display value.");
        IInputCommand[] bad = {
            new AddSurfaceDecalCmd(new Proto.ID("Arrow"),area,Rotation90.Deg0,false,8),
            new ChangeSurfaceDecalColorCmd(area,-1),
            new RemoveSurfaceDecalCmd(new RectangleTerrainArea2i(new Tile2i(0,0),new RelTile2i(1024,1024))),
            new RemoveLogisticsZoneCmd(new LogisticsZoneId(-1)),
            new RenameLogisticsZoneCmd(new LogisticsZoneId(1),"Bad\nname"),
            new EntityDisableLogisticsToggleCmd(new EntityId(17),true,(EntityLogisticsMode)9),
            new FarmSetFertilityTargetCmd(new EntityId(17),Percent.FromRaw(140001)),
            new StorageAlertSetThresholdCmd(new EntityId(17),Percent.FromRaw(-1),false),
            new TruckConstructionRuleSetCmd(new Proto.ID("Truck"),new LogisticsZoneId(0),(TruckConstructionRule)9)
        };
        foreach(var command in bad)Check(!GameCommandCodec.TryEncode(command,out _,out _),"Invalid command accepted: "+command.GetType().Name);
    }
    private static void Reject(Action action) {try{action();}catch(ProtocolException){return;}throw new Exception("Malformed input accepted.");}
    private static void Check(bool value,string message){if(!value)throw new Exception(message);}
}
