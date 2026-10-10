using CoiDedicated.Protocol;
using Mafi;
using Mafi.Core;
using Mafi.Core.Buildings.Cargo;
using Mafi.Core.Buildings.Farms;
using Mafi.Core.Buildings.Forestry;
using Mafi.Core.Buildings.OreSorting;
using Mafi.Core.Buildings.Shipyard;
using Mafi.Core.Buildings.Storages;
using Mafi.Core.Buildings.VehicleDepots;
using Mafi.Core.Entities;
using Mafi.Core.Entities.Commands;
using Mafi.Core.Entities.Dynamic;
using Mafi.Core.Entities.Priorities;
using Mafi.Core.Entities.Static;
using Mafi.Core.Entities.Static.Commands;
using Mafi.Core.Factory.Machines;
using Mafi.Core.Input;
using Mafi.Core.Products;
using Mafi.Core.Vehicles.Commands;

namespace CoiDedicated.GameAdapter
{
    public static partial class GameCommandCodec
    {
        private static readonly ScalarCommandSpec[] DetailCommands = {
            Scalar<EntityDisableLogisticsToggleCmd>(198, (v,w) => {
                WriteDetailEntity(w,v.EntityId); w.WriteBoolean(v.IsInput); WriteBounded(w,(int)v.Mode,0,2,"Logistics mode");
            }, (r,d) => {
                var id=ReadEntityId(r); bool input=r.ReadBoolean(); int mode=ReadBounded(r,0,2,"Logistics mode"); r.EnsureEnd();
                RequireEntity<IEntityWithLogisticsControl>(d,id); return new EntityDisableLogisticsToggleCmd(id,input,(EntityLogisticsMode)mode);
            }),
            Scalar<ToggleImportRouteEnforcementCmd>(199, (v,w) => WriteDetailEntity(w,v.EntityToToggleId), (r,d) => {
                var id=ReadEntityId(r); r.EnsureEnd(); return new ToggleImportRouteEnforcementCmd(RequireEntity<IEntityAssignedAsInput>(d,id));
            }),
            Scalar<ForestryTowerSetCutPercentageCmd>(200, (v,w) => {
                WriteDetailEntity(w,v.ForestryTowerId); WriteBounded(w,v.CutPercent.RawValue,0,100000,"Cut percentage");
            }, (r,d) => {
                var id=ReadEntityId(r); int percent=ReadBounded(r,0,100000,"Cut percentage"); r.EnsureEnd(); RequireEntity<ForestryTower>(d,id);
                return new ForestryTowerSetCutPercentageCmd(id,Percent.FromRaw(percent));
            }),
            Scalar<FarmSetFertilityTargetCmd>(201, (v,w) => {
                WriteDetailEntity(w,v.FarmId); WriteBounded(w,v.Target.RawValue,0,140000,"Fertility target");
            }, (r,d) => {
                var id=ReadEntityId(r); int percent=ReadBounded(r,0,140000,"Fertility target"); r.EnsureEnd(); RequireEntity<Farm>(d,id);
                return new FarmSetFertilityTargetCmd(id,Percent.FromRaw(percent));
            }),
            Scalar<StorageAlertSetThresholdCmd>(202, (v,w) => {
                WriteDetailEntity(w,v.StorageId); WriteBounded(w,v.Value.RawValue,0,100000,"Alert threshold"); w.WriteBoolean(v.IsAbove);
            }, (r,d) => {
                var id=ReadEntityId(r); int percent=ReadBounded(r,0,100000,"Alert threshold"); bool above=r.ReadBoolean(); r.EnsureEnd();
                RequireEntity<IEntity>(d,id); return new StorageAlertSetThresholdCmd(id,Percent.FromRaw(percent),above);
            }),
            Scalar<AnimalFarmToggleGrowthPauseCmd>(203, (v,w) => WriteDetailEntity(w,v.AnimalFarmId), (r,d) => {
                var id=ReadEntityId(r); r.EnsureEnd(); RequireEntity<AnimalFarm>(d,id); return new AnimalFarmToggleGrowthPauseCmd(id);
            }),
            Scalar<AssignProductToSlotCmd>(204, (v,w) => {
                WriteDetailEntity(w,v.EntityId); WriteOptionalProto(w,v.ProductId?.Value); WriteBounded(w,v.Slot,0,255,"Product slot");
            }, (r,d) => {
                var id=ReadEntityId(r); string? product=ReadOptionalProto(r); int slot=ReadBounded(r,0,255,"Product slot"); r.EnsureEnd();
                var entity=RequireEntity<IEntityWithMultipleProductsToAssign>(d,id);
                if(slot>=entity.BuffersPerSlot.Count) throw new ProtocolException("Product slot does not exist.");
                if(product!=null && !entity.SupportedProducts.Contains(RequireResearchTradeProto<ProductProto>(d,product)))
                    throw new ProtocolException("Product is not supported by this building.");
                return new AssignProductToSlotCmd(id,product==null?(ProductProto.ID?)null:new ProductProto.ID(product),slot);
            }),
            Scalar<RemoveVehicleFromBuildQueueCmd>(205, (v,w) => {
                WriteDetailEntity(w,v.VehicleDepotId); WriteBounded(w,v.Index,0,4095,"Queue index");
            }, (r,d) => {
                var id=ReadEntityId(r); int index=ReadBounded(r,0,4095,"Queue index"); r.EnsureEnd();
                var depot=RequireEntity<VehicleDepotBase>(d,id);
                if(index>=depot.BuildQueue.Count+depot.ReplaceQueue.Count)throw new ProtocolException("Vehicle queue index does not exist.");
                return new RemoveVehicleFromBuildQueueCmd(index,id);
            }),
            Scalar<BuildVehicleForeverCmd>(206, (v,w) => {
                WriteDetailEntity(w,v.VehicleDepotId); WriteOptionalProto(w,v.ProtoId?.Value);
            }, (r,d) => {
                var id=ReadEntityId(r); string? proto=ReadOptionalProto(r); r.EnsureEnd(); var depot=RequireEntity<VehicleDepotBase>(d,id);
                if(proto!=null) {
                    var vehicle=RequireResearchTradeProto<DrivingEntityProto>(d,proto);
                    if(!vehicle.IsUnlockedAndAvailable || !depot.Prototype.BuildableEntities.Contains(vehicle))
                        throw new ProtocolException("Vehicle is locked or not supported by this depot.");
                }
                return new BuildVehicleForeverCmd(proto==null?(DynamicEntityProto.ID?)null:new DynamicEntityProto.ID(proto),id);
            }),
            Scalar<ExcavatorTogglePreferredProductCmd>(207, (v,w) => {
                WriteDetailEntity(w,v.ExcavatorId); WriteOptionalProto(w,v.ProductToMine?.Value);
            }, (r,d) => {
                var id=ReadEntityId(r); string? product=ReadOptionalProto(r); r.EnsureEnd(); RequireEntity<Vehicle>(d,id);
                if(product!=null) RequireResearchTradeProto<ProductProto>(d,product);
                return new ExcavatorTogglePreferredProductCmd(id,product==null?(ProductProto.ID?)null:new ProductProto.ID(product));
            }),
            Scalar<CargoDepotSetFuelSliderStepCmd>(208, (v,w) => {
                WriteDetailEntity(w,v.CargoDepotId); WriteBounded(w,v.ImportStep,-1,10,"Fuel import"); WriteBounded(w,v.ExportStep,-1,10,"Fuel export");
            }, (r,d) => {
                var id=ReadEntityId(r); int import=ReadBounded(r,-1,10,"Fuel import"), export=ReadBounded(r,-1,10,"Fuel export"); r.EnsureEnd();
                RequireEntity<CargoDepot>(d,id); return new CargoDepotSetFuelSliderStepCmd(id,import,export);
            }),
            Scalar<ShipayardSetFuelSliderStepCmd>(209, (v,w) => {
                WriteDetailEntity(w,v.ShipyardId); WriteBounded(w,v.ImportStep,-1,10,"Fuel import"); WriteBounded(w,v.ExportStep,-1,10,"Fuel export");
            }, (r,d) => {
                var id=ReadEntityId(r); int import=ReadBounded(r,-1,10,"Fuel import"), export=ReadBounded(r,-1,10,"Fuel export"); r.EnsureEnd();
                RequireEntity<Shipyard>(d,id); return new ShipayardSetFuelSliderStepCmd(id,import,export);
            }),
            Scalar<SetCustomPriorityCmd>(210, (v,w) => {
                WriteDetailEntity(w,v.EntityId); ValidateProtoId(v.PriorityId); w.WriteString(v.PriorityId,MaxProtoIdBytes); ValidatePriority(v.Priority); w.WriteInt32(v.Priority);
            }, (r,d) => {
                var id=ReadEntityId(r); string key=ReadScalarProtoId(r); int priority=r.ReadInt32(); r.EnsureEnd(); ValidatePriority(priority);
                RequireEntity<IEntityWithCustomPriority>(d,id); return new SetCustomPriorityCmd(id,key,priority);
            }),
            Scalar<SetGlobalPriorityCmd>(211, (v,w) => {
                ValidateProtoId(v.PriorityId); w.WriteString(v.PriorityId,MaxProtoIdBytes); ValidatePriority(v.Priority); w.WriteInt32(v.Priority);
            }, (r,d) => {
                string key=ReadScalarProtoId(r); int priority=r.ReadInt32(); r.EnsureEnd(); ValidatePriority(priority); return new SetGlobalPriorityCmd(key,priority);
            }),
            Scalar<ReorderRecipeCmd>(212, (v,w) => {
                WriteDetailEntity(w,v.MachineId); WriteBounded(w,v.OldIndex,0,255,"Recipe index"); WriteBounded(w,v.NewIndexAfterRemove,0,255,"Recipe index");
            }, (r,d) => {
                var id=ReadEntityId(r); int oldIndex=ReadBounded(r,0,255,"Recipe index"),newIndex=ReadBounded(r,0,255,"Recipe index"); r.EnsureEnd();
                var machine=RequireEntity<Machine>(d,id);
                if(oldIndex>=machine.RecipesAssigned.Count || newIndex>=machine.RecipesAssigned.Count)throw new ProtocolException("Recipe index does not exist.");
                return new ReorderRecipeCmd(id,oldIndex,newIndex);
            }),
            Scalar<AddProductToSortCmd>(213, (v,w) => WriteSortProduct(w,v.SortingPlantId,v.ProductId), (r,d) => {
                var id=ReadEntityId(r); string product=ReadScalarProtoId(r); r.EnsureEnd();
                return new AddProductToSortCmd(RequireEntity<OreSortingPlant>(d,id),RequireResearchTradeProto<ProductProto>(d,product));
            }),
            Scalar<RemoveProductToSortCmd>(214, (v,w) => WriteSortProduct(w,v.SortingPlantId,v.ProductId), (r,d) => {
                var id=ReadEntityId(r); string product=ReadScalarProtoId(r); r.EnsureEnd();
                return new RemoveProductToSortCmd(RequireEntity<OreSortingPlant>(d,id),RequireResearchTradeProto<ProductProto>(d,product));
            }),
            Scalar<SetProductPortCmd>(215, (v,w) => {
                WriteSortProduct(w,v.SortingPlantId,v.ProductId); WriteBounded(w,v.PortIndex,0,255,"Sorting port"); w.WriteBoolean(v.ClearPortInstead);
            }, (r,d) => {
                var id=ReadEntityId(r); string product=ReadScalarProtoId(r); int port=ReadBounded(r,0,255,"Sorting port"); bool clear=r.ReadBoolean(); r.EnsureEnd();
                return new SetProductPortCmd(RequireEntity<OreSortingPlant>(d,id),RequireResearchTradeProto<ProductProto>(d,product),port,clear);
            }),
        };
        private static bool TryEncodeDetailControls(IInputCommand v, PayloadWriter w)
        { foreach(var s in DetailCommands)if(v.GetType()==s.Type){w.WriteUInt16(s.Id);s.Write(v,w);return true;} return false; }
        private static IInputCommand? DecodeDetailControls(GameCommandKind kind, PayloadReader r, DependencyResolver d)
        { foreach(var s in DetailCommands)if(s.Id==(ushort)kind)return s.Read(r,d);return null; }
        private static void WriteDetailEntity(PayloadWriter w,EntityId id)
        { ValidateEntityId(id.Value);w.WriteInt32(id.Value); }
        private static void WriteBounded(PayloadWriter w,int value,int min,int max,string label)
        { ValidateRange(value,min,max,label);w.WriteInt32(value); }
        private static int ReadBounded(PayloadReader r,int min,int max,string label)
        { int value=r.ReadInt32();ValidateRange(value,min,max,label);return value; }
        private static void WriteSortProduct(PayloadWriter w,EntityId id,ProductProto.ID product)
        { WriteDetailEntity(w,id);ValidateProtoId(product.Value);w.WriteString(product.Value,MaxProtoIdBytes); }
    }
}
