using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using System.Text.Json;
using System.Collections;
using System.Net.Sockets;
using System.Threading.Channels;
using CoiDedicated.GameAdapter;
using CoiDedicated.Protocol;
using Mafi;
using Mafi.Core;
using Mafi.Core.Entities;
using Mafi.Core.Entities.Dynamic;
using Mafi.Core.Entities.Static;
using Mafi.Core.Input;
using Mafi.Core.Vehicles;
using Mafi.Core.Vehicles.Commands;
using Mafi.Collections.ImmutableCollections;
using Mafi.Core.Bridges;
using Mafi.Core.Buildings.Farms;
using Mafi.Core.Buildings.Forestry;
using Mafi.Core.Buildings.Storages;
using Mafi.Core.Buildings.VehicleDepots;
using Mafi.Core.Entities.Commands;
using Mafi.Core.Entities.Static.Commands;
using Mafi.Core.Entities.Static.Layout;
using Mafi.Core.Prototypes;
using Mafi.Core.Terrain;
using Mafi.Core.Terrain.Designation;
using Mafi.Core.Terrain.Surfaces;
using Mafi.Core.Trains;

internal static class Program
{
    private static int Main(string[] args)
    {
        if (args.Length < 2) throw new ArgumentException("Usage: GameplayExpansion.Tests SOURCE_SAVE NEW_ISOLATED_OUTPUT_DIR [GAME_DIR]");
        string source = Path.GetFullPath(args[0]);
        string output = Path.GetFullPath(args[1]);
        string game = Path.GetFullPath(args.Length > 2 ? args[2] : Environment.GetEnvironmentVariable("COI_GAME_DIR")
            ?? @"F:\SteamLibrary\steamapps\common\Captain of Industry");
        if (!File.Exists(source)) throw new FileNotFoundException("An existing CoI 0.8.7d fixture save is required.", source);
        if (Directory.Exists(output)) throw new ArgumentException("Use a new isolated output directory.");
        Directory.CreateDirectory(output);
        string fixture = Path.Combine(output, "fixture.save");
        File.Copy(source, fixture);
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
        Action<DependencyResolver, object> callback = (resolver, runner) => Test(resolver, runner, output);
        return (int)host.GetType("CoiDedicated.GameHost", true)!.GetMethod("Run")!.Invoke(null,
            new object[] { options, callback, CancellationToken.None })!;
    }

