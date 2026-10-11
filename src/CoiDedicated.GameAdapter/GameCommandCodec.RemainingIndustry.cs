using System;
using System.Collections.Generic;
using System.Linq;
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
using Mafi.Core.Factory.Datacenters;
using Mafi.Core.Factory.ElectricPower;
using Mafi.Core.Factory.NuclearReactors;
using Mafi.Core.Fleet;
using Mafi.Core.Input;
using Mafi.Core.Products;
using Mafi.Core.Prototypes;
using Mafi.Core.Vehicles;
using Mafi.Core.Vehicles.Commands;
using Mafi.Core.World;
using Mafi.Core.World.Contracts;
using Mafi.Core.World.Entities;
using Mafi.Core.World.Loans;

namespace CoiDedicated.GameAdapter
{
    public static partial class GameCommandCodec
    {
        // Only native command fields are transmitted. Costs, capacity, ownership and availability
        // are still handled by the game's normal processors; world references are checked here.
        private static readonly ScalarCommandSpec[] RemainingIndustryCommands = {
            Scalar<SetBillboardColorCmd>(300,(v,w)=>{
                WriteDetailEntity(w,v.EntityId); w.WriteUInt32(v.PrimaryColor.Rgba); w.WriteUInt32(v.SecondaryColor.Rgba);
            },(r,d)=>{
                var id=ReadEntityId(r); var a=new ColorRgba(r.ReadUInt32()); var b=new ColorRgba(r.ReadUInt32()); r.EnsureEnd();
                RequireEntity<Billboard>(d,id); return new SetBillboardColorCmd(id,a,b);
            }),
            Scalar<TrainStationFuelSetFuelCmd>(301,(v,w)=>{
                WriteDetailEntity(w,v.ModuleId); WriteOptionalProto(w,v.PrimaryFuelId?.Value);
            },(r,d)=>{
                var id=ReadEntityId(r); string? fuel=ReadOptionalProto(r); r.EnsureEnd(); var station=RequireEntity<TrainStationFuel>(d,id);
                if(fuel!=null && !station.Prototype.AllowedFuels.Any(x=>x.PrimaryProduct.Product.Id.Value==fuel))
                    throw new ProtocolException("Fuel is not supported by this train station.");
                return new TrainStationFuelSetFuelCmd(id,fuel==null?(ProductProto.ID?)null:new ProductProto.ID(fuel));
            }),
            Scalar<CargoDepotAssignContractCmd>(302,(v,w)=>{
                WriteDetailEntity(w,v.CargoDepotId); WriteOptionalProto(w,v.ContractId?.Value);
            },(r,d)=>{
                var id=ReadEntityId(r); string? contract=ReadOptionalProto(r); r.EnsureEnd();
                var depot=RequireEntity<CargoDepot>(d,id);
                return new CargoDepotAssignContractCmd(depot,contract==null?Option<ContractProto>.None:Option.Create(RequireResearchTradeProto<ContractProto>(d,contract)));
            }),
            Scalar<CargoShipDepartNowCmd>(303,(v,w)=>WriteDetailEntity(w,v.CargoShipId),(r,d)=>{
                var id=ReadEntityId(r); r.EnsureEnd(); return new CargoShipDepartNowCmd(RequireEntity<CargoShipV2>(d,id));
            }),
            Scalar<CargoShipPayWithUnityIfOutOfFuelCmd>(304,(v,w)=>{
                WriteDetailEntity(w,v.CargoShipId); w.WriteBoolean(v.PayWithUnity);
            },(r,d)=>{
                var id=ReadEntityId(r); bool enabled=r.ReadBoolean(); r.EnsureEnd(); return new CargoShipPayWithUnityIfOutOfFuelCmd(RequireEntity<CargoShipV2>(d,id),enabled);
            }),
            Scalar<CargoShipSetFuelSaverCmd>(305,(v,w)=>{
                WriteDetailEntity(w,v.CargoShipId); w.WriteBoolean(v.IsFuelSaverEnabled);
            },(r,d)=>{
                var id=ReadEntityId(r); bool enabled=r.ReadBoolean(); r.EnsureEnd(); return new CargoShipSetFuelSaverCmd(RequireEntity<CargoShipV2>(d,id),enabled);
            }),
            Scalar<AnimalFarmSetSlaughterLimitCmd>(306,(v,w)=>{
                WriteDetailEntity(w,v.AnimalFarmId); w.WriteBoolean(v.SlaughterSliderStep.HasValue);
                if(v.SlaughterSliderStep.HasValue)WriteBounded(w,v.SlaughterSliderStep.Value,0,10,"Slaughter step");
            },(r,d)=>{
                var id=ReadEntityId(r); int? step=r.ReadBoolean()?ReadBounded(r,0,10,"Slaughter step"):(int?)null; r.EnsureEnd();
                RequireEntity<AnimalFarm>(d,id); return new AnimalFarmSetSlaughterLimitCmd(id,step);
            }),
            Scalar<MoveAnimalsCmd>(307,(v,w)=>{
                WriteDetailEntity(w,v.AnimalFarmId); WriteBounded(w,v.NumberToMove,-1000000,1000000,"Animals to move");
            },(r,d)=>{
                var id=ReadEntityId(r); int count=ReadBounded(r,-1000000,1000000,"Animals to move"); r.EnsureEnd();
                RequireEntity<AnimalFarm>(d,id); return new MoveAnimalsCmd(id,count);
            }),
            Scalar<AddProductToNotifyIfCannotDumpCmd>(308,(v,w)=>{
                WriteDetailEntity(w,v.MineTowerId); WriteIndustryProto(w,v.ProductId.Value);
            },(r,d)=>{
                var id=ReadEntityId(r); string product=ReadScalarProtoId(r); r.EnsureEnd();
                return new AddProductToNotifyIfCannotDumpCmd(RequireEntity<MineTower>(d,id),RequireResearchTradeProto<LooseProductProto>(d,product));
            }),
            Scalar<RemoveProductToNotifyIfCannotDumpCmd>(309,(v,w)=>{
                WriteDetailEntity(w,v.MineTowerId); WriteIndustryProto(w,v.ProductId.Value);
            },(r,d)=>{
                var id=ReadEntityId(r); string product=ReadScalarProtoId(r); r.EnsureEnd();
                return new RemoveProductToNotifyIfCannotDumpCmd(RequireEntity<MineTower>(d,id),RequireResearchTradeProto<LooseProductProto>(d,product));
            }),
            Scalar<SetOfficeComputingBoostStepCmd>(310,(v,w)=>{
                WriteDetailEntity(w,v.OfficeId); WriteBounded(w,v.Step,0,4096,"Office computing step");
            },(r,d)=>{
                var id=ReadEntityId(r); int step=ReadBounded(r,0,4096,"Office computing step"); r.EnsureEnd();
                if(step>RequireEntity<OfficeBuilding>(d,id).Prototype.MaxComputingBoostSteps)throw new ProtocolException("Office computing step exceeds this office's range.");
                return new SetOfficeComputingBoostStepCmd(id,step);
            }),
            Scalar<SetOfficeFocusStepCmd>(311,(v,w)=>{
                WriteIndustryProto(w,v.FocusId.Value); WriteBounded(w,v.Diff,-4096,4096,"Focus step change");
            },(r,d)=>{
                string id=ReadScalarProtoId(r); int diff=ReadBounded(r,-4096,4096,"Focus step change"); r.EnsureEnd();
                var focus=d.Resolve<OfficeBuildingsManager>().AllFocuses.FirstOrDefault(x=>x.Prototype.Id.Value==id);
                if(focus==null)throw new ProtocolException("Office focus does not exist.");
                return new SetOfficeFocusStepCmd(new Proto.ID(id),diff);
            }),
            Scalar<ShipyardWorldEntityConstructionToggle>(312,(v,w)=>{
                WriteDetailEntity(w,v.ShipyardId); WriteDetailEntity(w,v.WorldEntityIdToConstruct);
            },(r,d)=>{
                var id=ReadEntityId(r); var world=ReadEntityId(r); r.EnsureEnd(); RequireEntity<Shipyard>(d,id); RequireEntity<IWorldMapRepairableEntity>(d,world);
                return new ShipyardWorldEntityConstructionToggle(id,world);
            }),
            Scalar<DataCenterToggleRackCmd>(313,(v,w)=>{
                WriteDetailEntity(w,v.DataCenterId); WriteIndustryProto(w,v.ServerRackId.Value); WriteBounded(w,v.Difference,-4096,4096,"Rack count change");
            },(r,d)=>{
                var id=ReadEntityId(r); string proto=ReadScalarProtoId(r); int diff=ReadBounded(r,-4096,4096,"Rack count change"); r.EnsureEnd();
                var data=RequireEntity<DataCenter>(d,id); var rack=RequireResearchTradeProto<ServerRackProto>(d,proto);
                // Shift/control bulk buttons may ask for more racks than fit. Native processing
                // stops at capacity/resources (or the first empty removal), so retain that behavior.
                if(!data.ServersCounts.ContainsKey(rack))throw new ProtocolException("Rack is not supported by this data center.");
                return new DataCenterToggleRackCmd(id,rack.Id,diff);
            }),
            Scalar<SetElectricityGenerationPriorityCmd>(314,(v,w)=>{
                WriteDetailEntity(w,v.EntityId); WriteBounded(w,v.Priority,0,14,"Generation priority");
            },(r,d)=>{
                var id=ReadEntityId(r); int priority=ReadBounded(r,0,14,"Generation priority"); r.EnsureEnd(); RequireEntity<IElectricityGeneratingEntity>(d,id);
                return new SetElectricityGenerationPriorityCmd(id,priority);
            }),
            Scalar<NuclearReactorSetEnrichmentStepCmd>(315,(v,w)=>{
                WriteDetailEntity(w,v.ReactorId); WriteBounded(w,v.EnrichmentStep,0,4096,"Enrichment step");
            },(r,d)=>{
                var id=ReadEntityId(r); int step=ReadBounded(r,0,4096,"Enrichment step"); r.EnsureEnd(); var reactor=RequireEntity<NuclearReactor>(d,id);
                if(reactor.Prototype.Enrichment.IsNone || step>=reactor.Prototype.Enrichment.Value.EnrichmentSteps.Length)throw new ProtocolException("Enrichment step is not supported by this reactor.");
                return new NuclearReactorSetEnrichmentStepCmd(id,step);
            }),
            Scalar<NuclearReactorSetPowerLevelCmd>(316,(v,w)=>{
                WriteDetailEntity(w,v.ReactorId); WriteBounded(w,v.PowerLevel,0,4096,"Reactor power level");
            },(r,d)=>{
                var id=ReadEntityId(r); int level=ReadBounded(r,0,4096,"Reactor power level"); r.EnsureEnd();
                if(level>RequireEntity<NuclearReactor>(d,id).MaxPowerLevel)throw new ProtocolException("Power level exceeds this reactor's range.");
                return new NuclearReactorSetPowerLevelCmd(id,level);
            }),
            Scalar<AddVehicleReplacementTaskCmd>(317,(v,w)=>{
                WriteIndustryProto(w,v.CurrentProtoId.Value); WriteIndustryProto(w,v.ReplacementProtoId.Value); WriteZone(w,v.ZoneId);
                WriteIndustryOptionalEntity(w,v.VehicleDepotId); w.WriteBoolean(v.UnassignedOnly); WriteIndustryOptionalEntity(w,v.AssigneeId);
                WriteBounded(w,v.Limit,0,1000000,"Vehicle replacement limit");
            },(r,d)=>{
                string current=ReadScalarProtoId(r),replacement=ReadScalarProtoId(r); var zone=ReadZone(r); var depot=ReadIndustryOptionalEntity(r);
                bool unassigned=r.ReadBoolean(); var assignee=ReadIndustryOptionalEntity(r); int limit=ReadBounded(r,0,1000000,"Vehicle replacement limit"); r.EnsureEnd();
                var oldProto=RequireResearchTradeProto<DrivingEntityProto>(d,current); var newProto=RequireIndustryVehicleProto(d,replacement);
                if(oldProto==newProto)throw new ProtocolException("Replacement vehicle must differ from the original."); RequireZone(d,zone);
                if(depot.HasValue && !RequireEntity<VehicleDepotBase>(d,depot.Value).Prototype.BuildableEntities.Contains(newProto))throw new ProtocolException("Replacement vehicle is not supported by this depot.");
                if(assignee.HasValue)RequireEntity<IEntityAssignedWithVehicles>(d,assignee.Value);
                return new AddVehicleReplacementTaskCmd(oldProto.Id,newProto.Id,zone,depot,unassigned,assignee,limit);
            }),
            Scalar<ReplaceVehicleCmd>(318,(v,w)=>{
                WriteDetailEntity(w,v.VehicleId); WriteIndustryProto(w,v.TargetProtoId.Value);
            },(r,d)=>{
                var id=ReadEntityId(r); string proto=ReadScalarProtoId(r); r.EnsureEnd(); RequireEntity<Vehicle>(d,id);
                return new ReplaceVehicleCmd(id,RequireIndustryVehicleProto(d,proto).Id);
            }),
            Scalar<RemoveVehicleReplacementTaskCmd>(319,(v,w)=>w.WriteUInt32(v.TaskId),(r,d)=>{
                uint id=r.ReadUInt32(); r.EnsureEnd(); var manager=d.Resolve<VehiclesReplacer>();
                if(!manager.ActiveReplacementTasks.Any(x=>x.TaskId==id) && !manager.FinishedReplacementTasks.Any(x=>x.TaskId==id))throw new ProtocolException("Vehicle replacement task does not exist.");
                return new RemoveVehicleReplacementTaskCmd(id);
            }),
            Scalar<SetShiftsCountForMineCmd>(320,(v,w)=>{
                WriteDetailEntity(w,v.EntityId); WriteBounded(w,v.ShiftsCount,0,4096,"Mine production shifts");
            },(r,d)=>{
                var id=ReadEntityId(r); int shifts=ReadBounded(r,0,4096,"Mine production shifts"); r.EnsureEnd();
                if(shifts>RequireEntity<WorldMapMine>(d,id).MaxProductionSteps)throw new ProtocolException("Mine shift count exceeds its level.");
                return new SetShiftsCountForMineCmd(id,shifts);
            }),
            Scalar<FleetModificationsPrepareCmd>(321,(v,w)=>WriteIndustryFleet(w,v.ModificationRequest),(r,d)=>{
                var request=ReadIndustryFleet(r); r.EnsureEnd(); ValidateIndustryFleet(d,request); return new FleetModificationsPrepareCmd(request);
            }),
            Scalar<GoToLocationCmd>(322,(v,w)=>{
                WriteBounded(w,v.LocationId.Value,0,int.MaxValue,"World location"); WriteBounded(w,(int)v.Reason,0,2,"Visit reason");
            },(r,d)=>{
                int id=ReadBounded(r,0,int.MaxValue,"World location"),reason=ReadBounded(r,0,2,"Visit reason"); r.EnsureEnd();
                if(d.Resolve<WorldMapManager>().Map[new WorldMapLocId(id)].IsNone || !d.Resolve<TravelingFleetManager>().HasFleet)throw new ProtocolException("World location or traveling fleet does not exist.");
                return new GoToLocationCmd(new WorldMapLocId(id),(LocationVisitReason)reason);
            }),
            Scalar<AcceptLoanCmd>(323,(v,w)=>{
                WriteIndustryProto(w,v.ProductId.Value); WriteBounded(w,v.ToBorrow.Value,100,1000000,"Loan amount"); WriteIndustryLoanYears(w,v.YearsTotal);
            },(r,d)=>{
                string product=ReadScalarProtoId(r); int amount=ReadBounded(r,100,1000000,"Loan amount"),years=ReadBounded(r,5,50,"Loan years"); r.EnsureEnd();
                ValidateIndustryLoanYears(years); var proto=RequireResearchTradeProto<ProductProto>(d,product);
                if(!d.Resolve<LoansManager>().GetAllProductsToBorrow().Contains(proto))throw new ProtocolException("No discovered lender offers this unlocked product.");
                return new AcceptLoanCmd(new ProductProto.ID(product),new Quantity(amount),years);
            }),
            Scalar<MakeLoanOverpaymentCmd>(324,(v,w)=>{
                WriteIndustryProto(w,v.ProductId.Value); w.WriteUInt32(v.LoanId); WriteBounded(w,v.ToPay.Value,1,1000000,"Loan overpayment");
            },(r,d)=>{
                string product=ReadScalarProtoId(r); uint id=r.ReadUInt32(); int amount=ReadBounded(r,1,1000000,"Loan overpayment"); r.EnsureEnd();
                if(RequireIndustryLoan(d,id).Product.Id.Value!=product)throw new ProtocolException("Loan product does not match this loan.");
                return new MakeLoanOverpaymentCmd(new ProductProto.ID(product),id,new Quantity(amount));
            }),
            Scalar<SetLoanBufferPriorityCmd>(325,(v,w)=>{
                w.WriteUInt32(v.LoanId); WriteBounded(w,v.Priority,0,14,"Loan priority");
            },(r,d)=>{
                uint id=r.ReadUInt32(); int priority=ReadBounded(r,0,14,"Loan priority"); r.EnsureEnd(); RequireIndustryLoan(d,id); return new SetLoanBufferPriorityCmd(id,priority);
            }),
            Scalar<WorldMapEntityCancelRepairCmd>(326,(v,w)=>WriteDetailEntity(w,v.EntityId),(r,d)=>{
                var id=ReadEntityId(r); r.EnsureEnd(); RequireEntity<WorldMapRepairableEntity>(d,id); return new WorldMapEntityCancelRepairCmd(id);
            }),
            Scalar<WorldMapEntityStartRepairCmd>(327,(v,w)=>WriteDetailEntity(w,v.EntityId),(r,d)=>{
                var id=ReadEntityId(r); r.EnsureEnd(); RequireEntity<WorldMapRepairableEntity>(d,id); return new WorldMapEntityStartRepairCmd(id);
            }),
            Scalar<WorldMapEntityUpgradeCmd>(328,(v,w)=>WriteDetailEntity(w,v.EntityId),(r,d)=>{
                var id=ReadEntityId(r); r.EnsureEnd(); RequireEntity<IUpgradableWorldEntity>(d,id); return new WorldMapEntityUpgradeCmd(id);
            }),
            Scalar<WorldMapSettlementAdoptPopsCmd>(329,(v,w)=>{
                WriteDetailEntity(w,v.EntityId); WriteBounded(w,v.PopsCount,1,1000000,"Population to adopt");
            },(r,d)=>{
                var id=ReadEntityId(r); int pops=ReadBounded(r,1,1000000,"Population to adopt"); r.EnsureEnd(); var village=RequireEntity<WorldMapVillage>(d,id);
                if(pops>village.PopsAvailable)throw new ProtocolException("Population exceeds the village's available people."); return new WorldMapSettlementAdoptPopsCmd(id,pops);
            }),
        };

