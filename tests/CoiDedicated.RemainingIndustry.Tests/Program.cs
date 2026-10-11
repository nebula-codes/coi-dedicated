using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using System.Text.Json;
using CoiDedicated.GameAdapter;
using CoiDedicated.Protocol;
using Mafi;
using Mafi.Base.Prototypes.Buildings;
using Mafi.Base.Prototypes.Trains;
using Mafi.Collections.ImmutableCollections;
using Mafi.Core;
using Mafi.Core.Buildings.Cargo;
using Mafi.Core.Buildings.Cargo.Modules;
using Mafi.Core.Buildings.Cargo.Ships;
using Mafi.Core.Buildings.Farms;
using Mafi.Core.Buildings.Mine;
using Mafi.Core.Buildings.Offices;
using Mafi.Core.Buildings.Shipyard;
using Mafi.Core.Buildings.VehicleDepots;
using Mafi.Core.Entities;
using Mafi.Core.Entities.Dynamic;
using Mafi.Core.Entities.Static;
using Mafi.Core.Entities.Static.Commands;
using Mafi.Core.Entities.Static.Layout;
using Mafi.Core.Factory.Datacenters;
using Mafi.Core.Factory.ElectricPower;
using Mafi.Core.Factory.NuclearReactors;
using Mafi.Core.Fleet;
using Mafi.Core.Input;
using Mafi.Core.Products;
using Mafi.Core.Prototypes;
using Mafi.Core.Simulation;
using Mafi.Core.Terrain;
using Mafi.Core.Terrain.Designation;
using Mafi.Core.Terrain.Surfaces;
using Mafi.Core.Vehicles;
using Mafi.Core.Vehicles.Commands;
using Mafi.Core.World;
using Mafi.Core.World.Entities;
using Mafi.Core.World.Loans;