    private static void Test(DependencyResolver resolver, object runner, string output)
    {
        var entities=resolver.Resolve<EntitiesManager>(); var scheduler=resolver.Resolve<IInputScheduler>();
        var zones=resolver.Resolve<LogisticsZonesManager>(); var db=resolver.Resolve<ProtosDb>();
        var terrain=resolver.Resolve<TerrainManager>(); var designations=resolver.Resolve<SurfaceDesignationsManager>();
        runner.GetType().GetProperty("ExtraSimUpdateMs")!.SetValue(runner,Fix32.Zero);
        resolver.Resolve<Mafi.Core.Simulation.SimLoopEvents>().SetSimSpeed(1);
        var passed=new List<string>();
        void Update()=>runner.GetType().GetMethod("Update")!.Invoke(runner,new object[]{100.ToFix32()});
        IInputCommand Execute(IInputCommand original)
        {
            Check(GameCommandCodec.TryEncode(original,out byte[] wire,out string reason),reason);
            var decoded=GameCommandCodec.Decode(wire,resolver);
            Check(GameCommandCodec.TryEncode(decoded,out byte[] rebuilt) && wire.SequenceEqual(rebuilt),"Changed command fields: "+original.GetType().Name);
            scheduler.ScheduleInputCmd(decoded);Update();Update();
            Check(decoded.IsProcessedAndSynced && decoded.ResultSet && !decoded.HasError,"Native command failed: "+decoded.GetType().Name+": "+decoded.ErrorMessage);
            passed.Add(decoded.GetType().Name);Console.WriteLine("PASS native gameplay: "+decoded.GetType().Name);return decoded;
        }
        void RejectDecode(IInputCommand command)
        {
            Check(GameCommandCodec.TryEncode(command,out byte[] wire,out string reason),reason);
            try{GameCommandCodec.Decode(wire,resolver);}
            catch(ProtocolException){return;}
            throw new Exception("Invalid world reference accepted: "+command.GetType().Name);
        }
        var center=entities.GetAllEntitiesOfType<StaticEntity>().First().CenterTile.Xy;
        var origin=new Tile2i((center.X+80)/4*4,(center.Y+80)/4*4); var area=new RectangleTerrainArea2i(origin,new RelTile2i(4,4));
        var surface=db.All<TerrainTileSurfaceProto>().First(p=>p.CanBePlacedByPlayer); resolver.Resolve<UnlockedProtosDb>().Unlock(surface);
        var placing=db.All<SurfaceDesignationProto>().First(p=>p.IsPlacing);
        Execute(new AddSurfaceDesignationsCmd(placing.Id,new ImmutableArray<SurfaceDesignationData>(new[]{new SurfaceDesignationData(origin,0xF000,surface.SlimId)})));
        Check(designations.PlacingDesignationsDict.ContainsKey(origin),"Road designation was not created.");
        Execute(new RemoveSurfaceDesignationsCmd(new ImmutableArray<Tile2i>(new[]{origin}),area));
        Check(!designations.PlacingDesignationsDict.ContainsKey(origin),"Road designation was not removed.");
        var decal=db.All<TerrainTileSurfaceDecalProto>().First(); resolver.Resolve<UnlockedProtosDb>().Unlock(decal);
        Execute(new AddSurfaceDecalCmd(decal.Id,area,Rotation90.Deg90,true,3));
        Execute(new ChangeSurfaceDecalColorCmd(area,5)); Execute(new RemoveSurfaceDecalCmd(area));

        Execute(new CreateNewLogisticsZoneCmd()); var zone=zones.AllZones.AsEnumerable().Last();
        Execute(new RenameLogisticsZoneCmd(zone.Id,"Native codec fixture"));
        Check(zone.Name.Value=="Native codec fixture","Zone name changed in transit.");
        Execute(new SetLogisticsZoneColorCmd(zone.Id,3));
        var polygon=PolygonTerrainArea2i.FromVertices(new ImmutableArray<Vector2i>(new[]{new Vector2i(origin.X,origin.Y),new Vector2i(origin.X+8,origin.Y),new Vector2i(origin.X+8,origin.Y+8),new Vector2i(origin.X,origin.Y+8)}));
        Execute(new ChangeLogisticsZoneAreaCmd(zone.Id,polygon));
        Execute(new ToggleLogisticsZoneConstructionCmd(zone.Id,LogisticsZonesManager.DefaultZoneId));
        var truck=entities.GetAllEntitiesOfType<Vehicle>().First(v=>v.GetType().Name.Contains("Truck"));
        Execute(new SetVehicleLogisticsZoneCmd(zone.Id,truck.Id));
        Check(truck.AssignedZone.HasValue && truck.AssignedZone.Value.Id==zone.Id,"Vehicle did not enter selected zone.");
        Execute(new SetVehicleLogisticsZoneCmd(null,truck.Id)); Check(truck.AssignedZone.IsNone,"Vehicle zone did not clear.");
        var group=db.All<VehicleGroupProto>().First();
        Execute(new TruckJobsForbidCmd(group.Id,zone.Id)); Execute(new TruckJobsAllowCmd(group.Id,zone.Id));
        Execute(new TruckConstructionRuleSetCmd(group.Id,zone.Id,TruckConstructionRule.Forbid));
        Execute(new TruckConstructionRuleSetCmd(group.Id,zone.Id,TruckConstructionRule.None));
        Execute(new RemoveLogisticsZoneCmd(zone.Id)); Check(!zones.TryGetZone(zone.Id,out _),"Zone was not removed.");

        LayoutEntity AddFixture(string family)
        {
            var proto=db.All<LayoutEntityProto>().Where(p=>p.GetType().Name==family && p.IsAvailable && !p.CannotBeBuiltByPlayer && !p.IsAutoBuiltByParent)
                .OrderBy(p=>p.Layout.TilesCount).First(); resolver.Resolve<UnlockedProtosDb>().Unlock(proto);
            var factory=resolver.Resolve<LayoutEntityAddRequestFactory>();
            for(int dx=-128;dx<=128;dx+=4)for(int dy=-128;dy<=128;dy+=4)
            {
                var xy=new Tile2i(center.X+dx,center.Y+dy);
                if(xy.X<terrain.MinOnLimits.X+24 || xy.Y<terrain.MinOnLimits.Y+24 || xy.X>=terrain.MaxOnLimitsExcl.X-24 || xy.Y>=terrain.MaxOnLimitsExcl.Y-24)continue;
                var transform=new TileTransform(new Tile3i(xy,terrain.GetHeight(xy).TilesHeightRounded.Value));
                var request=factory.CreateRequestFor(proto,new EntityAddRequestData(transform)); bool valid;
                try{valid=!entities.CanAdd(request).IsError;}finally{request.ReturnToPool();}
                if(!valid)continue;
                Execute(new CreateStaticEntityCmd(proto.Id,transform));
                return entities.GetAllEntitiesOfType<LayoutEntity>().Single(e=>e.Prototype==proto && e.Transform==transform);
            }
            throw new Exception("No location for fixture "+family);
        }
        var storage=(Storage)AddFixture("UnitStorageProto");
        Execute(new StorageAlertSetThresholdCmd(storage.Id,Percent.FromRaw(25321),false));
        var machine=AddFixture("MachineProto"); Execute(new EntityDisableLogisticsToggleCmd(machine.Id,true,EntityLogisticsMode.Off));
        Execute(new EntityDisableLogisticsToggleCmd(machine.Id,true,EntityLogisticsMode.Auto));
        Execute(new ToggleImportRouteEnforcementCmd(storage)); Execute(new ToggleImportRouteEnforcementCmd(storage));
        var farm=(Farm)AddFixture("FarmProto"); Execute(new FarmSetFertilityTargetCmd(farm.Id,Percent.FromRaw(120000)));
        var forest=(ForestryTower)AddFixture("ForestryTowerProto"); Execute(new ForestryTowerSetCutPercentageCmd(forest.Id,Percent.FromRaw(75000)));
        var depot=(VehicleDepotBase)AddFixture("VehicleDepotProto"); Execute(new SetVehicleDepotTargetZone(depot.Id,null));
        var vehicle=db.All<DrivingEntityProto>().First(p=>depot.Prototype.BuildableEntities.Contains(p));resolver.Resolve<UnlockedProtosDb>().Unlock(vehicle);
        Execute(new AddVehicleToBuildQueueCmd(vehicle.Id,depot.Id,2));
        Execute(new RemoveVehicleFromBuildQueueCmd(depot.BuildQueue.Count-1,depot.Id));
        Execute(new BuildVehicleForeverCmd(vehicle.Id,depot.Id));Execute(new BuildVehicleForeverCmd(null,depot.Id));
        Execute(new CreateNewTrainLineCmd());var lines=resolver.Resolve<TrainLinesManager>();var line=lines.Lines.AsEnumerable().Last();
        Execute(new RenameTrainLineCmd(line.Id,"Fixture train line"));Execute(new SetTrainLineColorApplicationCmd(line.Id,true));
        Execute(new CreateNewTrainNetworkCmd());var networks=resolver.Resolve<TrainNetworksManager>();var network=networks.Networks.Values.Last();
        Execute(new RenameTrainNetworkCmd(network.Id,"Fixture train network"));Execute(new SetTrainNetworkColorApplicationCmd(network.Id,true));
        Execute(new SetNetworkRefuelThresholdCmd(network.Id,Percent.FromRaw(65321)));
        Execute(new RemoveTrainNetworkCmd(network.Id));Check(!networks.Networks.ContainsKey(network.Id),"Train network was not removed.");
        TileTransform FindConstruction(LayoutEntityProto proto)
        {
            var factory=resolver.Resolve<LayoutEntityAddRequestFactory>(); var failures=new Dictionary<string,int>();
            for(int dz=0;dz<=12;dz+=4)for(int dx=-192;dx<=192;dx+=4)for(int dy=-192;dy<=192;dy+=4)
            {
                var xy=new Tile2i(center.X+dx,center.Y+dy);
                if(xy.X<terrain.MinOnLimits.X+40 || xy.Y<terrain.MinOnLimits.Y+40 || xy.X>=terrain.MaxOnLimitsExcl.X-40 || xy.Y>=terrain.MaxOnLimitsExcl.Y-40)continue;
                var transform=new TileTransform(new Tile3i(xy,terrain.GetHeight(xy).TilesHeightRounded.Value+dz));
                var request=factory.CreateRequestFor(proto,new EntityAddRequestData(transform,allowValidationSuppression:proto is BridgeSegmentProto bridgeCandidate && bridgeCandidate.IsEntrance)); bool valid;
                try{var validation=entities.CanAdd(request);valid=!validation.IsError || (proto is BridgeSegmentProto entrance && entrance.IsEntrance && validation.IsSuppressedError);if(!valid){string message=validation.ValidationStatus+":"+validation.ErrorMessageForPlayer.ToString()+":"+validation.ErrorMessage;failures[message]=failures.GetValueOrDefault(message)+1;}}finally{request.ReturnToPool();}
                if(valid)return transform;
            }
            throw new Exception("No valid fixture position for "+proto.Id.Value+" "+JsonSerializer.Serialize(failures.OrderByDescending(k=>k.Value).Take(8)));
        }
        var bridgeProto=db.All<BridgeSegmentProto>().Where(p=>p.IsEntrance && p.IsAvailable).OrderBy(p=>p.Layout.TilesCount).First();
        resolver.Resolve<UnlockedProtosDb>().Unlock(bridgeProto);resolver.Resolve<UnlockedProtosDb>().Unlock(bridgeProto.BridgeProto);
        var bridgeTransform=FindConstruction(bridgeProto);
        var bridgeStep=new BridgePlanStep(bridgeProto,bridgeProto.EndDirection.Transformed(bridgeTransform),bridgeTransform);
        Execute(new CreateBridgeFromPlanCmd(new BridgePlan(new ImmutableArray<BridgePlanStep>(new[]{bridgeStep}))));
        var bridge=entities.GetAllEntitiesOfType<BridgeSegment>().Single(p=>p.Transform==bridgeTransform && p.Prototype==bridgeProto);
        Check(!bridge.IsConstructed,"Bridge was incorrectly granted free construction.");
        Execute(new SetBridgeLaneLayoutCmd(bridge.Id,0,null));

        var railProto=db.All<TrainTrackProto>().Where(p=>p.IsAvailable && !p.CannotBeBuiltByPlayer && !p.IsElevated && p.IsStraight && p.ElectrificationType==ElectrificationType.None)
            .OrderBy(p=>p.Layout.TilesCount).First();
        resolver.Resolve<UnlockedProtosDb>().Unlock(railProto);
        var railTransform=FindConstruction(railProto);
        var railStep=new TrainTrackPlanStep(railProto,railProto.GetTransformedTrajectory(railTransform.Transform90RotFlip).EndDirection,
            TrainTrackTrajectoryDirection.Forward,railTransform,ImmutableArray<TrainTrackPillarInfo>.Empty,ImmutableArray<TrainTrackPoleInfo>.Empty);
        // PolesValid is advisory, not an execution requirement; native commits supported geometry.
        Execute(new CreateTrainTrackFromPlanCmd(new TrainTrackPlan(new ImmutableArray<TrainTrackPlanStep>(new[]{railStep}),true,false,
            options:new TrainTrackPathFinderOptions(buildDirection:TrainTrackTrajectoryDirection.Bidirectional))));
        var rail=entities.GetAllEntitiesOfType<TrainTrack>().Single(p=>p.Transform==railTransform);
        Check(!rail.IsConstructed && rail.Direction==TrainTrackTrajectoryDirection.Bidirectional,"Rail must keep construction costs and plan direction.");
        var stationProto=db.All<TrainStationRootBaseProto>().Where(p=>p.IsAvailable && !p.CannotBeBuiltByPlayer).OrderBy(p=>p.Layout.TilesCount).First();
        resolver.Resolve<UnlockedProtosDb>().Unlock(stationProto);var stationTransform=FindConstruction(stationProto);
        var station=resolver.TryInvokeFactoryHierarchy<StaticEntity>(stationProto,stationTransform).Value;
        Check(!entities.TryAddEntity(station).IsError,"Could not add fixture train station.");station.MakeFullyConstructed();Update();Update();
        Execute(new AddNewScheduleItemToTrainLineCmd(line.Id,station.Id,10));
        Check(line.Schedule.Count==1,"Train schedule item was not added.");
        var schedule=new TrainScheduleItemId(line.Id,0);
        RejectDecode(new RemoveEntityFromScheduleItemCmd(schedule,storage.Id));
        var otherStationTransform=FindConstruction(stationProto);
        var otherStation=resolver.TryInvokeFactoryHierarchy<StaticEntity>(stationProto,otherStationTransform).Value;
        Check(!entities.TryAddEntity(otherStation).IsError,"Could not add unrelated fixture train station.");otherStation.MakeFullyConstructed();Update();Update();
        RejectDecode(new SetTrainSchedulePriority(schedule,10,otherStation.Id));
        Execute(new SetTrainSchedulePriority(schedule,11,station.Id));
        Execute(new SetTrainScheduleSkipIfCannotArriveCmd(schedule,true));
        Execute(new SetTrainScheduleSkipIfNextCanArriveCmd(schedule,true));
        Execute(new SetTrainScheduleSkipIfHighFuelCmd(schedule,Percent.FromRaw(50123)));
        Execute(new ToggleLoadUnloadTrainScheduleItemCmd(schedule,true,true));
        Execute(new ReorderTrainLineScheduleItemCmd(schedule,0));
        Execute(new RemoveTrainLineScheduleItemCmd(schedule));Check(line.Schedule.Count==0,"Schedule item was not removed.");
        Execute(new AddNewScheduleItemToTrainLineCmd(line.Id,station.Id,10));
        // Exercise the actual server admission queue without a remote listener/client or live world.
        // Each proposal is structurally valid before tick one; the duplicate removal becomes stale
        // only after the first has executed and must be rejected instead of reaching RemoveAt.
        var serverType=Assembly.Load("CoiDedicated").GetType("CoiDedicated.GameSessionServer",true)!;
        using(var server=(IDisposable)Activator.CreateInstance(serverType,new object[]{"127.0.0.1",0,"isolated-fixture-secret","fixture-build"})!)
        using(var tcp=new TcpClient())
        {
            var peerType=serverType.GetNestedType("Peer",BindingFlags.NonPublic)!;
            object peer=Activator.CreateInstance(peerType,BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic,null,
                new object[]{tcp,"fixture",1u,CancellationToken.None},null)!;
            try
            {
                peerType.GetField("QueuedCommands")!.SetValue(peer,3);
                var proposalType=serverType.GetNestedType("Proposal",BindingFlags.NonPublic)!;
                object queue=serverType.GetField("proposals",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(server)!;
                void Enqueue(ulong id,IInputCommand cmd)
                {
                    Check(GameCommandCodec.TryEncode(cmd,out byte[] wire,out string error),error);
                    object proposal=Activator.CreateInstance(proposalType,BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic,null,new object[]{peer,id,wire},null)!;
                    queue.GetType().GetMethod("Enqueue")!.Invoke(queue,new[]{proposal});
                }
                Enqueue(1,new RenameTrainLineCmd(line.Id,"Before isolated removal"));
                Enqueue(2,new RemoveEntityFromScheduleItemCmd(schedule,station.Id));
                Enqueue(3,new RemoveTrainLineScheduleItemCmd(schedule));
                IList Admit()=>(IList)serverType.GetMethod("Admit")!.Invoke(server,new object[]{resolver})!;
                Check(Admit().Count==1,"An isolated removal was admitted alongside a preceding command.");
                Check((int)peerType.GetField("QueuedCommands")!.GetValue(peer)! ==2,"Deferred proposals changed the queued count.");Update();
                Check(line.Schedule.Count==1,"A deferred removal executed too early.");
                Check(Admit().Count==1,"First removal should be admitted alone.");
                Check((int)peerType.GetField("QueuedCommands")!.GetValue(peer)! ==1,"First removal did not release exactly one queue slot.");Update();
                Check(line.Schedule.Count==0,"First removal did not execute.");
                Check(Admit().Count==0,"Duplicate removal should be rejected against the updated list.");
                var outbound=(Channel<ProtocolFrame>)peerType.GetField("Outbound")!.GetValue(peer)!;
                Check(outbound.Reader.TryRead(out var rejected) && rejected.Kind==MessageKind.CommandRejected,"Duplicate removal did not produce a rejection frame.");
                Check((int)peerType.GetField("QueuedCommands")!.GetValue(peer)! ==0,"Deferred queue count leaked.");
                passed.Add(nameof(RemoveEntityFromScheduleItemCmd));
                Console.WriteLine("PASS native admission barrier: prior command, last-root removal, and stale item removal validate across separate ticks.");
                Execute(new AddNewScheduleItemToTrainLineCmd(line.Id,station.Id,10));
                Execute(new AddNewScheduleItemToTrainLineCmd(line.Id,otherStation.Id,10));
                peerType.GetField("QueuedCommands")!.SetValue(peer,2);
                Enqueue(4,new ReorderTrainLineScheduleItemCmd(schedule,1));
                Enqueue(5,new SetTrainSchedulePriority(schedule,12,station.Id));
                Check(Admit().Count==1,"Priority change must wait for preceding schedule reordering.");Update();
                Check(Admit().Count==0,"Priority change targeted a station no longer in its schedule item.");
                Check(outbound.Reader.TryRead(out rejected) && rejected.Kind==MessageKind.CommandRejected,"Stale priority root did not produce a rejection.");
                Check((int)peerType.GetField("QueuedCommands")!.GetValue(peer)! ==0,"Priority rejection leaked queue count.");
                Execute(new RemoveTrainLineScheduleItemCmd(schedule));Execute(new RemoveTrainLineScheduleItemCmd(schedule));
                Console.WriteLine("PASS native priority admission: reordered schedule membership is revalidated before priority mutation.");
            }
            finally{peerType.GetMethod("Close")!.Invoke(peer,null);}
        }
        Execute(new RemoveTrainLineCmd(line.Id));Check(!lines.TryGetTrainLine(line.Id,out _),"Train line was not removed.");
        File.WriteAllText(Path.Combine(output,"gameplay-expansion-results.json"),JsonSerializer.Serialize(passed,new JsonSerializerOptions{WriteIndented=true}));
    }
    private static void Check(bool value,string message){if(!value)throw new Exception(message);}
}
