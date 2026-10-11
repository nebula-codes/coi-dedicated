using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using System.Text.Json;
using CoiDedicated.GameAdapter;
using CoiDedicated.Protocol;
using Mafi;
using Mafi.Collections;
using Mafi.Collections.ImmutableCollections;
using Mafi.Core;
using Mafi.Core.Entities;
using Mafi.Core.Entities.Dynamic;
using Mafi.Core.Entities.Static;
using Mafi.Core.Entities.Static.Layout;
using Mafi.Core.Input;
using Mafi.Core.Products;
using Mafi.Core.Prototypes;
using Mafi.Core.Terrain;
using Mafi.Core.Trains;

internal static class Program
{
    private static int Main(string[] args)
    {
        string game=Path.GetFullPath(Environment.GetEnvironmentVariable("COI_GAME_DIR")??@"F:\SteamLibrary\steamapps\common\Captain of Industry");
        AssemblyLoadContext.Default.Resolving+=(_,name)=>name.Name is "Mafi" or "Mafi.Core" or "Mafi.Base"?AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.Combine(game,"Captain of Industry_Data","Managed",name.Name+".dll")):null;
        Environment.CurrentDirectory=game;
        if(args.Length==1&&args[0]=="--unit")return Unit();
        if(args.Length!=2)throw new ArgumentException("Usage: --unit OR SOURCE_SAVE NEW_ISOLATED_OUTPUT_DIR (absolute paths)");
        string source=Path.GetFullPath(args[0]),output=Path.GetFullPath(args[1]);
        if(!File.Exists(source)||Directory.Exists(output))throw new ArgumentException("Existing source and fresh output required.");Directory.CreateDirectory(output);string fixture=Path.Combine(output,"fixture.save");File.Copy(source,fixture);
        return Run(fixture,output,game);
    }
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static int Unit()
    {
        var id=new EntityId(17);var line=new TrainLineId(1);var network=new TrainNetworkId(2);var item=new TrainScheduleItemId(line,0);var train=new TrainId(1);
        var color=new TrainLineColor(new ColorRgba(0x12345678u),new ColorRgba(0xabcdef01u),new TrainColor(0x123456789abcdef0ul));
        var tracks=new ImmutableArray<EntityId>(new[]{id});var blocks=new ImmutableArray<Pair<EntityId,byte>>(new[]{new Pair<EntityId,byte>(id,0)});
        var valid=new List<IInputCommand>{
            new AddTrainTrackPillarCmd(id,0),new QuickRepairTrainCmd(train,new ProductProto.ID("MaintenanceT1")),new RecoverTrainCmd(train,true,id),
            new RemoveDepartConditionCmd(item,id,0),new RemovePathfindingRestrictionCmd(id,0),new ReorderDepartConditionCmd(item,id,0,1),new ReplaceEntityInScheduleCmd(null,id,new EntityId(18)),
            new ReverseTracksCmd(tracks),new ScrapTrainCarCmd(id,new DynamicEntityProto.ID("Wagon")),new SetDepartConditionCombineMethodCmd(item,id,0,true),
            new SetTrainLineColorCmd(line,color),new SetTrainLineIcon(line,Option<IProtoWithIcon>.None),new SetTrainLineLocoColorModeCmd(line,LocoColorMode.Custom),new SetTrainLineLocoCustomColorCmd(line,color),
            new SetTrainNetworkColorCmd(network,color),new SetTrainNetworkLocoColorModeCmd(network,LocoColorMode.NoOverride),new SetTrainNetworkLocoCustomColorCmd(network,color),
            new SetTrainTrackCriticalBlockStateCmd(blocks,true),new SetTrainTrackSuperBlockStateCmd(blocks,false),new SetWagonProductFilterCmd(train,0,0,null),new ToggleBidirectionalCommand(tracks),
            new TrainContainsDepartConditionEditCmd(item,id,-1,true,TrainContainsDepartCondition.ComparisonMode.SpecificProduct,Percent.FromRaw(65432),null,true,false),new TrainDurationDepartConditionEditCmd(item,id,-1,9999.Days()),
            new EditPathfindingRestrictionCmd(id,-1,new TrainCarriesProductPathfindingRestriction{IsDeny=true}),
            new EditPathfindingRestrictionCmd(id,0,new TrainGroupingPathfindingRestriction{Kind=TrainGroupingPathfindingRestriction.GroupingKind.Network,LineId=line,NetworkId=network}),
            new EditPathfindingRestrictionCmd(id,1,new TrainLengthPathfindingRestriction{Threshold=9999.Tiles(),UseLessThanInsteadOfGreaterEqual=true}),
            new EditPathfindingRestrictionCmd(id,2,new TrainMaxSpeedPathfindingRestriction{Threshold=80.Kmh()}),
            new EditPathfindingRestrictionCmd(id,3,new TrainWagonTypePathfindingRestriction{Kind=TrainWagonTypePathfindingRestriction.WagonTypeKind.Universal})
        };
        int rejected=0;var resolver=new DependencyResolverBuilder().BuildAndClear();
        foreach(var command in valid){Check(GameCommandCodec.TryEncode(command,out var bytes,out string reason),reason);for(int i=0;i<bytes.Length;i++){Reject(()=>GameCommandCodec.Decode(bytes.AsMemory(0,i),resolver));rejected++;}Reject(()=>GameCommandCodec.Decode(bytes.Concat(new byte[]{0}).ToArray(),resolver));rejected++;}
        IInputCommand[] invalid={new AddTrainTrackPillarCmd(id,-1),new SetWagonProductFilterCmd(train,0,-1,null),new ReverseTracksCmd(new ImmutableArray<EntityId>(new[]{id,id})),new ToggleBidirectionalCommand(ImmutableArray<EntityId>.Empty),new TrainDurationDepartConditionEditCmd(item,id,-1,Duration.Zero),new TrainContainsDepartConditionEditCmd(item,id,-1,mode:(TrainContainsDepartCondition.ComparisonMode)3),new TrainContainsDepartConditionEditCmd(item,id,-1,percent:Percent.FromRaw(100001)),new SetTrainLineLocoColorModeCmd(line,(LocoColorMode)3),new EditPathfindingRestrictionCmd(id,-2,new TrainLengthPathfindingRestriction()),new EditPathfindingRestrictionCmd(id,0,new TrainWagonTypePathfindingRestriction{Kind=(TrainWagonTypePathfindingRestriction.WagonTypeKind)6})};
        foreach(var command in invalid)Check(!GameCommandCodec.TryEncode(command,out _,out _),"Invalid train command encoded.");
        using(var w=new PayloadWriter()){w.WriteUInt16(1);w.WriteUInt16(351);w.WriteInt32(17);w.WriteInt32(129);Reject(()=>GameCommandCodec.Decode(w.ToArray(),resolver));}
        using(var w=new PayloadWriter()){w.WriteUInt16(1);w.WriteUInt16(352);w.WriteInt32(17);w.WriteInt32(-1);w.WriteUInt16(6);Reject(()=>GameCommandCodec.Decode(w.ToArray(),resolver));}
        Console.WriteLine($"PASS {valid.Count} representative encodes, {rejected} truncation/trailing rejection cases, {invalid.Length} invalid encodes and bounded train/restriction discriminators.");return 0;
    }
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static int Run(string fixture,string output,string game)
    {
        Assembly host=Assembly.Load("CoiDedicated");object options=host.GetType("CoiDedicated.HostOptions",true)!.GetMethod("Parse")!.Invoke(null,new object[]{new[]{"--game-dir",game,"--load",fixture,"--data-dir",output,"--steps","0"}})!;
        Action<DependencyResolver,object> callback=(resolver,runner)=>Test(resolver,runner,output);
        return (int)host.GetType("CoiDedicated.GameHost",true)!.GetMethod("Run")!.Invoke(null,new object[]{options,callback,CancellationToken.None})!;
    }
    private static void Test(DependencyResolver d,object runner,string output)
    {
        var entities=d.Resolve<EntitiesManager>();var scheduler=d.Resolve<IInputScheduler>();var db=d.Resolve<ProtosDb>();var terrain=d.Resolve<TerrainManager>();var passed=new List<string>();var rejected=new List<string>();
        runner.GetType().GetProperty("ExtraSimUpdateMs")!.SetValue(runner,Fix32.Zero);d.Resolve<Mafi.Core.Simulation.SimLoopEvents>().SetSimSpeed(1);
        void Update()=>runner.GetType().GetMethod("Update")!.Invoke(runner,new object[]{100.ToFix32()});
        IInputCommand Execute(IInputCommand original){Check(GameCommandCodec.TryEncode(original,out var bytes,out string reason),reason);var decoded=GameCommandCodec.Decode(bytes,d);Check(GameCommandCodec.TryEncode(decoded,out var rebuilt)&&bytes.SequenceEqual(rebuilt),"Canonical wire changed: "+original.GetType().Name);scheduler.ScheduleInputCmd(decoded);Update();Update();Check(decoded.IsProcessedAndSynced&&decoded.ResultSet&&!decoded.HasError,"Native command failed: "+decoded.GetType().Name+": "+decoded.ErrorMessage);passed.Add(decoded.GetType().Name);Console.WriteLine("PASS native: "+decoded.GetType().Name);return decoded;}
        void RejectWorld(IInputCommand command){Check(GameCommandCodec.TryEncode(command,out var bytes,out string reason),reason);Reject(()=>GameCommandCodec.Decode(bytes,d));rejected.Add(command.GetType().Name);}
        var center=entities.GetAllEntitiesOfType<StaticEntity>().First().CenterTile.Xy;
        TileTransform FindConstruction(LayoutEntityProto proto){var factory=d.Resolve<LayoutEntityAddRequestFactory>();for(int dz=0;dz<=12;dz+=4)for(int dx=-192;dx<=192;dx+=4)for(int dy=-192;dy<=192;dy+=4){var xy=new Tile2i(center.X+dx,center.Y+dy);if(xy.X<terrain.MinOnLimits.X+40||xy.Y<terrain.MinOnLimits.Y+40||xy.X>=terrain.MaxOnLimitsExcl.X-40||xy.Y>=terrain.MaxOnLimitsExcl.Y-40)continue;var transform=new TileTransform(new Tile3i(xy,terrain.GetHeight(xy).TilesHeightRounded.Value+dz));var request=factory.CreateRequestFor(proto,new EntityAddRequestData(transform));bool valid;try{valid=!entities.CanAdd(request).IsError;}finally{request.ReturnToPool();}if(valid)return transform;}throw new Exception("No valid fixture location for "+proto.Id.Value);}
        StaticEntity AddFixture(LayoutEntityProto proto){d.Resolve<UnlockedProtosDb>().Unlock(proto);var transform=FindConstruction(proto);var entity=d.TryInvokeFactoryHierarchy<StaticEntity>(proto,transform).Value;Check(!entities.TryAddEntity(entity).IsError,"Fixture placement failed.");entity.MakeFullyConstructed();Update();Update();return entity;}
        Execute(new CreateNewTrainLineCmd());var line=d.Resolve<TrainLinesManager>().Lines.AsEnumerable().Last();Execute(new CreateNewTrainNetworkCmd());var network=d.Resolve<TrainNetworksManager>().Networks.Values.Last();
        var color=new TrainLineColor(ColorRgba.Red,ColorRgba.Blue,new TrainColor(ColorRgba.Yellow,ColorRgba.Green));
        Execute(new SetTrainLineColorCmd(line.Id,color));Execute(new SetTrainLineLocoColorModeCmd(line.Id,LocoColorMode.Custom));Execute(new SetTrainLineLocoCustomColorCmd(line.Id,color));
        var product=db.All<ProductProto>().First(p=>p.IsAvailable);Execute(new SetTrainLineIcon(line.Id,Option<IProtoWithIcon>.Some(product)));Execute(new SetTrainLineIcon(line.Id,Option<IProtoWithIcon>.None));
        Execute(new SetTrainNetworkColorCmd(network.Id,color));Execute(new SetTrainNetworkLocoColorModeCmd(network.Id,LocoColorMode.Custom));Execute(new SetTrainNetworkLocoCustomColorCmd(network.Id,color));
        var stationProto=db.All<TrainStationRootBaseProto>().Where(p=>p.IsAvailable&&!p.CannotBeBuiltByPlayer).OrderBy(p=>p.Layout.TilesCount).First();var station=AddFixture(stationProto);Execute(new AddNewScheduleItemToTrainLineCmd(line.Id,station.Id,10));var item=new TrainScheduleItemId(line.Id,0);
        var root=line.Schedule[0].StationRoots.First();var conditions=line.Schedule[0].DepartConditions[root];int start=conditions.Count;
        Execute(new TrainContainsDepartConditionEditCmd(item,root.Id,-1,false,TrainContainsDepartCondition.ComparisonMode.SpecificProduct,Percent.FromRaw(54321),product.Id,true,false));
        Check(conditions.Count==start+1&&conditions[start] is TrainContainsDepartCondition c&&c.Percent.RawValue==54321&&c.Product.HasValue,"Contains rule did not preserve fields.");
        Execute(new TrainContainsDepartConditionEditCmd(item,root.Id,start,productIdSpecified:true));Check(((TrainContainsDepartCondition)conditions[start]).Product.IsNone,"Explicit product clear was lost.");
        Execute(new TrainDurationDepartConditionEditCmd(item,root.Id,-1,7.Days()));Check(conditions[start+1] is TrainDurationDepartCondition duration&&duration.Delay==7.Days(),"Delay was lost.");
        Execute(new SetDepartConditionCombineMethodCmd(item,root.Id,start,true));Check(((TrainScheduleDepartConditionBase)conditions[start]).CombineAsOrInsteadOfAnd,"Combine method lost.");
        Execute(new ReorderDepartConditionCmd(item,root.Id,start,start+1));Check(conditions[start] is TrainDurationDepartCondition,"Condition order did not change.");
        RejectWorld(new TrainContainsDepartConditionEditCmd(item,root.Id,start,percent:Percent.FromRaw(20000)));RejectWorld(new RemoveDepartConditionCmd(item,root.Id,255));
        Execute(new RemoveDepartConditionCmd(item,root.Id,start+1));Execute(new RemoveDepartConditionCmd(item,root.Id,start));Check(conditions.Count==start,"Condition removal failed.");
        var station2=AddFixture(stationProto);Execute(new ReplaceEntityInScheduleCmd(item,station.Id,station2.Id));Check(line.Schedule[0].StationRoots.Any(r=>r.Id==station2.Id),"Schedule station not replaced.");
        var waypoint=station as ITrainStationRootWithPathfindingRestrictions;
        if(waypoint==null){foreach(var proto in db.All<TrainStationWaypointBaseProto>().Where(p=>p.IsAvailable&&!p.CannotBeBuiltByPlayer)){waypoint=AddFixture(proto) as ITrainStationRootWithPathfindingRestrictions;if(waypoint!=null)break;}}
        Check(waypoint!=null,"No waypoint fixture.");var wp=waypoint!;int initial=wp.PathfindingRestrictions.Count;
        ITrainPathfindingRestriction[] restrictions={new TrainCarriesProductPathfindingRestriction{IsDeny=true,Product=product},new TrainGroupingPathfindingRestriction{Kind=TrainGroupingPathfindingRestriction.GroupingKind.Line,LineId=line.Id,NetworkId=network.Id},new TrainLengthPathfindingRestriction{Threshold=40.Tiles(),UseLessThanInsteadOfGreaterEqual=true},new TrainMaxSpeedPathfindingRestriction{Threshold=80.Kmh()},new TrainWagonTypePathfindingRestriction{Kind=TrainWagonTypePathfindingRestriction.WagonTypeKind.Countable}};
        foreach(var restriction in restrictions)Execute(new EditPathfindingRestrictionCmd(wp.Id,-1,restriction));Check(wp.PathfindingRestrictions.Count==initial+5,"Restriction insertion failed.");
        Execute(new EditPathfindingRestrictionCmd(wp.Id,initial+1,new TrainGroupingPathfindingRestriction{Kind=TrainGroupingPathfindingRestriction.GroupingKind.Line,LineId=line.Id,NetworkId=new TrainNetworkIdOrNone(int.MaxValue)}));Check(((TrainGroupingPathfindingRestriction)wp.PathfindingRestrictions[initial+1]).NetworkId.Raw==int.MaxValue,"Inactive stale grouping selection was not preserved.");
        Execute(new EditPathfindingRestrictionCmd(wp.Id,initial,new TrainCarriesProductPathfindingRestriction()));Check(((TrainCarriesProductPathfindingRestriction)wp.PathfindingRestrictions[initial]).Product.IsNone,"Restriction product clear lost.");
        RejectWorld(new RemovePathfindingRestrictionCmd(wp.Id,255));for(int i=0;i<5;i++)Execute(new RemovePathfindingRestrictionCmd(wp.Id,initial));Check(wp.PathfindingRestrictions.Count==initial,"Restriction removal failed.");
        var railProto=db.All<TrainTrackProto>().Where(p=>p.IsAvailable&&!p.CannotBeBuiltByPlayer&&!p.IsElevated&&p.IsStraight&&p.ElectrificationType==ElectrificationType.None).OrderBy(p=>p.Layout.TilesCount).First();d.Resolve<UnlockedProtosDb>().Unlock(railProto);var railTransform=FindConstruction(railProto);
        var step=new TrainTrackPlanStep(railProto,railProto.GetTransformedTrajectory(railTransform.Transform90RotFlip).EndDirection,TrainTrackTrajectoryDirection.Forward,railTransform,ImmutableArray<TrainTrackPillarInfo>.Empty,ImmutableArray<TrainTrackPoleInfo>.Empty);
        Execute(new CreateTrainTrackFromPlanCmd(new TrainTrackPlan(new ImmutableArray<TrainTrackPlanStep>(new[]{step}),true,false,options:new TrainTrackPathFinderOptions(buildDirection:TrainTrackTrajectoryDirection.Forward))));
        var rail=entities.GetAllEntitiesOfType<TrainTrack>().Single(e=>e.Transform==railTransform);rail.MakeFullyConstructed();Update();Update();var tracks=new ImmutableArray<EntityId>(new[]{rail.Id});var blocks=new ImmutableArray<Pair<EntityId,byte>>(new[]{new Pair<EntityId,byte>(rail.Id,0)});
        var oldDirection=rail.Direction;Execute(new ReverseTracksCmd(tracks));Check(rail.Direction!=oldDirection,"Rail direction unchanged.");Execute(new ToggleBidirectionalCommand(tracks));Check(rail.Direction==TrainTrackTrajectoryDirection.Bidirectional,"Bidirectional state lost.");
        Execute(new SetTrainTrackCriticalBlockStateCmd(blocks,true));Execute(new SetTrainTrackCriticalBlockStateCmd(blocks,false));Execute(new SetTrainTrackSuperBlockStateCmd(blocks,true));Execute(new SetTrainTrackSuperBlockStateCmd(blocks,false));
        RejectWorld(new SetTrainTrackCriticalBlockStateCmd(new ImmutableArray<Pair<EntityId,byte>>(new[]{new Pair<EntityId,byte>(rail.Id,255)}),true));RejectWorld(new AddTrainTrackPillarCmd(rail.Id,255));
        foreach(var pillarProto in db.All<TrainTrackPillarProto>())d.Resolve<UnlockedProtosDb>().Unlock(pillarProto);
        var pillarTrack=(ITrainTrackMayBeElevatedFriend)rail;
        var pillarCandidates=pillarTrack.GetTransformedPillarsData(pillarTrack.TrackTransform.Transform90RotFlip);
        int pillarBlock=-1;for(int i=0;i<pillarCandidates.Length;i++)if(d.Resolve<TrainTracksPillarManager>().CanPlacePillar(pillarTrack,i,out _,out _)){pillarBlock=i;break;}
        Check(pillarBlock>=0,"No valid pillar fixture block.");Execute(new AddTrainTrackPillarCmd(rail.Id,pillarBlock));Check(pillarTrack.Pillars.Length>0,"Pillar was not added.");
        TestTrainConstruction(d,Execute,AddFixture,RejectWorld,Update);
        File.WriteAllText(Path.Combine(output,"results.json"),JsonSerializer.Serialize(new {Passed=passed,Distinct=passed.Distinct().Count(),Rejected=rejected},new JsonSerializerOptions{WriteIndented=true}));
    }
    private static void TestTrainConstruction(DependencyResolver d,Func<IInputCommand,IInputCommand> execute,Func<LayoutEntityProto,StaticEntity> addFixture,Action<IInputCommand> reject,Action update)
    {
        var db=d.Resolve<ProtosDb>();foreach(var p in db.All<TrainCarBaseProto>().Where(p=>p.IsAvailable))d.Resolve<UnlockedProtosDb>().Unlock(p);
        var proto=db.All<TrainDepotProto>().Where(p=>p.IsAvailable&&!p.CannotBeBuiltByPlayer).OrderBy(p=>p.Layout.TilesCount).First();var depot=(TrainDepot)addFixture(proto);
        var loco=db.All<LocomotiveProto>().First(p=>p.IsAvailable);var wagon=db.All<CargoWagonProto>().First(p=>p.IsAvailable);var product=db.All<ProductProto>().First(p=>p.IsAvailable&&wagon.ProductType.Matches(p.Type));
        var designs=new ImmutableArray<TrainCarDesignWithId>(new[]{new TrainCarDesignWithId(loco.Id),new TrainCarDesignWithId(wagon.Id,false,product.Id)});
        execute(new BuildTrainCmd(depot,designs,TrainLineIdOrNone.None,TrainNetworkIdOrNone.None,TrainIdOrNone.None));Check(depot.TrainBuildQueue.Count>0,"Train construction was not queued.");
        // Public native fixture helpers create an isolated train; the network never exposes these helpers.
        var creator=d.Resolve<EntitiesCreator>();Check(creator.TryCreateTrainCar<TrainCarBase>(loco,out var locoCar),"Fixture locomotive failed.");Check(creator.TryCreateTrainCar<TrainCarBase>(wagon,out var wagonCar),"Fixture wagon failed.");
        var train=d.Resolve<TrainsManager>().CreateNewTrain(depot);Check(train.TrySetTrainCars(new ImmutableArray<TrainCarBase>(new[]{locoCar,wagonCar}),out string error),error);
        execute(new SetWagonProductFilterCmd(train.TrainId,1,0,product.Id));Check(((CargoWagon)wagonCar).GetSubCargoWagon(0).OnlyAllowedProduct.HasValue,"Cargo filter was not set.");
        execute(new SetWagonProductFilterCmd(train.TrainId,1,0,null));Check(((CargoWagon)wagonCar).GetSubCargoWagon(0).OnlyAllowedProduct.IsNone,"Cargo filter did not clear.");
        reject(new SetWagonProductFilterCmd(train.TrainId,0,0,product.Id));reject(new SetWagonProductFilterCmd(train.TrainId,1,15,product.Id));
        var locomotive=(Locomotive)locoCar;locomotive.Maintenance.SetCurrentMaintenanceTo(Percent.FromRaw(90000));
        execute(new QuickRepairTrainCmd(train.TrainId,locomotive.Maintenance.Costs.Product.Id));Check(locomotive.Maintenance.Status.MissingPointsToFull.IsNotPositive,"Train maintenance was not repaired.");
        train.SetDrivingMode(TrainDrivingMode.None);Check(train.TrySpawnAt(depot,depot,RelTile1f.One,out _,out _),"Fixture train failed to spawn.");execute(new RecoverTrainCmd(train.TrainId,true,depot.Id));Check(train.IsPaused,"Recovered train pause was not preserved.");
        Check(depot.TryCreateTrainCar_TestOnly(wagon),"Fixture stored wagon failed.");int stored=depot.CarsWithoutTrain[wagon];execute(new ScrapTrainCarCmd(depot.Id,wagon.Id));Check(depot.CarsWithoutTrain.GetValueOrDefault(wagon)==stored-1,"Stored wagon was not scrapped.");
    }
    private static void Reject(Action a){try{a();}catch(ProtocolException){return;}throw new Exception("Invalid train payload accepted.");}
    private static void Check(bool value,string message){if(!value)throw new Exception(message);}
}
