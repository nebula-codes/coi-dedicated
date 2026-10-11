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
using Mafi.Core.Buildings.Storages;
using Mafi.Core.Buildings.SpaceProgram;
using Mafi.Core.Entities.Commands;
using Mafi.Core.Entities.Static.Commands;
using Mafi.Core.Factory.Transports;
using Mafi.Core.Factory.Zippers;
using Mafi.Core.Game;
using Mafi.Core.MessageNotifications;
using Mafi.Core.MessageNotifications.Notifications;
using Mafi.Core.Notifications;
using Mafi.Core.Population;
using Mafi.Core.SpaceProgram;
using Mafi.Core.Terrain.Designation;
using Mafi.Core.Terrain.Props;
using Mafi.Core.Terrain.Trees;

internal static class Program
{
    private static int Main(string[] args)
    {
        string game=Path.GetFullPath(Environment.GetEnvironmentVariable("COI_GAME_DIR")??@"F:\SteamLibrary\steamapps\common\Captain of Industry");
        AssemblyLoadContext.Default.Resolving+=(_,name)=>name.Name is "Mafi" or "Mafi.Core" or "Mafi.Base"?AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.Combine(game,"Captain of Industry_Data","Managed",name.Name+".dll")):null;
        Environment.CurrentDirectory=game;
        if(args.Length!=2)throw new ArgumentException("Usage: SOURCE_SAVE NEW_ISOLATED_OUTPUT_DIR (absolute paths)");
        string source=Path.GetFullPath(args[0]),output=Path.GetFullPath(args[1]);
        if(!File.Exists(source)||Directory.Exists(output))throw new ArgumentException("Existing source and fresh output required.");Directory.CreateDirectory(output);string fixture=Path.Combine(output,"fixture.save");File.Copy(source,fixture);
        return Run(fixture,output,game);
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
        var entities=d.Resolve<EntitiesManager>();var scheduler=d.Resolve<IInputScheduler>();var db=d.Resolve<ProtosDb>();var terrain=d.Resolve<TerrainManager>();var passed=new List<string>();var nativeErrors=new List<string>();var rejected=new List<string>();var untested=new List<string>();
        runner.GetType().GetProperty("ExtraSimUpdateMs")!.SetValue(runner,Fix32.Zero);d.Resolve<Mafi.Core.Simulation.SimLoopEvents>().SetSimSpeed(1);
        void Update()=>runner.GetType().GetMethod("Update")!.Invoke(runner,new object[]{100.ToFix32()});
        IInputCommand Execute(IInputCommand original,bool allowNativeError=false){Check(GameCommandCodec.TryEncode(original,out var bytes,out string reason),reason);var decoded=GameCommandCodec.Decode(bytes,d);Check(GameCommandCodec.TryEncode(decoded,out var rebuilt)&&bytes.SequenceEqual(rebuilt),"Canonical wire changed: "+original.GetType().Name);scheduler.ScheduleInputCmd(decoded);Update();Update();Check(decoded.IsProcessedAndSynced&&decoded.ResultSet,"Native command was not processed: "+decoded.GetType().Name);if(decoded.HasError){Check(allowNativeError,"Native command failed: "+decoded.GetType().Name+": "+decoded.ErrorMessage);nativeErrors.Add(decoded.GetType().Name+": "+decoded.ErrorMessage);Console.WriteLine("EXPECTED native rejection: "+decoded.GetType().Name+": "+decoded.ErrorMessage);}else{passed.Add(decoded.GetType().Name);Console.WriteLine("PASS native: "+decoded.GetType().Name);}return decoded;}
        void RejectWorld(IInputCommand command){Check(GameCommandCodec.TryEncode(command,out var bytes,out string reason),reason);try{GameCommandCodec.Decode(bytes,d);}catch(ProtocolException){rejected.Add(command.GetType().Name);return;}throw new Exception("Invalid world request accepted.");}
        var center=entities.GetAllEntitiesOfType<StaticEntity>().First().CenterTile.Xy;
        TileTransform FindConstruction(LayoutEntityProto proto){var factory=d.Resolve<LayoutEntityAddRequestFactory>();for(int dz=0;dz<=12;dz+=4)for(int dx=-192;dx<=192;dx+=4)for(int dy=-192;dy<=192;dy+=4){var xy=new Tile2i(center.X+dx,center.Y+dy);if(xy.X<terrain.MinOnLimits.X+40||xy.Y<terrain.MinOnLimits.Y+40||xy.X>=terrain.MaxOnLimitsExcl.X-40||xy.Y>=terrain.MaxOnLimitsExcl.Y-40)continue;var transform=new TileTransform(new Tile3i(xy,terrain.GetHeight(xy).TilesHeightRounded.Value+dz));var request=factory.CreateRequestFor(proto,new EntityAddRequestData(transform));bool valid;try{valid=!entities.CanAdd(request).IsError;}finally{request.ReturnToPool();}if(valid)return transform;}throw new Exception("No valid fixture location for "+proto.Id.Value);}
        StaticEntity AddFixture(LayoutEntityProto proto,bool constructed=true){d.Resolve<UnlockedProtosDb>().Unlock(proto);var transform=FindConstruction(proto);var entity=d.TryInvokeFactoryHierarchy<StaticEntity>(proto,transform).Value;Check(!entities.TryAddEntity(entity).IsError,"Fixture placement failed.");if(constructed)entity.MakeFullyConstructed();Update();Update();return entity;}
        var storageProto=db.All<StorageProto>().Where(p=>p.IsAvailable&&!p.CannotBeBuiltByPlayer).OrderBy(p=>p.Layout.TilesCount).First();var storage=(Storage)AddFixture(storageProto);
        var product=db.All<ProductProto>().First(p=>p.IsAvailable&&(!storageProto.ProductType.HasValue||storageProto.ProductType.Value.Matches(p.Type)));Execute(new StorageSetProductCmd(storage,product));storage.AddAsMuchAs(new Quantity(20));
        RejectWorld(new StorageCheatClearProductCmd(storage.Id));RejectWorld(new StorageSetCheatModeCmd(storage.Id,Storage.StorageCheatMode.KeepFull));RejectWorld(new FinishBuildOfStaticEntityCmd(storage.Id,false));
        var selected=new ImmutableArray<EntityId>(new[]{storage.Id});Execute(new ToggleEnabledGroupCmd(selected,true));Check(storage.IsPaused,"Group pause was not applied.");Execute(new ToggleEnabledGroupCmd(selected,false));Check(!storage.IsPaused,"Group resume was not applied.");
        var pinned=d.Resolve<PinnedProductsManager>();var pinnedProduct=pinned.AllPinnedProducts[0];Execute(new PinnedProductReorderCmd(pinnedProduct.Id,pinned.AllPinnedProducts.Count-1));Check(pinned.AllPinnedProducts[pinned.AllPinnedProducts.Count-1]==pinnedProduct,"Pinned product reorder did not persist.");var appendProduct=pinned.AllPinnedProducts[0];Execute(new PinnedProductReorderCmd(appendProduct.Id,pinned.AllPinnedProducts.Count));Check(pinned.AllPinnedProducts[pinned.AllPinnedProducts.Count-1]==appendProduct,"Native append-at-count failed.");
        var messageManager=d.Resolve<IMessageNotificationsManager>();var message=new NewRefugeesMessage(1,ImmutableArray<ProductQuantity>.Empty);messageManager.AddMessage(message);Execute(new MessageNotificationDismissCmd(message.NotificationId));Check(!messageManager.AllNotifications.Any(n=>n.NotificationId==message.NotificationId),"Message dismissal failed.");
        var notifications=d.Resolve<INotificationsManager>();var notificationProto=db.All<NotificationProto>().First(p=>p.Type==NotificationType.OneTimeOnly);var notificationId=notifications.AddNotification(notificationProto,Option<IObjectWithTitle>.None,Option<object>.None);Execute(new NotificationDismissCmd(new ImmutableArray<NotificationId>(new[]{notificationId})));Check(!notifications.FetchAllNotifications().Any(n=>n.NotificationId==notificationId),"One-shot notification was not dismissed.");
        var stackerProto=db.All<StackerTowerProto>().First(p=>p.IsAvailable&&!p.CannotBeBuiltByPlayer);var stacker=(StackerTower)AddFixture(stackerProto);
        Execute(new SetDumpHeightOffsetCmd(stacker.Id,new ThicknessTilesI(0)));Check(stacker.DumpHeightOffset.Value==0,"Dump height failed.");
        var dumpable=db.All<ProductProto>().First(p=>p.IsAvailable&&p.DumpableProduct.HasValue);Execute(new StackerTowerSelectProductForSlopePreviewCmd(stacker.Id,dumpable));Check(stacker.SelectedProductForSlopePreview.HasValue,"Slope preview product lost.");Execute(new StackerTowerSelectProductForSlopePreviewCmd(stacker.Id,Option<ProductProto>.None));
        Execute(new StackerTowerSetDumpRadiusCmd(stacker.Id,0.Tiles(),stackerProto.MaxDumpRadius));Check(stacker.DumpRadiusLeft.Value==0&&stacker.DumpRadiusRight==stackerProto.MaxDumpRadius,"Nullable dump radius not applied.");
        Execute(new StackerTowerSetMaxSegmentsCmd(stacker.Id,2));Check(stacker.MaxSegments==2,"Max segments did not apply.");Execute(new StackerTowerSetMaxSegmentsCmd(stacker.Id,0));
        var zipperProto=db.All<ZipperProto>().First(p=>p.IsAvailable&&!p.CannotBeBuiltByPlayer);var zipper=(Zipper)AddFixture(zipperProto);Execute(new ZipperSetPriorityPortsCmd(zipper.Id,zipper.Ports[0].Name,true));Execute(new ZipperSetPriorityPortsCmd(zipper.Id,zipper.Ports[0].Name,null));
        var trees=d.Resolve<ITreePlantingManager>();var treeProto=db.All<TreeProto>().First(p=>p.IsAvailable);Tile2i? treePos=null;
        for(int dx=-100;dx<=100&&!treePos.HasValue;dx+=4)for(int dy=-100;dy<=100&&!treePos.HasValue;dy+=4){var pos=new Tile2i(center.X+dx,center.Y+dy);if(terrain.IsValidCoord(pos)&&trees.TryAddManualTree(treeProto,pos))treePos=pos;}
        Check(treePos.HasValue,"Could not plant manual fixture tree.");var treeId=new TreeId(new Tile2iSlim((ushort)treePos!.Value.X,(ushort)treePos!.Value.Y));Execute(new RemoveManualPlantTreeCmd(treeId));Check(!trees.EnumerateManualTreesInArea(new RectangleTerrainArea2i(treePos!.Value,new RelTile2i(1,1))).Contains(treeId),"Manual tree was not removed.");
        var props=d.Resolve<TerrainPropsManager>();RectangleTerrainArea2i? propArea=null;
        for(int dx=-200;dx<=200&&!propArea.HasValue;dx+=16)for(int dy=-200;dy<=200&&!propArea.HasValue;dy+=16){var pos=new Tile2i(center.X+dx,center.Y+dy);if(!terrain.IsValidCoord(pos)||!terrain.IsValidCoord(new Tile2i(pos.X+15,pos.Y+15)))continue;var area=new RectangleTerrainArea2i(pos,new RelTile2i(16,16));if(props.EnumeratePropsInArea(area).Any())propArea=area;}
        Check(propArea.HasValue,"No terrain prop fixture.");int beforeProps=props.EnumeratePropsInArea(propArea!.Value).Count();var unity=d.Resolve<UpointsManager>();var beforeUnity=unity.Quantity;Execute(new QuickRemovePropsCmd(propArea!.Value));Check(props.EnumeratePropsInArea(propArea!.Value).Count()<beforeProps&&unity.Quantity<beforeUnity,"Prop quick removal did not remove props and charge Unity.");
        Execute(new SetOrbitCargoLimitCmd(product.Id,new Quantity(1234)));Check(d.Resolve<OrbitManager>().GetOrbitalLimitFor(product).Value==1234,"Orbit cargo limit lost.");
        var cutEntity=(Storage)AddFixture(storageProto,false);Execute(new StorageSetProductCmd(cutEntity,product));
        var cutTransform=cutEntity.Transform;
        var cut=(CutEntitiesForPasteCmd)Execute(new CutEntitiesForPasteCmd(new ImmutableArray<EntityId>(new[]{cutEntity.Id}),ImmutableArray<TransportCutRange>.Empty));
        Check(cut.CutConfigsOut!=null&&cut.CutConfigsOut.Count==1&&cutEntity.IsDestroyed,"Cut lost result configuration or left entity behind.");
        Execute(new BatchCreateStaticEntitiesCmd(cut.CutConfigsOut!.ToImmutableArray(),BuildMiniZippersMode.DeferToProto,isFree:false,allowValidationSuppression:false,applyConfiguration:true));
        var pasted=entities.GetAllEntitiesOfType<Storage>().Single(e=>e.Transform==cutTransform);
        Check(pasted.StoredProduct.ValueOrNull==product,"Native cut output could not be pasted with the original product configuration.");
        // Preflight the whole selection before destroying even its supported prefix.
        var supportedCut=AddFixture(storageProto,false);var unsupportedCut=AddFixture(stackerProto,false);
        RejectWorld(new CutEntitiesForPasteCmd(new ImmutableArray<EntityId>(new[]{supportedCut.Id,unsupportedCut.Id}),ImmutableArray<TransportCutRange>.Empty));
        Check(!supportedCut.IsDestroyed&&!unsupportedCut.IsDestroyed,"Rejected mixed cut destroyed source entities.");
        var incomplete=AddFixture(storageProto,false);var buildUnity=unity.Quantity;Execute(new FinishBuildOfStaticEntityCmd(incomplete.Id,true));Check(unity.Quantity<buildUnity,"Paid build did not charge Unity.");
        var spendBuild=AddFixture(storageProto,false);var spendUnity=unity.Quantity;Execute(new SpendUpointsOnEntitiesCmd(new ImmutableArray<EntityId>(new[]{spendBuild.Id}),null));Check(unity.Quantity<spendUnity,"Area quick build did not charge Unity.");
        var replacementTarget=AddFixture(storageProto,false);var replacementTransform=FindConstruction(storageProto);var replacementData=new EntityConfigData(storageProto,d.Resolve<ConfigSerializationContext>()){CustomTitle="Replicated settings",Transform=replacementTransform};replacementData.SetStorageStoredProduct(product);Execute(new ReplaceEntityCmd(replacementTarget.Id,storageProto.Id,replacementData,true));var replaced=entities.GetAllEntitiesOfType<Storage>().Single(p=>p.Transform==replacementTransform);Check(replaced.GetCustomTitle()=="Replicated settings","Replacement settings were not applied.");
        var transports=d.Resolve<TransportsManager>();var transportProto=db.All<TransportProto>().First(p=>p.IsAvailable&&!p.CannotBeBuiltByPlayer);d.Resolve<UnlockedProtosDb>().Unlock(transportProto);d.Resolve<UnlockedProtosDb>().Unlock(transports.PillarProto);
        (Transport Entity,Tile3i Start,Tile3i End) AddTransportFixture(int minHeight=0){for(int height=minHeight;height<=6;height+=2)for(int dx=-128;dx<=128;dx+=16)for(int dy=-128;dy<=128;dy+=16){var xy=new Tile2i(center.X+dx,center.Y+dy);if(!terrain.IsValidCoord(xy)||!terrain.IsValidCoord(new Tile2i(xy.X+12,xy.Y)))continue;int z=terrain.GetHeight(xy).TilesHeightRounded.Value+height;var a=new Tile3i(xy,z);var b=new Tile3i(xy.X+12,xy.Y,z);var pivots=new ImmutableArray<Tile3i>(new[]{a,b});if(transports.CanBuildOrJoinTransport(transportProto,pivots,new Set<Tile2i>(),null,null,true,out _,out _,out _)&&transports.TryBuildOrJoinTransport(transportProto,pivots,null,null,true,out var transport,out _)&&transport.HasValue){Update();Update();return(transport.Value,a,b);}}throw new Exception("No valid transport fixture.");}
        var t1=AddTransportFixture();Execute(new DeconstructTransportSegmentCmd(t1.Entity.Id,t1.Start,t1.End,false));
        var t2=AddTransportFixture();Execute(new StartDeconstructionOfTransportSubSectionsCmd(t2.Entity.Id,new ImmutableArray<Pair<Tile3i,Tile3i>>(new[]{new Pair<Tile3i,Tile3i>(t2.Start,t2.End)}),EntityRemoveReason.Remove,false));
        var t3=AddTransportFixture();var transportCut=new CutEntitiesForPasteCmd(ImmutableArray<EntityId>.Empty,new ImmutableArray<TransportCutRange>(new[]{new TransportCutRange(t3.Start,t3.End)}));
        Check(!GameCommandCodec.TryEncode(transportCut,out _,out _),"Unsupported transport cut must be rejected before it removes the transport.");
        Check(!t3.Entity.IsDestroyed&&t3.Entity.Trajectory.Pivots.First==t3.Start&&t3.Entity.Trajectory.Pivots.Last==t3.End,"Rejected transport cut changed the source trajectory.");
        var elevated=AddTransportFixture(4);bool pillarTested=false;var supports=elevated.Entity.Trajectory.TilesSupportInfo;
        for(int i=0;i<supports.Length;i++){var support=supports[i];if(support.PillarAttachmentType!=TransportPillarAttachmentType.NoAttachment&&transports.CanBuildPillarAt(support.Position.Xy,support.Position.Height,out _,out _)){Execute(new AddTransportPillarCmd(elevated.Entity.Id,i));Check(transports.Pillars.ContainsKey(support.Position.Xy),"Transport pillar was not created.");pillarTested=true;break;}}if(!pillarTested)untested.Add("AddTransportPillarCmd (native elevated fixture had no unoccupied eligible support)");
        var launchProto=db.All<LayoutEntityProto>().FirstOrDefault(p=>p.IsAvailable&&!p.CannotBeBuiltByPlayer&&p.GetType().Name=="RocketLaunchPadProto");
        if(launchProto!=null){var launch=(RocketLaunchPad)AddFixture(launchProto);launch.AddCargoBuffer(product);var secondProduct=db.All<ProductProto>().First(p=>p.IsAvailable&&p!=product);launch.AddCargoBuffer(secondProduct);var originalBuffer=launch.CargoBuffers[0];Execute(new ReorderLaunchPadCargoBufferCmd(launch.Id,0,launch.CargoBuffers.Count-1));Check(launch.CargoBuffers[launch.CargoBuffers.Count-1]==originalBuffer,"Cargo buffer order lost.");}else untested.Add("ReorderLaunchPadCargoBufferCmd (no available launchpad prototype)");
        var difficulty=d.Resolve<GameDifficultyConfig>();var changed=difficulty.Clone();var repair=difficulty.QuickRepair==GameDifficultyConfig.QuickRepairSetting.Enabled?GameDifficultyConfig.QuickRepairSetting.Disabled:GameDifficultyConfig.QuickRepairSetting.Enabled;GameDifficultyConfig.QuickRepairInfo.SetValue(changed,repair);Execute(new ChangeGameDifficultyCmd(changed));Check(difficulty.QuickRepair==repair,"Difficulty quick repair change did not take effect.");
        // Sandbox is intentionally outside native AllOptions/GenerateDiff: prepare only the disposable fixture.
        GameDifficultyConfig.SandboxInfo.SetValue(difficulty,GameDifficultyConfig.SandboxSetting.Enabled);Check(d.Resolve<SandboxManager>().CanCheat,"Sandbox fixture setup failed.");
        // A supported building type can still carry a setting the paste schema cannot preserve.
        var configuredCut=(Storage)AddFixture(storageProto,false);Execute(new StorageSetCheatModeCmd(configuredCut.Id,Storage.StorageCheatMode.KeepFull));
        RejectWorld(new CutEntitiesForPasteCmd(new ImmutableArray<EntityId>(new[]{configuredCut.Id}),ImmutableArray<TransportCutRange>.Empty));
        Check(!configuredCut.IsDestroyed&&configuredCut.CheatMode==Storage.StorageCheatMode.KeepFull,"Rejected configuration cut destroyed or changed its source.");
        Execute(new StorageCheatClearProductCmd(storage.Id));Check(storage.CurrentQuantity.IsZero,"Sandbox storage clear failed.");Execute(new StorageSetCheatModeCmd(storage.Id,Storage.StorageCheatMode.KeepFull));Check(storage.CheatMode==Storage.StorageCheatMode.KeepFull,"Storage cheat mode did not change.");Execute(new StorageSetCheatModeCmd(storage.Id,Storage.StorageCheatMode.None));
        var freeIncomplete=AddFixture(storageProto,false);Execute(new FinishBuildOfStaticEntityCmd(freeIncomplete.Id,false));
        var designationArea=new RectangleTerrainArea2i(new Tile2i(center.X,center.Y),new RelTile2i(1,1));Execute(new QuickFulfillDesignationsCmd(designationArea,Option<ProductProto>.None),true);
        var asteroids=d.Resolve<AsteroidsManager>();var asteroid=asteroids.CheatNewAsteroidToOrbit(4.Tiles());var bring=Execute(new BringAsteroidToOrbitCmd(asteroid.Id),true);Check(bring.HasError,"Missing crewed-station prerequisite was not rejected.");Check(asteroids.AsteroidsActive.Any(a=>a.Id==asteroid.Id),"Rejected asteroid bring changed active list.");Execute(new DiscardAsteroidCmd(asteroid.Id));Check(!asteroids.AsteroidsActive.Any(a=>a.Id==asteroid.Id),"Asteroid was not discarded.");
        var drop=asteroids.CheatNewAsteroidToOrbit(4.Tiles());var dropAt=new Tile2i(terrain.MinOnLimits.X+80,terrain.MinOnLimits.Y+80);Execute(new DropAsteroidAtCmd(drop.Id,dropAt));Check(asteroids.AsteroidsDropped.Any(a=>a.Id==drop.Id)&&asteroids.AsteroidBeingDropped.HasValue&&asteroids.DroppedAsteroidPosition==dropAt,"Asteroid drop did not enter native processing state.");
        untested.Add("RecoverOceanAccessCmd");untested.Add("BringAsteroidToOrbitCmd success path (missing crewed high-tier space station; native rejection tested)");
        File.WriteAllText(Path.Combine(output,"results.json"),JsonSerializer.Serialize(new{Passed=passed,Distinct=passed.Distinct().Count(),NativeRejections=nativeErrors,DecodeRejections=rejected,Untested=untested},new JsonSerializerOptions{WriteIndented=true}));
    }
    private static void Check(bool value,string message){if(!value)throw new Exception(message);}
}
