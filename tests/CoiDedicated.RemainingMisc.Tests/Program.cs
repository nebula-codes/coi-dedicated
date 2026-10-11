using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using CoiDedicated.GameAdapter;
using CoiDedicated.Protocol;
using Mafi;
using Mafi.Collections;
using Mafi.Collections.ImmutableCollections;
using Mafi.Core;
using Mafi.Core.Buildings.Storages;
using Mafi.Core.Entities;
using Mafi.Core.Entities.Commands;
using Mafi.Core.Entities.Static;
using Mafi.Core.Entities.Static.Commands;
using Mafi.Core.Factory.Transports;
using Mafi.Core.Factory.Zippers;
using Mafi.Core.Game;
using Mafi.Core.Input;
using Mafi.Core.MessageNotifications;
using Mafi.Core.Notifications;
using Mafi.Core.Products;
using Mafi.Core.Prototypes;
using Mafi.Core.SpaceProgram;
using Mafi.Core.Terrain;
using Mafi.Core.Terrain.Designation;
using Mafi.Core.Terrain.Props;
using Mafi.Core.Terrain.Trees;

internal static class Program
{
    private static int Main()
    {
        string game=Environment.GetEnvironmentVariable("COI_GAME_DIR")??@"F:\SteamLibrary\steamapps\common\Captain of Industry";
        AssemblyLoadContext.Default.Resolving+=(_,name)=>name.Name is "Mafi" or "Mafi.Core" or "Mafi.Base" ? AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.Combine(game,"Captain of Industry_Data","Managed",name.Name+".dll")):null;
        Environment.CurrentDirectory=game;return Test();
    }
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static int Test()
    {
        var id=new EntityId(17);var ids=new ImmutableArray<EntityId>(new[]{id});var area=new RectangleTerrainArea2i(new Tile2i(12,12),new RelTile2i(4,4));var p=new Tile3i(12,12,1);var end=new Tile3i(16,12,1);
        var sections=new ImmutableArray<Pair<Tile3i,Tile3i>>(new[]{new Pair<Tile3i,Tile3i>(p,end)});
        IInputCommand[] valid={
            new StorageCheatClearProductCmd(id),new StorageSetCheatModeCmd(id,Storage.StorageCheatMode.KeepFull),new SetDumpHeightOffsetCmd(id,new ThicknessTilesI(-3)),
            new ReplaceEntityCmd(id,new Proto.ID("StorageLooseT2")),new CutEntitiesForPasteCmd(ids,ImmutableArray<TransportCutRange>.Empty),
            new FinishBuildOfStaticEntityCmd(id,true),new SpendUpointsOnEntitiesCmd(ids,area),new StartDeconstructionOfTransportSubSectionsCmd(id,sections,EntityRemoveReason.Remove,true),new RecoverOceanAccessCmd(id,50),new ToggleEnabledGroupCmd(ids,true),
            new AddTransportPillarCmd(id,3),new DeconstructTransportSegmentCmd(id,p,end,true),new StackerTowerSelectProductForSlopePreviewCmd(id,Option<ProductProto>.None),new StackerTowerSetDumpRadiusCmd(id,15.Tiles(),null),new StackerTowerSetMaxSegmentsCmd(id,200),new ZipperSetPriorityPortsCmd(id,'A',null),
            new ChangeGameDifficultyCmd(GameDifficultyConfig.Normal()),new MessageNotificationDismissCmd(new MessageNotificationId(1)),new NotificationDismissCmd(new ImmutableArray<NotificationId>(new[]{new NotificationId(uint.MaxValue)})),
            new PinnedProductReorderCmd(new ProductProto.ID("Wood"),0),new BringAsteroidToOrbitCmd(new AsteroidId(0)),new DiscardAsteroidCmd(new AsteroidId(5)),new DropAsteroidAtCmd(new AsteroidId(5),new Tile2i(64,64)),
            new ReorderLaunchPadCargoBufferCmd(id,1,0),new SetOrbitCargoLimitCmd(new ProductProto.ID("Wood"),new Quantity(1234)),new QuickFulfillDesignationsCmd(area,Option<ProductProto>.None),new QuickRemovePropsCmd(area),new RemoveManualPlantTreeCmd(new TreeId(new Tile2iSlim(15,16)))
        };
        var resolver=new DependencyResolverBuilder().BuildAndClear();int malformed=0;var schemas=new HashSet<ushort>();
        foreach(var command in valid)
        {
            Check(GameCommandCodec.TryEncode(command,out var bytes,out string reason),command.GetType().Name+": "+reason);schemas.Add(BitConverter.ToUInt16(bytes,2));
            for(int n=0;n<bytes.Length;n++){Reject(()=>GameCommandCodec.Decode(bytes.AsMemory(0,n),resolver));malformed++;}
            Reject(()=>GameCommandCodec.Decode(bytes.Concat(new byte[]{0}).ToArray(),resolver));malformed++;
        }
        IInputCommand[] invalid={
            new StorageSetCheatModeCmd(id,(Storage.StorageCheatMode)3),new SetDumpHeightOffsetCmd(id,new ThicknessTilesI(int.MaxValue)),new RecoverOceanAccessCmd(id,0),new StackerTowerSetMaxSegmentsCmd(id,201),
            new StackerTowerSetDumpRadiusCmd(id,(-1).Tiles(),null),new AddTransportPillarCmd(id,-1),new ToggleEnabledGroupCmd(new ImmutableArray<EntityId>(new[]{id,id}),false),
            new CutEntitiesForPasteCmd(new ImmutableArray<EntityId>(Enumerable.Repeat(id,1025).ToArray()),ImmutableArray<TransportCutRange>.Empty),
            new CutEntitiesForPasteCmd(new ImmutableArray<EntityId>(Enumerable.Range(1,129).Select(x=>new EntityId(x)).ToArray()),ImmutableArray<TransportCutRange>.Empty),
            new CutEntitiesForPasteCmd(ImmutableArray<EntityId>.Empty,ImmutableArray<TransportCutRange>.Empty),
            new CutEntitiesForPasteCmd(ids,new ImmutableArray<TransportCutRange>(new[]{new TransportCutRange(p,end)})),
            new StartDeconstructionOfTransportSubSectionsCmd(id,sections,(EntityRemoveReason)255,false),new SetOrbitCargoLimitCmd(new ProductProto.ID("Wood"),new Quantity(-1)),
            new NotificationDismissCmd(new ImmutableArray<NotificationId>(new[]{new NotificationId(1),new NotificationId(1)})),new QuickRemovePropsCmd(new RectangleTerrainArea2i(new Tile2i(0,0),new RelTile2i(1024,1024)))
        };
        foreach(var command in invalid)Check(!GameCommandCodec.TryEncode(command,out _,out _),"Invalid request encoded: "+command.GetType().Name);
        // A complete difficulty packet containing an out-of-range option must fail before any resolver access.
        Check(GameCommandCodec.TryEncode(valid.Single(v=>v is ChangeGameDifficultyCmd),out var difficulty),"Difficulty encode failed.");difficulty[4]=255;difficulty[5]=255;Reject(()=>GameCommandCodec.Decode(difficulty,resolver));
        // A hostile peer cannot bypass the encoder to cut a selection larger than one paste.
        using(var writer=new PayloadWriter(GameCommandCodec.MaxCommandBytes)){
            writer.WriteUInt16(GameCommandCodec.Version);writer.WriteUInt16(414);writer.WriteInt32(129);
            for(int i=1;i<=129;i++)writer.WriteInt32(i);writer.WriteInt32(0);
            Reject(()=>GameCommandCodec.Decode(writer.ToArray(),resolver));
        }
        // Nor can it submit a transport cut which native processing would destroy but paste rejects.
        using(var writer=new PayloadWriter(GameCommandCodec.MaxCommandBytes)){
            writer.WriteUInt16(GameCommandCodec.Version);writer.WriteUInt16(414);writer.WriteInt32(0);writer.WriteInt32(1);
            foreach(var point in new[]{p,end}){writer.WriteInt32(point.X);writer.WriteInt32(point.Y);writer.WriteInt32(point.Z);}
            Reject(()=>GameCommandCodec.Decode(writer.ToArray(),resolver));
        }
        // Simultaneous reorders must not reuse stale list indices admitted in the same tick.
        foreach(ushort kind in new ushort[]{352,355,356,357,358,373,374,414,420,429,433})
            Check(GameCommandCodec.RequiresIsolatedTick(new byte[]{1,0,(byte)kind,(byte)(kind>>8)}),"Missing isolated admission for "+kind);
        Check(schemas.Count==28,"Missing command schema fixture.");
        Console.WriteLine($"PASS {schemas.Count} command schemas, {malformed} malformed packets, {invalid.Length} invalid encode cases, difficulty option bounds, and isolated admission checks.");return 0;
    }
    private static void Check(bool value,string message){if(!value)throw new Exception(message);}
    private static void Reject(Action action){try{action();}catch(ProtocolException){return;}throw new Exception("Malformed request accepted.");}
}