internal static class Program
{
    private static int Main(string[] args)
    {
        string game=Environment.GetEnvironmentVariable("COI_GAME_DIR")??@"F:\SteamLibrary\steamapps\common\Captain of Industry";
        AssemblyLoadContext.Default.Resolving+=(_,name)=>name.Name is "Mafi" or "Mafi.Core" or "Mafi.Base"
            ?AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.Combine(game,"Captain of Industry_Data","Managed",name.Name+".dll")):null;
        if(args.Length==0){Environment.CurrentDirectory=game;return (int)Assembly.GetExecutingAssembly().GetType("Tests")!.GetMethod("CodecTests",BindingFlags.Static|BindingFlags.NonPublic)!.Invoke(null,null)!;}
        string source=Path.GetFullPath(args[0]),output=Path.GetFullPath(args[1]);
        if(Directory.Exists(output))throw new ArgumentException("Use a new isolated output directory.");
        Directory.CreateDirectory(output);string fixture=Path.Combine(output,"fixture.save");File.Copy(source,fixture);
        Environment.CurrentDirectory=game;
        return (int)Assembly.GetExecutingAssembly().GetType("Tests")!.GetMethod("NativeTests",BindingFlags.Static|BindingFlags.NonPublic)!.Invoke(null,new object[]{fixture,output,game})!;
    }
}
internal static class Tests
{
    private static void Check(bool ok,string message){if(!ok)throw new Exception(message);}
    private static void Reject(Action test){try{test();}catch(ProtocolException){return;}throw new Exception("Malformed command accepted.");}
    // Test-only field fixtures cover command classes whose public constructors require a live entity.
    // Production has no reflection or object deserialization and uses the normal public constructors.
    private static T Fields<T>(params (string Name,object? Value)[] fields) where T:IInputCommand
    {
        var value=(T)RuntimeHelpers.GetUninitializedObject(typeof(T));
        foreach(var field in fields)typeof(T).GetField(field.Name)!.SetValue(value,field.Value);return value;
    }
    [MethodImpl(MethodImplOptions.NoInlining)]
    internal static int CodecTests()
    {
        var empty=new DependencyResolverBuilder().BuildAndClear(); var id=new EntityId(7);var product=new ProductProto.ID("Diesel");var proto=new Proto.ID("Fixture");
        IInputCommand[] commands={
            new SetBillboardColorCmd(id,new ColorRgba(0x01234567u),new ColorRgba(0x89abcdefu)),
            new TrainStationFuelSetFuelCmd(id,product),
            Fields<CargoDepotAssignContractCmd>(("CargoDepotId",id),("ContractId",proto)),
            Fields<CargoShipDepartNowCmd>(("CargoShipId",id)),
            Fields<CargoShipPayWithUnityIfOutOfFuelCmd>(("CargoShipId",id),("PayWithUnity",true)),
            Fields<CargoShipSetFuelSaverCmd>(("CargoShipId",id),("IsFuelSaverEnabled",false)),
            new AnimalFarmSetSlaughterLimitCmd(id,7),new MoveAnimalsCmd(id,-4),
            Fields<AddProductToNotifyIfCannotDumpCmd>(("MineTowerId",id),("ProductId",product)),
            Fields<RemoveProductToNotifyIfCannotDumpCmd>(("MineTowerId",id),("ProductId",product)),
            new SetOfficeComputingBoostStepCmd(id,1),new SetOfficeFocusStepCmd(proto,-1),
            new ShipyardWorldEntityConstructionToggle(id,new EntityId(8)),new DataCenterToggleRackCmd(id,proto,1),
            new SetElectricityGenerationPriorityCmd(id,14),new NuclearReactorSetEnrichmentStepCmd(id,1),new NuclearReactorSetPowerLevelCmd(id,1),
            new AddVehicleReplacementTaskCmd(new DynamicEntityProto.ID("Truck1"),new DynamicEntityProto.ID("Truck2"),LogisticsZonesManager.DefaultZoneId,id,false,new EntityId(9),25),
            new ReplaceVehicleCmd(id,new DynamicEntityProto.ID("Truck2")),new RemoveVehicleReplacementTaskCmd(3),
            new SetShiftsCountForMineCmd(id,1),
            new FleetModificationsPrepareCmd(new FleetEntityModificationRequest(new ImmutableArray<SlotModification>(new[]{new SlotModification(proto,new FleetEntityPartProto.ID("Part"))}),new FleetEntityHullProto.ID("Hull"))),
            new GoToLocationCmd(new WorldMapLocId(2),LocationVisitReason.LoadCargo),new AcceptLoanCmd(product,new Quantity(500),10),
            new MakeLoanOverpaymentCmd(product,2,new Quantity(5)),new SetLoanBufferPriorityCmd(2,3),
            new WorldMapEntityCancelRepairCmd(id),new WorldMapEntityStartRepairCmd(id),new WorldMapEntityUpgradeCmd(id),new WorldMapSettlementAdoptPopsCmd(id,5)
        };
        int malformed=0;
        for(int i=0;i<commands.Length;i++)
        {
            Check(GameCommandCodec.TryEncode(commands[i],out var wire,out var reason),reason);
            Check(BitConverter.ToUInt16(wire,2)==300+i,"Wire ID mismatch: "+commands[i].GetType().Name);
            for(int length=0;length<wire.Length;length++){var truncated=wire.AsMemory(0,length);Reject(()=>GameCommandCodec.Decode(truncated,empty));malformed++;}
            Reject(()=>GameCommandCodec.Decode(wire.Concat(new byte[]{0}).ToArray(),empty));malformed++;
        }
        Check(GameCommandCodec.TryEncode(commands[0],out var colors),"Color fixture failed.");
        Check(BitConverter.ToUInt32(colors,8)==0x01234567u && BitConverter.ToUInt32(colors,12)==0x89abcdefu,"RGBA changed in transit.");
        IInputCommand[] invalid={new AnimalFarmSetSlaughterLimitCmd(id,11),new MoveAnimalsCmd(id,int.MinValue),new SetOfficeComputingBoostStepCmd(id,-1),
            new DataCenterToggleRackCmd(id,proto,int.MaxValue),new SetElectricityGenerationPriorityCmd(id,15),new NuclearReactorSetEnrichmentStepCmd(id,-1),
            new NuclearReactorSetPowerLevelCmd(id,-1),new GoToLocationCmd(new WorldMapLocId(2),(LocationVisitReason)3),
            new AcceptLoanCmd(product,new Quantity(500),6),new AcceptLoanCmd(product,new Quantity(0),10),new MakeLoanOverpaymentCmd(product,2,new Quantity(0)),
            new SetLoanBufferPriorityCmd(2,-1),new WorldMapSettlementAdoptPopsCmd(id,-1),
            new FleetModificationsPrepareCmd(new FleetEntityModificationRequest(new ImmutableArray<SlotModification>(new[]{new SlotModification(proto,null),new SlotModification(proto,null)}),null)),
            new FleetModificationsPrepareCmd(new FleetEntityModificationRequest(new ImmutableArray<SlotModification>(Enumerable.Range(0,65).Select(i=>new SlotModification(new Proto.ID("S"+i),null)).ToArray()),null))};
        foreach(var value in invalid)Check(!GameCommandCodec.TryEncode(value,out _,out _),"Invalid input accepted: "+value.GetType().Name);
        using(var writer=new PayloadWriter(GameCommandCodec.MaxCommandBytes)){writer.WriteUInt16(GameCommandCodec.Version);writer.WriteUInt16(321);writer.WriteBoolean(false);writer.WriteInt32(int.MaxValue);Reject(()=>GameCommandCodec.Decode(writer.ToArray(),empty));}
        Console.WriteLine($"PASS industry schemas: {commands.Length} command fixtures, {malformed} truncated/trailing payloads, {invalid.Length} invalid command values, fleet allocation bound.");SurfaceBatchTests.Run();return 0;
    }
    [MethodImpl(MethodImplOptions.NoInlining)]
    internal static int NativeTests(string fixture,string output,string game)
    {
        CodecTests();var host=Assembly.Load("CoiDedicated");
        var options=host.GetType("CoiDedicated.HostOptions",true)!.GetMethod("Parse")!.Invoke(null,new object[]{new[]{"--game-dir",game,"--load",fixture,"--data-dir",output,"--steps","0"}})!;
        Action<DependencyResolver,object> callback=(d,runner)=>Native(d,runner,output);
        return (int)host.GetType("CoiDedicated.GameHost",true)!.GetMethod("Run")!.Invoke(null,new[]{options,callback,(object)CancellationToken.None})!;
    }
    private static void Native(DependencyResolver d,object runner,string output)
    {
        var entities=d.Resolve<EntitiesManager>();var scheduler=d.Resolve<IInputScheduler>();var db=d.Resolve<ProtosDb>();var terrain=d.Resolve<TerrainManager>();
        runner.GetType().GetProperty("ExtraSimUpdateMs")!.SetValue(runner,Fix32.Zero);d.Resolve<Mafi.Core.Simulation.SimLoopEvents>().SetSimSpeed(1);
        var outcomes=new List<object>();int rejected=0;
        void Update()=>runner.GetType().GetMethod("Update")!.Invoke(runner,new object[]{100.ToFix32()});
        IInputCommand Execute(IInputCommand command,bool allowNativeError=false)
        {
            Check(GameCommandCodec.TryEncode(command,out var wire,out var reason),reason);var decoded=GameCommandCodec.Decode(wire,d);
            Check(GameCommandCodec.TryEncode(decoded,out var repeated) && wire.SequenceEqual(repeated),"Roundtrip changed native fields.");
            scheduler.ScheduleInputCmd(decoded);Update();Update();
            Check(decoded.IsProcessedAndSynced && decoded.ResultSet,"Native processor did not finish: "+decoded.GetType().Name);
            Check(allowNativeError || !decoded.HasError,"Native error: "+decoded.GetType().Name+": "+decoded.ErrorMessage);
            outcomes.Add(new{type=decoded.GetType().FullName,hasError=decoded.HasError,error=decoded.HasError?decoded.ErrorMessage.ToString():null});
            Console.WriteLine("PASS native "+decoded.GetType().Name+(decoded.HasError?" (normal game rejection)":""));return decoded;
        }
        void RejectCommand(IInputCommand command){Check(GameCommandCodec.TryEncode(command,out var wire,out var reason),reason);Reject(()=>GameCommandCodec.Decode(wire,d));rejected++;}
        var center=entities.GetAllEntitiesOfType<StaticEntity>().First().CenterTile.Xy;
        var surface=db.All<TerrainTileSurfaceProto>().First(p=>p.CanBePlacedByPlayer);d.Resolve<UnlockedProtosDb>().Unlock(surface);
        var decal=db.All<TerrainTileSurfaceDecalProto>().First();d.Resolve<UnlockedProtosDb>().Unlock(decal);
        var origin=new Tile2i((center.X+80)/4*4,(center.Y+80)/4*4);
        var placing=db.All<SurfaceDesignationProto>().First(p=>p.IsPlacing);
        var surfaceData=new ImmutableArray<TileSurfaceCopyPasteData>(new[]{new TileSurfaceCopyPasteData(new TileSurfaceData(surface.SlimId.Value,0),origin,4,4)});
        Execute(new PasteSurfaceDesignationsCmd(placing.Id,surfaceData));
        var designationManager=d.Resolve<SurfaceDesignationsManager>();Check(designationManager.PlacingDesignationsDict.ContainsKey(origin),"Pasted road designation was not created.");
        var decalData=new ImmutableArray<TileSurfaceCopyPasteData>(new[]{new TileSurfaceCopyPasteData(TileSurfaceData.CreateWithDecalOnly(decal.SlimId,true,Rotation90.Deg90,5),origin,1,1)});
        Execute(new BatchAddSurfaceDecalCmd(decalData));Check(terrain.GetTileSurface(terrain.ExtendTileIndex(origin).Index).DecalSlimId==decal.SlimId,"Batch decal was not added.");
        Execute(new BatchRemoveSurfaceDecalCmd(decalData));Check(terrain.GetTileSurface(terrain.ExtendTileIndex(origin).Index).DecalSlimId.Value==0,"Batch decal was not removed.");
        var removalData=new ImmutableArray<TileSurfaceCopyPasteData>(Enumerable.Range(0,16).Select(i=>new TileSurfaceCopyPasteData(new TileSurfaceData(surface.SlimId.Value,0),new Tile2i(origin.X+i%4,origin.Y+i/4),1,1)).ToArray());
        var removed=(BatchRemoveSurfacePlacingDesignationsCmd)Execute(new BatchRemoveSurfacePlacingDesignationsCmd(removalData));
        Check(!designationManager.PlacingDesignationsDict.ContainsKey(origin),"Batch road designation was not removed.");
        LayoutEntity AddFixture(string family,Func<LayoutEntityProto,bool>? filter=null)
        {
            var proto=db.All<LayoutEntityProto>().Where(p=>p.GetType().Name==family && p.IsAvailable && !p.CannotBeBuiltByPlayer && !p.IsAutoBuiltByParent && (filter==null || filter(p))).OrderBy(p=>p.Layout.TilesCount).First();
            d.Resolve<UnlockedProtosDb>().Unlock(proto);var factory=d.Resolve<LayoutEntityAddRequestFactory>();
            int orientations=family=="CargoDepotProto"?4:1,maxHeightOffset=family=="CargoDepotProto"?3:0;
            for(int rotation=0;rotation<orientations;rotation++)for(int dz=0;dz<=maxHeightOffset;dz++)for(int dx=-160;dx<=160;dx+=4)for(int dy=-160;dy<=160;dy+=4)
            {
                var xy=new Tile2i(center.X+dx,center.Y+dy);if(xy.X<terrain.MinOnLimits.X+24 || xy.Y<terrain.MinOnLimits.Y+24 || xy.X>=terrain.MaxOnLimitsExcl.X-24 || xy.Y>=terrain.MaxOnLimitsExcl.Y-24)continue;
                var transform=new TileTransform(new Tile3i(xy,terrain.GetHeight(xy).TilesHeightRounded.Value+dz),new Rotation90(rotation),false);var req=factory.CreateRequestFor(proto,new EntityAddRequestData(transform));bool valid;
                try{valid=!entities.CanAdd(req).IsError;}finally{req.ReturnToPool();}if(!valid)continue;
                Execute(new CreateStaticEntityCmd(proto.Id,transform));return entities.GetAllEntitiesOfType<LayoutEntity>().Single(e=>e.Prototype==proto && e.Transform==transform);
            }
            throw new Exception("No fixture position for "+family);
        }
        var billboard=(Billboard)AddFixture("BillboardProto");Execute(new SetBillboardColorCmd(billboard.Id,new ColorRgba(0x123456ff),new ColorRgba(0xabcdef7f)));
        var fuelStation=(TrainStationFuel)AddFixture("TrainStationFuelProto");
        Execute(new TrainStationFuelSetFuelCmd(fuelStation.Id,fuelStation.Prototype.AllowedFuels.First().PrimaryProduct.Product.Id));
        Execute(new TrainStationFuelSetFuelCmd(fuelStation.Id,null));
        RejectCommand(new TrainStationFuelSetFuelCmd(fuelStation.Id,new ProductProto.ID("UnknownFixtureFuel")));
        var animals=(AnimalFarm)AddFixture("AnimalFarmProto");Execute(new AnimalFarmSetSlaughterLimitCmd(animals.Id,7));Check(animals.SlaughterStep==7,"Slaughter limit not applied.");
        Execute(new AnimalFarmSetSlaughterLimitCmd(animals.Id,null));Execute(new MoveAnimalsCmd(animals.Id,-1));
        var mine=(MineTower)AddFixture("MineTowerProto");var product=db.All<LooseProductProto>().First();Execute(new AddProductToNotifyIfCannotDumpCmd(mine,product));Execute(new RemoveProductToNotifyIfCannotDumpCmd(mine,product));
        var office=(OfficeBuilding)AddFixture("OfficeBuildingProto");Execute(new SetOfficeComputingBoostStepCmd(office.Id,0));
        var focus=d.Resolve<OfficeBuildingsManager>().AllFocuses[0];Execute(new SetOfficeFocusStepCmd(focus.Prototype.Id,100));Execute(new SetOfficeFocusStepCmd(focus.Prototype.Id,-100));
        var datacenter=(DataCenter)AddFixture("DataCenterProto");var rack=datacenter.ServersCounts.Keys.First();Execute(new DataCenterToggleRackCmd(datacenter,rack,100),true);
        Check(datacenter.RacksCount==0,"Unbuilt datacenter accepted rack construction without normal costs.");
        var reactor=(NuclearReactor)AddFixture("NuclearReactorProto",p=>((NuclearReactorProto)p).Enrichment.HasValue);
        Execute(new NuclearReactorSetEnrichmentStepCmd(reactor.Id,0));Execute(new NuclearReactorSetPowerLevelCmd(reactor.Id,0));
        var generator=entities.GetAllEntitiesOfType<IElectricityGeneratingEntity>().FirstOrDefault();
        if(generator!=null)Execute(new SetElectricityGenerationPriorityCmd(generator,5));
        var truck=entities.GetAllEntitiesOfType<Vehicle>().First(v=>v.GetType().Name.Contains("Truck"));
        var replacement=db.All<DrivingEntityProto>().First(p=>p.Id!=truck.Prototype.Id && p.IsAvailable && p.GetType()==truck.Prototype.GetType());d.Resolve<UnlockedProtosDb>().Unlock(replacement);
        var replacer=d.Resolve<VehiclesReplacer>();Execute(new AddVehicleReplacementTaskCmd(truck.Prototype.Id,replacement.Id,LogisticsZonesManager.DefaultZoneId,null,true,null,1));
        var task=replacer.ActiveReplacementTasks.AsEnumerable().Concat(replacer.FinishedReplacementTasks.AsEnumerable()).Last();Execute(new RemoveVehicleReplacementTaskCmd(task.TaskId));
        Execute(new ReplaceVehicleCmd(truck.Id,replacement.Id),true);
        // Native factory-equivalent creation of an unassigned ship in this isolated fixture.
        var cargoShip=d.Instantiate<CargoShipV2>(d.Resolve<EntityId.Factory>().GetNextId(),db.All<CargoShipProto>().First());entities.AddEntityNoChecks(cargoShip);
        Execute(new CargoShipPayWithUnityIfOutOfFuelCmd(cargoShip,true));Check(cargoShip.CanPayWithUnityIfOutOfFuel,"Cargo ship unity option did not apply.");
        Execute(new CargoShipSetFuelSaverCmd(cargoShip,true));Check(cargoShip.IsFuelReductionEnabled,"Cargo ship fuel-saving option did not apply.");
        Execute(new CargoShipDepartNowCmd(cargoShip));
        try
        {
            var cargoDepot=(CargoDepot)AddFixture("CargoDepotProto");Execute(new CargoDepotAssignContractCmd(cargoDepot,Option<Mafi.Core.World.Contracts.ContractProto>.None));
            var contract=db.All<Mafi.Core.World.Contracts.ContractProto>().First();Execute(new CargoDepotAssignContractCmd(cargoDepot,Option.Create(contract)),true);
        }
        catch(Exception error)when(error.Message=="No fixture position for CargoDepotProto") { Console.WriteLine("SKIP native cargo-contract fixture: no valid coastal placement in bounded search."); }
        RejectCommand(new SetOfficeComputingBoostStepCmd(mine.Id,0));RejectCommand(new NuclearReactorSetPowerLevelCmd(reactor.Id,reactor.MaxPowerLevel+1));
        RejectCommand(new SetOfficeComputingBoostStepCmd(office.Id,office.Prototype.MaxComputingBoostSteps+1));RejectCommand(new TrainStationFuelSetFuelCmd(mine.Id,null));
        RejectCommand(new RemoveVehicleReplacementTaskCmd(uint.MaxValue));RejectCommand(new SetLoanBufferPriorityCmd(uint.MaxValue,3));
        RejectCommand(new GoToLocationCmd(new WorldMapLocId(int.MaxValue),LocationVisitReason.General));
        var world=d.Resolve<WorldMapManager>();
        // Materialize one real generated mine through the game's normal world-entity factory.
        var mineLocation=world.Map.Locations.FirstOrDefault(l=>l.Entity.IsNone && l.EntityProto.ValueOrNull is WorldMapMineProto);
        if(mineLocation!=null)typeof(WorldMapManager).GetMethod("CreateEntityForLocation",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(world,new object[]{mineLocation.EntityProto.Value,mineLocation});
        var worldEntities=entities.GetAllEntitiesOfType<WorldMapEntity>().ToArray();
        Console.WriteLine("World fixture entities: "+string.Join(",",worldEntities.Select(e=>e.GetType().Name).Distinct()));
        var worldMine=worldEntities.OfType<WorldMapMine>().FirstOrDefault();if(worldMine!=null){Execute(new SetShiftsCountForMineCmd(worldMine.Id,0));RejectCommand(new SetShiftsCountForMineCmd(worldMine.Id,worldMine.MaxProductionSteps+1));}
        var repairable=worldEntities.OfType<WorldMapRepairableEntity>().FirstOrDefault();if(repairable!=null){Execute(new WorldMapEntityCancelRepairCmd(repairable.Id));Execute(new WorldMapEntityStartRepairCmd(repairable.Id),true);Execute(new WorldMapEntityCancelRepairCmd(repairable.Id));}
        var upgradable=worldEntities.OfType<IUpgradableWorldEntity>().FirstOrDefault();if(upgradable!=null)Execute(new WorldMapEntityUpgradeCmd(upgradable.Id),true);
        var village=worldEntities.OfType<WorldMapVillage>().FirstOrDefault(v=>v.PopsAvailable>0);
        if(village!=null)Execute(new WorldMapSettlementAdoptPopsCmd(village.Id,1));
        var loans=d.Resolve<LoansManager>();var loanProduct=loans.GetAllProductsToBorrow().First();
        int countBefore=loans.ActiveLoans.Count;bool canBorrow=loans.CanBorrow(loanProduct,new Quantity(100),10,out _,out _);
        Execute(new AcceptLoanCmd(loanProduct.Id,new Quantity(100),10));Check(loans.ActiveLoans.Count==countBefore+(canBorrow?1:0),"Loan eligibility was bypassed.");
        // Seed a normal ActiveLoan record solely in the copied fixture, to exercise repayment even
        // when this early-game island lacks the trading dock required to originate a new loan.
        var seededLoan=new ActiveLoan(1000001,loans,loanProduct,new Quantity(100),d.Resolve<ICalendar>().CurrentDate,10,Percent.Zero,Percent.Hundred,d.Resolve<Mafi.Core.Notifications.INotificationsManager>());
        ((Mafi.Collections.Lyst<ActiveLoan>)typeof(LoansManager).GetField("m_activeLoans",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(loans)!).Add(seededLoan);
        Execute(new SetLoanBufferPriorityCmd(seededLoan.Id,7));Check(seededLoan.BufferPriority==7,"Loan priority did not apply.");
        Execute(new MakeLoanOverpaymentCmd(loanProduct.Id,seededLoan.Id,new Quantity(1)));Check(seededLoan.LeftToPay.Value>=99 && seededLoan.LeftToPay.Value<=100,"Repayment changed debt by more than requested.");
        RejectCommand(new MakeLoanOverpaymentCmd(product.Id,seededLoan.Id,new Quantity(1)));
        var shipyard=entities.GetAllEntitiesOfType<Shipyard>().FirstOrDefault();if(shipyard!=null && repairable!=null)Execute(new ShipyardWorldEntityConstructionToggle(shipyard.Id,repairable.Id));
        var fleet=d.Resolve<TravelingFleetManager>();
        if(fleet.HasFleet)
        {
            Execute(new GoToLocationCmd(world.Map.HomeLocation.Id,LocationVisitReason.General),true);
            RejectCommand(new FleetModificationsPrepareCmd(new FleetEntityModificationRequest(new ImmutableArray<SlotModification>(new[]{new SlotModification(new Proto.ID("MissingFixtureSlot"),null)}),null)));
            foreach(var slot in fleet.TravelingFleet.FleetEntity.Slots)
            {
                var part=slot.Proto.EligibleItems.FirstOrDefault(p=>slot.ExistingPart.IsNone || p!=slot.ExistingPart.Value);if(part==null)continue;
                d.Resolve<UnlockedProtosDb>().Unlock(part);
                var request=new FleetEntityModificationRequest(new ImmutableArray<SlotModification>(new[]{new SlotModification(slot.Proto.Id,part.Id)}),null);
                if(!request.GetPriceForModifications(fleet.TravelingFleet.FleetEntity,db,out var price) || price.GetQuantitySum().IsNotPositive)continue;
                Execute(new FleetModificationsPrepareCmd(request),true);break;
            }
        }
        File.WriteAllText(Path.Combine(output,"results.json"),JsonSerializer.Serialize(new{outcomes,rejected},new JsonSerializerOptions{WriteIndented=true}));
        Console.WriteLine($"PASS native industry fixture: {outcomes.Count} processor invocations, {rejected} invalid world references/bounds rejected.");
    }
}