        private static bool TryEncodeRemainingIndustry(IInputCommand command,PayloadWriter writer)
        {
            foreach(var spec in RemainingIndustryCommands)if(command.GetType()==spec.Type){writer.WriteUInt16(spec.Id);spec.Write(command,writer);return true;}
            return false;
        }
        private static IInputCommand? DecodeRemainingIndustry(GameCommandKind kind,PayloadReader reader,DependencyResolver resolver)
        {
            foreach(var spec in RemainingIndustryCommands)if(spec.Id==(ushort)kind)return spec.Read(reader,resolver);
            return null;
        }
        private static void WriteIndustryProto(PayloadWriter w,string value){ValidateProtoId(value);w.WriteString(value,MaxProtoIdBytes);}
        private static void WriteIndustryOptionalEntity(PayloadWriter w,EntityId? id){w.WriteBoolean(id.HasValue);if(id.HasValue)WriteDetailEntity(w,id.Value);}
        private static EntityId? ReadIndustryOptionalEntity(PayloadReader r)=>r.ReadBoolean()?ReadEntityId(r):(EntityId?)null;
        private static DrivingEntityProto RequireIndustryVehicleProto(DependencyResolver d,string id)
        {
            var proto=RequireResearchTradeProto<DrivingEntityProto>(d,id);
            if(!proto.IsUnlockedAndAvailable)throw new ProtocolException("Replacement vehicle is locked or unavailable.");return proto;
        }
        private static void WriteIndustryLoanYears(PayloadWriter w,int years){ValidateIndustryLoanYears(years);w.WriteInt32(years);}
        private static void ValidateIndustryLoanYears(int years)
        {if(years!=5 && years!=10 && years!=15 && years!=20 && years!=30 && years!=50)throw new ProtocolException("Unsupported loan duration.");}
        private static ActiveLoan RequireIndustryLoan(DependencyResolver d,uint id)
        {
            var loan=d.Resolve<LoansManager>().ActiveLoans.FirstOrDefault(x=>x.Id==id);
            if(loan==null)throw new ProtocolException("Loan does not exist.");return loan;
        }
        private static void WriteIndustryFleet(PayloadWriter w,FleetEntityModificationRequest request)
        {
            WriteOptionalProto(w,request.HullId?.Value); WriteBounded(w,request.SlotsData.Length,0,64,"Fleet modification slots");
            var seen=new HashSet<string>(StringComparer.Ordinal);
            foreach(var slot in request.SlotsData){if(!seen.Add(slot.SlotId.Value))throw new ProtocolException("Duplicate fleet slot.");WriteIndustryProto(w,slot.SlotId.Value);WriteOptionalProto(w,slot.Part?.Value);}
        }
        private static FleetEntityModificationRequest ReadIndustryFleet(PayloadReader r)
        {
            string? hull=ReadOptionalProto(r); int count=ReadBounded(r,0,64,"Fleet modification slots"); var slots=new SlotModification[count];var seen=new HashSet<string>(StringComparer.Ordinal);
            for(int i=0;i<count;i++){string id=ReadScalarProtoId(r);string? part=ReadOptionalProto(r);if(!seen.Add(id))throw new ProtocolException("Duplicate fleet slot.");slots[i]=new SlotModification(new Proto.ID(id),part==null?(FleetEntityPartProto.ID?)null:new FleetEntityPartProto.ID(part));}
            return new FleetEntityModificationRequest(new ImmutableArray<SlotModification>(slots),hull==null?(FleetEntityHullProto.ID?)null:new FleetEntityHullProto.ID(hull));
        }
        private static void ValidateIndustryFleet(DependencyResolver d,FleetEntityModificationRequest request)
        {
            var manager=d.Resolve<TravelingFleetManager>();if(!manager.HasFleet)throw new ProtocolException("Traveling fleet does not exist.");
            var entity=manager.TravelingFleet.FleetEntity;
            // The shipped designer only exposes eligible slot parts and always emits a null hull.
            // Retain an unchanged hull field but reject arbitrary enemy/incompatible hull swaps.
            if(request.HullId.HasValue && request.HullId.Value!=entity.Hull.Proto.Id)throw new ProtocolException("The current ship designer does not support replacing the fleet hull.");
            foreach(var change in request.SlotsData)
            {
                var slot=entity.Slots.FirstOrDefault(x=>x.Proto.Id==change.SlotId);if(slot==null)throw new ProtocolException("Fleet slot does not exist.");
                if(change.Part.HasValue){var part=RequireResearchTradeProto<FleetEntityPartProto>(d,change.Part.Value.Value);if(!part.IsUnlockedAndAvailable || !slot.Proto.EligibleItems.Contains(part))throw new ProtocolException("Fleet part is locked or not eligible for this slot.");}
                else if(slot.ExistingPart.HasValue)throw new ProtocolException("Removing installed fleet parts is not supported by the native modification pricing path.");
            }
            if(!request.GetPriceForModifications(entity,d.Resolve<ProtosDb>(),out _))throw new ProtocolException("Fleet modification request contains no valid changes.");
        }
    }
}
