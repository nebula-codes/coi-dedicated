// Explicit scalar command schemas for CoI 0.8.7d. IDs are stable; never reorder existing entries.
// Derived from public command declarations and UI constructor call sites, not game serializer code.
using System;
using System.Collections.Generic;
using CoiDedicated.Protocol;
using Mafi;
using Mafi.Core.Input;
using Mafi.Core.Entities;
using Mafi.Core;
namespace CoiDedicated.GameAdapter {
public static partial class GameCommandCodec {
private static readonly ScalarCommandSpec[] ScalarCommands = new ScalarCommandSpec[] {
    Scalar<Mafi.Base.Prototypes.Machines.PowerGenerators.ToggleMechGeneratorAutoBalanceCmd>(100, (v,w) => {
        ValidateEntityId(v.EntityId.Value); w.WriteInt32(v.EntityId.Value);
    }, (r,d) => {
        var EntityId = ReadEntityId(r);
        r.EnsureEnd();
        RequireEntity<IEntity>(d, EntityId);
        return new Mafi.Base.Prototypes.Machines.PowerGenerators.ToggleMechGeneratorAutoBalanceCmd(EntityId);
    }),
    Scalar<Mafi.Core.Buildings.Cargo.Modules.CargoShipReplaceFuelCmd>(101, (v,w) => {
        ValidateEntityId(v.CargoShipId.Value); w.WriteInt32(v.CargoShipId.Value);
        ValidateProtoId(v.FuelId.Value); w.WriteString(v.FuelId.Value, MaxProtoIdBytes);
    }, (r,d) => {
        var CargoShipId = ReadEntityId(r);
        var FuelId = new Mafi.Core.Products.ProductProto.ID(ReadScalarProtoId(r));
        r.EnsureEnd();
        RequireEntity<IEntity>(d, CargoShipId);
        RequireResearchTradeProto<Mafi.Core.Products.ProductProto>(d, FuelId.Value);
        return new Mafi.Core.Buildings.Cargo.Modules.CargoShipReplaceFuelCmd(CargoShipId, FuelId);
    }),
    Scalar<Mafi.Core.Buildings.Cargo.TradeDockToggleUnloadPriorityCmd>(102, (v,w) => {
        ValidateEntityId(v.TradeDockId.Value); w.WriteInt32(v.TradeDockId.Value);
    }, (r,d) => {
        var TradeDockId = ReadEntityId(r);
        r.EnsureEnd();
        RequireEntity<IEntity>(d, TradeDockId);
        return new Mafi.Core.Buildings.Cargo.TradeDockToggleUnloadPriorityCmd(TradeDockId);
    }),
    Scalar<Mafi.Core.Buildings.Farms.FarmForceNextSlotCmd>(103, (v,w) => {
        ValidateEntityId(v.FarmId.Value); w.WriteInt32(v.FarmId.Value);
    }, (r,d) => {
        var FarmId = ReadEntityId(r);
        r.EnsureEnd();
        RequireEntity<IEntity>(d, FarmId);
        return new Mafi.Core.Buildings.Farms.FarmForceNextSlotCmd(FarmId);
    }),
    Scalar<Mafi.Core.Buildings.Farms.FarmHarvestNowCmd>(104, (v,w) => {
        ValidateEntityId(v.FarmId.Value); w.WriteInt32(v.FarmId.Value);
    }, (r,d) => {
        var FarmId = ReadEntityId(r);
        r.EnsureEnd();
        RequireEntity<IEntity>(d, FarmId);
        return new Mafi.Core.Buildings.Farms.FarmHarvestNowCmd(FarmId);
    }),
    Scalar<Mafi.Core.Buildings.Farms.ToggleFullBufferNotificationCmd>(105, (v,w) => {
        ValidateEntityId(v.FarmId.Value); w.WriteInt32(v.FarmId.Value);
    }, (r,d) => {
        var FarmId = ReadEntityId(r);
        r.EnsureEnd();
        RequireEntity<IEntity>(d, FarmId);
        return new Mafi.Core.Buildings.Farms.ToggleFullBufferNotificationCmd(FarmId);
    }),
    Scalar<Mafi.Core.Buildings.FuelStations.ToggleFuelStationTrucksAllowedToRefuelCmd>(106, (v,w) => {
        ValidateEntityId(v.FuelStationId.Value); w.WriteInt32(v.FuelStationId.Value);
    }, (r,d) => {
        var FuelStationId = ReadEntityId(r);
        r.EnsureEnd();
        RequireEntity<IEntity>(d, FuelStationId);
        return new Mafi.Core.Buildings.FuelStations.ToggleFuelStationTrucksAllowedToRefuelCmd(FuelStationId);
    }),
    Scalar<Mafi.Core.Buildings.OreSorting.SortingPlantSetBlockedProductAlertCmd>(107, (v,w) => {
        ValidateEntityId(v.SortingPlantId.Value); w.WriteInt32(v.SortingPlantId.Value);
        ValidateProtoId(v.ProductId.Value); w.WriteString(v.ProductId.Value, MaxProtoIdBytes);
        w.WriteBoolean(v.IsAlertEnabled);
    }, (r,d) => {
        var SortingPlantId = ReadEntityId(r);
        var ProductId = new Mafi.Core.Products.ProductProto.ID(ReadScalarProtoId(r));
        var IsAlertEnabled = r.ReadBoolean();
        r.EnsureEnd();
        RequireEntity<IEntity>(d, SortingPlantId);
        RequireResearchTradeProto<Mafi.Core.Products.ProductProto>(d, ProductId.Value);
        return new Mafi.Core.Buildings.OreSorting.SortingPlantSetBlockedProductAlertCmd(SortingPlantId, ProductId, IsAlertEnabled);
    }),
    Scalar<Mafi.Core.Buildings.Shipyard.ShipyardDiscardProductCmd>(108, (v,w) => {
        ValidateEntityId(v.ShipyardId.Value); w.WriteInt32(v.ShipyardId.Value);
        ValidateProtoId(v.ProductId.Value); w.WriteString(v.ProductId.Value, MaxProtoIdBytes);
    }, (r,d) => {
        var ShipyardId = ReadEntityId(r);
        var ProductId = new Mafi.Core.Products.ProductProto.ID(ReadScalarProtoId(r));
        r.EnsureEnd();
        RequireEntity<IEntity>(d, ShipyardId);
        RequireResearchTradeProto<Mafi.Core.Products.ProductProto>(d, ProductId.Value);
        return new Mafi.Core.Buildings.Shipyard.ShipyardDiscardProductCmd(ShipyardId, ProductId);
    }),
    Scalar<Mafi.Core.Buildings.Shipyard.ShipyardMakePrimaryCmd>(109, (v,w) => {
        ValidateEntityId(v.ShipyardId.Value); w.WriteInt32(v.ShipyardId.Value);
    }, (r,d) => {
        var ShipyardId = ReadEntityId(r);
        r.EnsureEnd();
        RequireEntity<IEntity>(d, ShipyardId);
        return new Mafi.Core.Buildings.Shipyard.ShipyardMakePrimaryCmd(ShipyardId);
    }),
    Scalar<Mafi.Core.Buildings.Shipyard.ShipyardToggleAutoRepairCmd>(110, (v,w) => {
        ValidateEntityId(v.ShipyardId.Value); w.WriteInt32(v.ShipyardId.Value);
    }, (r,d) => {
        var ShipyardId = ReadEntityId(r);
        r.EnsureEnd();
        RequireEntity<IEntity>(d, ShipyardId);
        return new Mafi.Core.Buildings.Shipyard.ShipyardToggleAutoRepairCmd(ShipyardId);
    }),
    Scalar<Mafi.Core.Buildings.Shipyard.ShipyardToggleUnloadPriorityCmd>(111, (v,w) => {
        ValidateEntityId(v.ShipyardId.Value); w.WriteInt32(v.ShipyardId.Value);
    }, (r,d) => {
        var ShipyardId = ReadEntityId(r);
        r.EnsureEnd();
        RequireEntity<IEntity>(d, ShipyardId);
        return new Mafi.Core.Buildings.Shipyard.ShipyardToggleUnloadPriorityCmd(ShipyardId);
    }),
    Scalar<Mafi.Core.Buildings.Shipyard.ShipyardToggleWorksPauseCmd>(112, (v,w) => {
        ValidateEntityId(v.ShipyardId.Value); w.WriteInt32(v.ShipyardId.Value);
    }, (r,d) => {
        var ShipyardId = ReadEntityId(r);
        r.EnsureEnd();
        RequireEntity<IEntity>(d, ShipyardId);
        return new Mafi.Core.Buildings.Shipyard.ShipyardToggleWorksPauseCmd(ShipyardId);
    }),
    Scalar<Mafi.Core.Buildings.Storages.NuclearWaste.NuclearWasteStorageToggleOutputPortCmd>(113, (v,w) => {
        ValidateEntityId(v.StorageId.Value); w.WriteInt32(v.StorageId.Value);
    }, (r,d) => {
        var StorageId = ReadEntityId(r);
        r.EnsureEnd();
        RequireEntity<IEntity>(d, StorageId);
        return new Mafi.Core.Buildings.Storages.NuclearWaste.NuclearWasteStorageToggleOutputPortCmd(StorageId);
    }),
    Scalar<Mafi.Core.Buildings.Storages.StorageAddAllowedTruckGroupCmd>(114, (v,w) => {
        ValidateEntityId(v.StorageId.Value); w.WriteInt32(v.StorageId.Value);
        ValidateProtoId(v.TruckProtoId.Value); w.WriteString(v.TruckProtoId.Value, MaxProtoIdBytes);
    }, (r,d) => {
        var StorageId = ReadEntityId(r);
        var TruckProtoId = new Mafi.Core.Prototypes.Proto.ID(ReadScalarProtoId(r));
        r.EnsureEnd();
        RequireEntity<IEntity>(d, StorageId);
        RequireResearchTradeProto<Mafi.Core.Prototypes.Proto>(d, TruckProtoId.Value);
        return new Mafi.Core.Buildings.Storages.StorageAddAllowedTruckGroupCmd(StorageId, TruckProtoId);
    }),
    Scalar<Mafi.Core.Buildings.Storages.StorageAlertSetEnabledCmd>(115, (v,w) => {
        ValidateEntityId(v.StorageId.Value); w.WriteInt32(v.StorageId.Value);
        w.WriteBoolean(v.IsEnabled);
        w.WriteBoolean(v.IsAbove);
    }, (r,d) => {
        var StorageId = ReadEntityId(r);
        var IsEnabled = r.ReadBoolean();
        var IsAbove = r.ReadBoolean();
        r.EnsureEnd();
        RequireEntity<IEntity>(d, StorageId);
        return new Mafi.Core.Buildings.Storages.StorageAlertSetEnabledCmd(StorageId, IsEnabled, IsAbove);
    }),
    Scalar<Mafi.Core.Buildings.Storages.StorageDisableLogisticsToggleCmd>(116, (v,w) => {
        ValidateEntityId(v.StorageId.Value); w.WriteInt32(v.StorageId.Value);
        w.WriteBoolean(v.IsInput);
        w.WriteBoolean(v.IsDisabled);
    }, (r,d) => {
        var StorageId = ReadEntityId(r);
        var IsInput = r.ReadBoolean();
        var IsDisabled = r.ReadBoolean();
        r.EnsureEnd();
        RequireEntity<IEntity>(d, StorageId);
        return new Mafi.Core.Buildings.Storages.StorageDisableLogisticsToggleCmd(StorageId, IsInput, IsDisabled);
    }),
    Scalar<Mafi.Core.Buildings.Storages.StorageRemoveAllowedTruckGroupCmd>(117, (v,w) => {
        ValidateEntityId(v.StorageId.Value); w.WriteInt32(v.StorageId.Value);
        ValidateProtoId(v.TruckProtoId.Value); w.WriteString(v.TruckProtoId.Value, MaxProtoIdBytes);
    }, (r,d) => {
        var StorageId = ReadEntityId(r);
        var TruckProtoId = new Mafi.Core.Prototypes.Proto.ID(ReadScalarProtoId(r));
        r.EnsureEnd();
        RequireEntity<IEntity>(d, StorageId);
        RequireResearchTradeProto<Mafi.Core.Prototypes.Proto>(d, TruckProtoId.Value);
        return new Mafi.Core.Buildings.Storages.StorageRemoveAllowedTruckGroupCmd(StorageId, TruckProtoId);
    }),
    Scalar<Mafi.Core.Buildings.Storages.StorageSetProductCmd>(118, (v,w) => {
        ValidateEntityId(v.StorageId.Value); w.WriteInt32(v.StorageId.Value);
        ValidateProtoId(v.ProductId.Value); w.WriteString(v.ProductId.Value, MaxProtoIdBytes);
    }, (r,d) => {
        var StorageId = ReadEntityId(r);
        var ProductId = new Mafi.Core.Products.ProductProto.ID(ReadScalarProtoId(r));
        r.EnsureEnd();
        RequireEntity<IEntity>(d, StorageId);
        RequireResearchTradeProto<Mafi.Core.Products.ProductProto>(d, ProductId.Value);
        return new Mafi.Core.Buildings.Storages.StorageSetProductCmd(StorageId, ProductId);
    }),
    Scalar<Mafi.Core.Buildings.Storages.ToggleEnforceAssignedVehiclesForEntityCmd>(119, (v,w) => {
        ValidateEntityId(v.StorageId.Value); w.WriteInt32(v.StorageId.Value);
        w.WriteBoolean(v.IsEnforced);
    }, (r,d) => {
        var StorageId = ReadEntityId(r);
        var IsEnforced = r.ReadBoolean();
        r.EnsureEnd();
        RequireEntity<IEntity>(d, StorageId);
        return new Mafi.Core.Buildings.Storages.ToggleEnforceAssignedVehiclesForEntityCmd(StorageId, IsEnforced);
    }),
    Scalar<Mafi.Core.Entities.QuickRemoveEntityRefundCmd>(120, (v,w) => {
        ValidateEntityId(v.EntityId.Value); w.WriteInt32(v.EntityId.Value);
    }, (r,d) => {
        var EntityId = ReadEntityId(r);
        r.EnsureEnd();
        RequireEntity<IEntity>(d, EntityId);
        return new Mafi.Core.Entities.QuickRemoveEntityRefundCmd(EntityId);
    }),
    Scalar<Mafi.Core.Entities.SetPlanningModeEnabledCmd>(121, (v,w) => {
        w.WriteBoolean(v.IsEnabled);
    }, (r,d) => {
        var IsEnabled = r.ReadBoolean();
        r.EnsureEnd();
        return new Mafi.Core.Entities.SetPlanningModeEnabledCmd(IsEnabled);
    }),
    Scalar<Mafi.Core.Entities.Ships.RecoverShipCmd>(122, (v,w) => {
        ValidateEntityId(v.ShipId.Value); w.WriteInt32(v.ShipId.Value);
    }, (r,d) => {
        var ShipId = ReadEntityId(r);
        r.EnsureEnd();
        RequireEntity<IEntity>(d, ShipId);
        return new Mafi.Core.Entities.Ships.RecoverShipCmd(ShipId);
    }),
    Scalar<Mafi.Core.Entities.Static.Commands.CloneConfigBetweenEntitiesCmd>(123, (v,w) => {
        ValidateEntityId(v.SourceEntityId.Value); w.WriteInt32(v.SourceEntityId.Value);
        ValidateEntityId(v.TargetEntityId.Value); w.WriteInt32(v.TargetEntityId.Value);
    }, (r,d) => {
        var SourceEntityId = ReadEntityId(r);
        var TargetEntityId = ReadEntityId(r);
        r.EnsureEnd();
        RequireEntity<IEntity>(d, SourceEntityId);
        RequireEntity<IEntity>(d, TargetEntityId);
        return new Mafi.Core.Entities.Static.Commands.CloneConfigBetweenEntitiesCmd(SourceEntityId, TargetEntityId);
    }),
    Scalar<Mafi.Core.Entities.Static.Commands.ToggleConstructionPriorityCmd>(124, (v,w) => {
        ValidateEntityId(v.EntityId.Value); w.WriteInt32(v.EntityId.Value);
    }, (r,d) => {
        var EntityId = ReadEntityId(r);
        r.EnsureEnd();
        RequireEntity<IEntity>(d, EntityId);
        return new Mafi.Core.Entities.Static.Commands.ToggleConstructionPriorityCmd(EntityId);
    }),
    Scalar<Mafi.Core.Entities.Static.Commands.TryTransformEntityCmd>(125, (v,w) => {
        ValidateEntityId(v.EntityId.Value); w.WriteInt32(v.EntityId.Value);
        w.WriteBoolean(v.Rotate);
        w.WriteBoolean(v.Flip);
    }, (r,d) => {
        var EntityId = ReadEntityId(r);
        var Rotate = r.ReadBoolean();
        var Flip = r.ReadBoolean();
        r.EnsureEnd();
        RequireEntity<IEntity>(d, EntityId);
        return new Mafi.Core.Entities.Static.Commands.TryTransformEntityCmd(EntityId, Rotate, Flip);
    }),
    Scalar<Mafi.Core.Entities.Static.QuickRemoveFromEntityCmd>(126, (v,w) => {
        ValidateEntityId(v.EntityId.Value); w.WriteInt32(v.EntityId.Value);
    }, (r,d) => {
        var EntityId = ReadEntityId(r);
        r.EnsureEnd();
        RequireEntity<IEntity>(d, EntityId);
        return new Mafi.Core.Entities.Static.QuickRemoveFromEntityCmd(EntityId);
    }),
    Scalar<Mafi.Core.Entities.ToggleDefaultLogisticsMode>(127, (v,w) => {
    }, (r,d) => {
        r.EnsureEnd();
        return new Mafi.Core.Entities.ToggleDefaultLogisticsMode();
    }),
    Scalar<Mafi.Core.Factory.ElectricPower.SetIsElectricitySurplusConsumerCmd>(128, (v,w) => {
        ValidateEntityId(v.EntityId.Value); w.WriteInt32(v.EntityId.Value);
        w.WriteBoolean(v.IsSurplusConsumer);
    }, (r,d) => {
        var EntityId = ReadEntityId(r);
        var IsSurplusConsumer = r.ReadBoolean();
        r.EnsureEnd();
        RequireEntity<IEntity>(d, EntityId);
        return new Mafi.Core.Factory.ElectricPower.SetIsElectricitySurplusConsumerCmd(EntityId, IsSurplusConsumer);
    }),
    Scalar<Mafi.Core.Factory.ElectricPower.SetIsElectricitySurplusGeneratorCmd>(129, (v,w) => {
        ValidateEntityId(v.EntityId.Value); w.WriteInt32(v.EntityId.Value);
        w.WriteBoolean(v.IsSurplusGenerator);
    }, (r,d) => {
        var EntityId = ReadEntityId(r);
        var IsSurplusGenerator = r.ReadBoolean();
        r.EnsureEnd();
        RequireEntity<IEntity>(d, EntityId);
        return new Mafi.Core.Factory.ElectricPower.SetIsElectricitySurplusGeneratorCmd(EntityId, IsSurplusGenerator);
    }),
    Scalar<Mafi.Core.Factory.Lifts.ReverseLiftCmd>(130, (v,w) => {
        ValidateEntityId(v.LiftId.Value); w.WriteInt32(v.LiftId.Value);
    }, (r,d) => {
        var LiftId = ReadEntityId(r);
        r.EnsureEnd();
        RequireEntity<IEntity>(d, LiftId);
        return new Mafi.Core.Factory.Lifts.ReverseLiftCmd(LiftId);
    }),
    Scalar<Mafi.Core.Factory.Machines.ClearUnusedMachineBuffersCmd>(131, (v,w) => {
        ValidateEntityId(v.MachineId.Value); w.WriteInt32(v.MachineId.Value);
    }, (r,d) => {
        var MachineId = ReadEntityId(r);
        r.EnsureEnd();
        RequireEntity<IEntity>(d, MachineId);
        return new Mafi.Core.Factory.Machines.ClearUnusedMachineBuffersCmd(MachineId);
    }),
    Scalar<Mafi.Core.Factory.Machines.MachineBoostToggleCmd>(132, (v,w) => {
        ValidateEntityId(v.MachineId.Value); w.WriteInt32(v.MachineId.Value);
    }, (r,d) => {
        var MachineId = ReadEntityId(r);
        r.EnsureEnd();
        RequireEntity<IEntity>(d, MachineId);
        return new Mafi.Core.Factory.Machines.MachineBoostToggleCmd(MachineId);
    }),
    Scalar<Mafi.Core.Factory.Machines.MachineToggleRecipeActiveCmd>(133, (v,w) => {
        ValidateEntityId(v.MachineId.Value); w.WriteInt32(v.MachineId.Value);
        ValidateProtoId(v.RecipeId.Value); w.WriteString(v.RecipeId.Value, MaxProtoIdBytes);
    }, (r,d) => {
        var MachineId = ReadEntityId(r);
        var RecipeId = new Mafi.Core.Factory.Recipes.RecipeProto.ID(ReadScalarProtoId(r));
        r.EnsureEnd();
        RequireEntity<IEntity>(d, MachineId);
        RequireResearchTradeProto<Mafi.Core.Factory.Recipes.RecipeProto>(d, RecipeId.Value);
        return new Mafi.Core.Factory.Machines.MachineToggleRecipeActiveCmd(MachineId, RecipeId);
    }),
    Scalar<Mafi.Core.Factory.Machines.WellPumpAlertSetEnabledCmd>(134, (v,w) => {
        ValidateEntityId(v.WellPumpId.Value); w.WriteInt32(v.WellPumpId.Value);
        w.WriteBoolean(v.IsEnabled);
    }, (r,d) => {
        var WellPumpId = ReadEntityId(r);
        var IsEnabled = r.ReadBoolean();
        r.EnsureEnd();
        RequireEntity<IEntity>(d, WellPumpId);
        return new Mafi.Core.Factory.Machines.WellPumpAlertSetEnabledCmd(WellPumpId, IsEnabled);
    }),
    Scalar<Mafi.Core.Factory.NuclearReactors.NuclearReactorToggleAllowedFuelCmd>(135, (v,w) => {
        ValidateEntityId(v.ReactorId.Value); w.WriteInt32(v.ReactorId.Value);
        ValidateProtoId(v.FuelProtoId.Value); w.WriteString(v.FuelProtoId.Value, MaxProtoIdBytes);
    }, (r,d) => {
        var ReactorId = ReadEntityId(r);
        var FuelProtoId = new Mafi.Core.Products.ProductProto.ID(ReadScalarProtoId(r));
        r.EnsureEnd();
        RequireEntity<IEntity>(d, ReactorId);
        RequireResearchTradeProto<Mafi.Core.Products.ProductProto>(d, FuelProtoId.Value);
        return new Mafi.Core.Factory.NuclearReactors.NuclearReactorToggleAllowedFuelCmd(ReactorId, FuelProtoId);
    }),
    Scalar<Mafi.Core.Factory.NuclearReactors.NuclearReactorToggleAutomaticRegulationCmd>(136, (v,w) => {
        ValidateEntityId(v.ReactorId.Value); w.WriteInt32(v.ReactorId.Value);
    }, (r,d) => {
        var ReactorId = ReadEntityId(r);
        r.EnsureEnd();
        RequireEntity<IEntity>(d, ReactorId);
        return new Mafi.Core.Factory.NuclearReactors.NuclearReactorToggleAutomaticRegulationCmd(ReactorId);
    }),
    Scalar<Mafi.Core.Factory.Sorters.SorterToggleProductCmd>(137, (v,w) => {
        ValidateEntityId(v.SorterId.Value); w.WriteInt32(v.SorterId.Value);
        ValidateProtoId(v.ProductId.Value); w.WriteString(v.ProductId.Value, MaxProtoIdBytes);
    }, (r,d) => {
        var SorterId = ReadEntityId(r);
        var ProductId = new Mafi.Core.Products.ProductProto.ID(ReadScalarProtoId(r));
        r.EnsureEnd();
        RequireEntity<IEntity>(d, SorterId);
        RequireResearchTradeProto<Mafi.Core.Products.ProductProto>(d, ProductId.Value);
        return new Mafi.Core.Factory.Sorters.SorterToggleProductCmd(SorterId, ProductId);
    }),
    Scalar<Mafi.Core.Factory.Transports.ClearTransportCmd>(138, (v,w) => {
        ValidateEntityId(v.TransportId.Value); w.WriteInt32(v.TransportId.Value);
    }, (r,d) => {
        var TransportId = ReadEntityId(r);
        r.EnsureEnd();
        RequireEntity<IEntity>(d, TransportId);
        return new Mafi.Core.Factory.Transports.ClearTransportCmd(TransportId);
    }),
    Scalar<Mafi.Core.Factory.Transports.RemoveTransportPillarCmd>(139, (v,w) => {
        ValidateEntityId(v.PillarId.Value); w.WriteInt32(v.PillarId.Value);
    }, (r,d) => {
        var PillarId = ReadEntityId(r);
        r.EnsureEnd();
        RequireEntity<IEntity>(d, PillarId);
        return new Mafi.Core.Factory.Transports.RemoveTransportPillarCmd(PillarId);
    }),
    Scalar<Mafi.Core.Factory.Transports.ReverseTransportCmd>(140, (v,w) => {
        ValidateEntityId(v.TransportId.Value); w.WriteInt32(v.TransportId.Value);
    }, (r,d) => {
        var TransportId = ReadEntityId(r);
        r.EnsureEnd();
        RequireEntity<IEntity>(d, TransportId);
        return new Mafi.Core.Factory.Transports.ReverseTransportCmd(TransportId);
    }),
    Scalar<Mafi.Core.Factory.Zippers.ZipperSetForceEvenInputsCmd>(141, (v,w) => {
        ValidateEntityId(v.ZipperId.Value); w.WriteInt32(v.ZipperId.Value);
        w.WriteBoolean(v.ForceEvenInputs);
    }, (r,d) => {
        var ZipperId = ReadEntityId(r);
        var ForceEvenInputs = r.ReadBoolean();
        r.EnsureEnd();
        RequireEntity<IEntity>(d, ZipperId);
        return new Mafi.Core.Factory.Zippers.ZipperSetForceEvenInputsCmd(ZipperId, ForceEvenInputs);
    }),
    Scalar<Mafi.Core.Factory.Zippers.ZipperSetForceEvenOutputsCmd>(142, (v,w) => {
        ValidateEntityId(v.ZipperId.Value); w.WriteInt32(v.ZipperId.Value);
        w.WriteBoolean(v.ForceEvenOutputs);
    }, (r,d) => {
        var ZipperId = ReadEntityId(r);
        var ForceEvenOutputs = r.ReadBoolean();
        r.EnsureEnd();
        RequireEntity<IEntity>(d, ZipperId);
        return new Mafi.Core.Factory.Zippers.ZipperSetForceEvenOutputsCmd(ZipperId, ForceEvenOutputs);
    }),
    Scalar<Mafi.Core.Fleet.NotifyOnBattleOpenedCmd>(143, (v,w) => {
    }, (r,d) => {
        r.EnsureEnd();
        return new Mafi.Core.Fleet.NotifyOnBattleOpenedCmd();
    }),
    Scalar<Mafi.Core.Maintenance.QuickRepairEntityCmd>(144, (v,w) => {
        ValidateEntityId(v.EntityId.Value); w.WriteInt32(v.EntityId.Value);
    }, (r,d) => {
        var EntityId = ReadEntityId(r);
        r.EnsureEnd();
        RequireEntity<IEntity>(d, EntityId);
        return new Mafi.Core.Maintenance.QuickRepairEntityCmd(EntityId);
    }),
    Scalar<Mafi.Core.Messages.Goals.MarkGoalAsFinishedCmd>(145, (v,w) => {
        ValidateProtoId(v.ProtoId.Value); w.WriteString(v.ProtoId.Value, MaxProtoIdBytes);
        w.WriteBoolean(v.GoalWasSkipped);
    }, (r,d) => {
        var ProtoId = new Mafi.Core.Prototypes.Proto.ID(ReadScalarProtoId(r));
        var GoalWasSkipped = r.ReadBoolean();
        r.EnsureEnd();
        RequireResearchTradeProto<Mafi.Core.Prototypes.Proto>(d, ProtoId.Value);
        return new Mafi.Core.Messages.Goals.MarkGoalAsFinishedCmd(ProtoId, GoalWasSkipped);
    }),
    Scalar<Mafi.Core.Messages.MarkMessageAsReadCmd>(146, (v,w) => {
        ValidateProtoId(v.ProtoId.Value); w.WriteString(v.ProtoId.Value, MaxProtoIdBytes);
    }, (r,d) => {
        var ProtoId = new Mafi.Core.Prototypes.Proto.ID(ReadScalarProtoId(r));
        r.EnsureEnd();
        RequireResearchTradeProto<Mafi.Core.Prototypes.Proto>(d, ProtoId.Value);
        return new Mafi.Core.Messages.MarkMessageAsReadCmd(ProtoId);
    }),
    Scalar<Mafi.Core.Products.PinToggleCmd>(147, (v,w) => {
        ValidateProtoId(v.ProductToToggle.Value); w.WriteString(v.ProductToToggle.Value, MaxProtoIdBytes);
    }, (r,d) => {
        var ProductToToggle = new Mafi.Core.Products.ProductProto.ID(ReadScalarProtoId(r));
        r.EnsureEnd();
        RequireResearchTradeProto<Mafi.Core.Products.ProductProto>(d, ProductToToggle.Value);
        return new Mafi.Core.Products.PinToggleCmd(ProductToToggle);
    }),
    Scalar<Mafi.Core.SpaceProgram.AddLaunchPadCargoBufferCmd>(148, (v,w) => {
        ValidateEntityId(v.LaunchPadId.Value); w.WriteInt32(v.LaunchPadId.Value);
        ValidateProtoId(v.ProductId.Value); w.WriteString(v.ProductId.Value, MaxProtoIdBytes);
    }, (r,d) => {
        var LaunchPadId = ReadEntityId(r);
        var ProductId = new Mafi.Core.Products.ProductProto.ID(ReadScalarProtoId(r));
        r.EnsureEnd();
        RequireEntity<IEntity>(d, LaunchPadId);
        RequireResearchTradeProto<Mafi.Core.Products.ProductProto>(d, ProductId.Value);
        return new Mafi.Core.SpaceProgram.AddLaunchPadCargoBufferCmd(LaunchPadId, ProductId);
    }),
    Scalar<Mafi.Core.SpaceProgram.BuildOrUpgradeSpaceStationCmd>(149, (v,w) => {
    }, (r,d) => {
        r.EnsureEnd();
        return new Mafi.Core.SpaceProgram.BuildOrUpgradeSpaceStationCmd();
    }),
    Scalar<Mafi.Core.SpaceProgram.CancelBuildOrUpgradeOfSpaceStationCmd>(150, (v,w) => {
    }, (r,d) => {
        r.EnsureEnd();
        return new Mafi.Core.SpaceProgram.CancelBuildOrUpgradeOfSpaceStationCmd();
    }),
    Scalar<Mafi.Core.SpaceProgram.DowngradeSpaceStationCmd>(151, (v,w) => {
    }, (r,d) => {
        r.EnsureEnd();
        return new Mafi.Core.SpaceProgram.DowngradeSpaceStationCmd();
    }),
    Scalar<Mafi.Core.SpaceProgram.LaunchRocketCmd>(152, (v,w) => {
        ValidateEntityId(v.LaunchPadId.Value); w.WriteInt32(v.LaunchPadId.Value);
    }, (r,d) => {
        var LaunchPadId = ReadEntityId(r);
        r.EnsureEnd();
        RequireEntity<IEntity>(d, LaunchPadId);
        return new Mafi.Core.SpaceProgram.LaunchRocketCmd(LaunchPadId);
    }),
    Scalar<Mafi.Core.SpaceProgram.RemoveLaunchPadCargoBufferCmd>(153, (v,w) => {
        ValidateEntityId(v.LaunchPadId.Value); w.WriteInt32(v.LaunchPadId.Value);
        ValidateProtoId(v.ProductId.Value); w.WriteString(v.ProductId.Value, MaxProtoIdBytes);
    }, (r,d) => {
        var LaunchPadId = ReadEntityId(r);
        var ProductId = new Mafi.Core.Products.ProductProto.ID(ReadScalarProtoId(r));
        r.EnsureEnd();
        RequireEntity<IEntity>(d, LaunchPadId);
        RequireResearchTradeProto<Mafi.Core.Products.ProductProto>(d, ProductId.Value);
        return new Mafi.Core.SpaceProgram.RemoveLaunchPadCargoBufferCmd(LaunchPadId, ProductId);
    }),
    Scalar<Mafi.Core.SpaceProgram.ScanForNewAsteroidCmd>(154, (v,w) => {
    }, (r,d) => {
        r.EnsureEnd();
        return new Mafi.Core.SpaceProgram.ScanForNewAsteroidCmd();
    }),
    Scalar<Mafi.Core.SpaceProgram.SetRocketAutoLaunchCmd>(155, (v,w) => {
        ValidateEntityId(v.LaunchPadId.Value); w.WriteInt32(v.LaunchPadId.Value);
        w.WriteBoolean(v.AutoLaunch);
    }, (r,d) => {
        var LaunchPadId = ReadEntityId(r);
        var AutoLaunch = r.ReadBoolean();
        r.EnsureEnd();
        RequireEntity<IEntity>(d, LaunchPadId);
        return new Mafi.Core.SpaceProgram.SetRocketAutoLaunchCmd(LaunchPadId, AutoLaunch);
    }),
    Scalar<Mafi.Core.SpaceProgram.ToggleRocketCountdownMuteCmd>(156, (v,w) => {
        ValidateEntityId(v.LaunchPadId.Value); w.WriteInt32(v.LaunchPadId.Value);
    }, (r,d) => {
        var LaunchPadId = ReadEntityId(r);
        r.EnsureEnd();
        RequireEntity<IEntity>(d, LaunchPadId);
        return new Mafi.Core.SpaceProgram.ToggleRocketCountdownMuteCmd(LaunchPadId);
    }),
    Scalar<Mafi.Core.SpaceProgram.ToggleSpaceStationCrewEnabledCmd>(157, (v,w) => {
    }, (r,d) => {
        r.EnsureEnd();
        return new Mafi.Core.SpaceProgram.ToggleSpaceStationCrewEnabledCmd();
    }),
    Scalar<Mafi.Core.Trains.CreateNewTrainLineCmd>(158, (v,w) => {
    }, (r,d) => {
        r.EnsureEnd();
        return new Mafi.Core.Trains.CreateNewTrainLineCmd();
    }),
    Scalar<Mafi.Core.Trains.CreateNewTrainNetworkCmd>(159, (v,w) => {
    }, (r,d) => {
        r.EnsureEnd();
        return new Mafi.Core.Trains.CreateNewTrainNetworkCmd();
    }),
    Scalar<Mafi.Core.Trains.QuickBuildCurrentTrainCmd>(160, (v,w) => {
        ValidateEntityId(v.DepotId.Value); w.WriteInt32(v.DepotId.Value);
    }, (r,d) => {
        var DepotId = ReadEntityId(r);
        r.EnsureEnd();
        RequireEntity<IEntity>(d, DepotId);
        return new Mafi.Core.Trains.QuickBuildCurrentTrainCmd(DepotId);
    }),
    Scalar<Mafi.Core.Trains.RemoveTrainTrackPillarCmd>(161, (v,w) => {
        ValidateEntityId(v.PillarId.Value); w.WriteInt32(v.PillarId.Value);
    }, (r,d) => {
        var PillarId = ReadEntityId(r);
        r.EnsureEnd();
        RequireEntity<IEntity>(d, PillarId);
        return new Mafi.Core.Trains.RemoveTrainTrackPillarCmd(PillarId);
    }),
    Scalar<Mafi.Core.Trains.SetArrivalRuleAlwaysAllowCmd>(162, (v,w) => {
        ValidateEntityId(v.StationId.Value); w.WriteInt32(v.StationId.Value);
        ValidateProtoId(v.ProductId.Value); w.WriteString(v.ProductId.Value, MaxProtoIdBytes);
        w.WriteBoolean(v.AlwaysAllow);
    }, (r,d) => {
        var StationId = ReadEntityId(r);
        var ProductId = new Mafi.Core.Products.ProductProto.ID(ReadScalarProtoId(r));
        var AlwaysAllow = r.ReadBoolean();
        r.EnsureEnd();
        RequireEntity<IEntity>(d, StationId);
        RequireResearchTradeProto<Mafi.Core.Products.ProductProto>(d, ProductId.Value);
        return new Mafi.Core.Trains.SetArrivalRuleAlwaysAllowCmd(StationId, ProductId, AlwaysAllow);
    }),
    Scalar<Mafi.Core.Trains.SetDoNotSkipThisTrainStationCmd>(163, (v,w) => {
        ValidateEntityId(v.RootStationId.Value); w.WriteInt32(v.RootStationId.Value);
        w.WriteBoolean(v.DoNotSkip);
    }, (r,d) => {
        var RootStationId = ReadEntityId(r);
        var DoNotSkip = r.ReadBoolean();
        r.EnsureEnd();
        RequireEntity<IEntity>(d, RootStationId);
        return new Mafi.Core.Trains.SetDoNotSkipThisTrainStationCmd(RootStationId, DoNotSkip);
    }),
    Scalar<Mafi.Core.Trains.SetStationRequireFullServiceOnArrivalCmd>(164, (v,w) => {
        ValidateEntityId(v.StationId.Value); w.WriteInt32(v.StationId.Value);
        ValidateProtoId(v.ProductId.Value); w.WriteString(v.ProductId.Value, MaxProtoIdBytes);
        w.WriteBoolean(v.RequireFullTransfer);
    }, (r,d) => {
        var StationId = ReadEntityId(r);
        var ProductId = new Mafi.Core.Products.ProductProto.ID(ReadScalarProtoId(r));
        var RequireFullTransfer = r.ReadBoolean();
        r.EnsureEnd();
        RequireEntity<IEntity>(d, StationId);
        RequireResearchTradeProto<Mafi.Core.Products.ProductProto>(d, ProductId.Value);
        return new Mafi.Core.Trains.SetStationRequireFullServiceOnArrivalCmd(StationId, ProductId, RequireFullTransfer);
    }),
    Scalar<Mafi.Core.Trains.ToggleTrainStationAllowedFuelCmd>(165, (v,w) => {
        ValidateEntityId(v.RootStationId.Value); w.WriteInt32(v.RootStationId.Value);
        ValidateProtoId(v.FuelId.Value); w.WriteString(v.FuelId.Value, MaxProtoIdBytes);
    }, (r,d) => {
        var RootStationId = ReadEntityId(r);
        var FuelId = new Mafi.Core.Products.ProductProto.ID(ReadScalarProtoId(r));
        r.EnsureEnd();
        RequireEntity<IEntity>(d, RootStationId);
        RequireResearchTradeProto<Mafi.Core.Products.ProductProto>(d, FuelId.Value);
        return new Mafi.Core.Trains.ToggleTrainStationAllowedFuelCmd(RootStationId, FuelId);
    }),
    Scalar<Mafi.Core.Trains.ToggleTrainStationStorageLinkCmd>(166, (v,w) => {
        ValidateEntityId(v.StationId.Value); w.WriteInt32(v.StationId.Value);
        ValidateEntityId(v.StorageId.Value); w.WriteInt32(v.StorageId.Value);
    }, (r,d) => {
        var StationId = ReadEntityId(r);
        var StorageId = ReadEntityId(r);
        r.EnsureEnd();
        RequireEntity<IEntity>(d, StationId);
        RequireEntity<IEntity>(d, StorageId);
        return new Mafi.Core.Trains.ToggleTrainStationStorageLinkCmd(StationId, StorageId);
    }),
    Scalar<Mafi.Core.Trains.TrainStationModuleClearProductCmd>(167, (v,w) => {
        ValidateEntityId(v.ModuleId.Value); w.WriteInt32(v.ModuleId.Value);
    }, (r,d) => {
        var ModuleId = ReadEntityId(r);
        r.EnsureEnd();
        RequireEntity<IEntity>(d, ModuleId);
        return new Mafi.Core.Trains.TrainStationModuleClearProductCmd(ModuleId);
    }),
    Scalar<Mafi.Core.Trains.TrainStationModuleQuickRemoveProductCmd>(168, (v,w) => {
        ValidateEntityId(v.ModuleId.Value); w.WriteInt32(v.ModuleId.Value);
    }, (r,d) => {
        var ModuleId = ReadEntityId(r);
        r.EnsureEnd();
        RequireEntity<IEntity>(d, ModuleId);
        return new Mafi.Core.Trains.TrainStationModuleQuickRemoveProductCmd(ModuleId);
    }),
    Scalar<Mafi.Core.Trains.TrainStationModuleSetProductCmd>(169, (v,w) => {
        ValidateEntityId(v.ModuleId.Value); w.WriteInt32(v.ModuleId.Value);
        ValidateProtoId(v.ProductId.Value); w.WriteString(v.ProductId.Value, MaxProtoIdBytes);
    }, (r,d) => {
        var ModuleId = ReadEntityId(r);
        var ProductId = new Mafi.Core.Products.ProductProto.ID(ReadScalarProtoId(r));
        r.EnsureEnd();
        RequireEntity<IEntity>(d, ModuleId);
        RequireResearchTradeProto<Mafi.Core.Products.ProductProto>(d, ProductId.Value);
        return new Mafi.Core.Trains.TrainStationModuleSetProductCmd(ModuleId, ProductId);
    }),
    Scalar<Mafi.Core.Vehicles.Commands.CancelReplaceVehicleCmd>(170, (v,w) => {
        ValidateEntityId(v.VehicleId.Value); w.WriteInt32(v.VehicleId.Value);
    }, (r,d) => {
        var VehicleId = ReadEntityId(r);
        r.EnsureEnd();
        RequireEntity<IEntity>(d, VehicleId);
        return new Mafi.Core.Vehicles.Commands.CancelReplaceVehicleCmd(VehicleId);
    }),
    Scalar<Mafi.Core.Vehicles.Commands.DiscardVehicleCargoCmd>(171, (v,w) => {
        ValidateEntityId(v.VehicleId.Value); w.WriteInt32(v.VehicleId.Value);
    }, (r,d) => {
        var VehicleId = ReadEntityId(r);
        r.EnsureEnd();
        RequireEntity<IEntity>(d, VehicleId);
        return new Mafi.Core.Vehicles.Commands.DiscardVehicleCargoCmd(VehicleId);
    }),
    Scalar<Mafi.Core.Vehicles.Commands.QuickBuildCurrentVehicleCmd>(172, (v,w) => {
        ValidateEntityId(v.VehicleDepotId.Value); w.WriteInt32(v.VehicleDepotId.Value);
    }, (r,d) => {
        var VehicleDepotId = ReadEntityId(r);
        r.EnsureEnd();
        RequireEntity<IEntity>(d, VehicleDepotId);
        return new Mafi.Core.Vehicles.Commands.QuickBuildCurrentVehicleCmd(VehicleDepotId);
    }),
    Scalar<Mafi.Core.Vehicles.Commands.RecoverVehicleCmd>(173, (v,w) => {
        ValidateEntityId(v.VehicleId.Value); w.WriteInt32(v.VehicleId.Value);
    }, (r,d) => {
        var VehicleId = ReadEntityId(r);
        r.EnsureEnd();
        RequireEntity<IEntity>(d, VehicleId);
        return new Mafi.Core.Vehicles.Commands.RecoverVehicleCmd(VehicleId);
    }),
    Scalar<Mafi.Core.Vehicles.Commands.TogglePartialTrucksLoadCmd>(174, (v,w) => {
    }, (r,d) => {
        r.EnsureEnd();
        return new Mafi.Core.Vehicles.Commands.TogglePartialTrucksLoadCmd();
    }),
    Scalar<Mafi.Core.Vehicles.Commands.ToggleVehicleScrapCmd>(175, (v,w) => {
        ValidateEntityId(v.VehicleId.Value); w.WriteInt32(v.VehicleId.Value);
    }, (r,d) => {
        var VehicleId = ReadEntityId(r);
        r.EnsureEnd();
        RequireEntity<IEntity>(d, VehicleId);
        return new Mafi.Core.Vehicles.Commands.ToggleVehicleScrapCmd(VehicleId);
    }),
    Scalar<Mafi.Core.Vehicles.Commands.UnassignVehicleCmd>(176, (v,w) => {
        ValidateEntityId(v.VehicleId.Value); w.WriteInt32(v.VehicleId.Value);
    }, (r,d) => {
        var VehicleId = ReadEntityId(r);
        r.EnsureEnd();
        RequireEntity<IEntity>(d, VehicleId);
        return new Mafi.Core.Vehicles.Commands.UnassignVehicleCmd(VehicleId);
    }),
    Scalar<Mafi.Core.Vehicles.CreateNewLogisticsZoneCmd>(177, (v,w) => {
    }, (r,d) => {
        r.EnsureEnd();
        return new Mafi.Core.Vehicles.CreateNewLogisticsZoneCmd();
    }),
    Scalar<Mafi.Core.World.FleetModificationsCancelCmd>(178, (v,w) => {
    }, (r,d) => {
        r.EnsureEnd();
        return new Mafi.Core.World.FleetModificationsCancelCmd();
    }),
    Scalar<Mafi.Core.World.FleetToggleAutoReturnCmd>(179, (v,w) => {
    }, (r,d) => {
        r.EnsureEnd();
        return new Mafi.Core.World.FleetToggleAutoReturnCmd();
    }),
    Scalar<Mafi.Core.World.FleetUnloadCrewCmd>(180, (v,w) => {
    }, (r,d) => {
        r.EnsureEnd();
        return new Mafi.Core.World.FleetUnloadCrewCmd();
    }),
    Scalar<Mafi.Core.World.FleetUnloadFuelCmd>(181, (v,w) => {
    }, (r,d) => {
        r.EnsureEnd();
        return new Mafi.Core.World.FleetUnloadFuelCmd();
    }),
    Scalar<Mafi.Base.Prototypes.Buildings.ThermalStorages.ThermalStorageSetProductCmd>(182, (v,w) => {
        ValidateEntityId(v.StorageId.Value); w.WriteInt32(v.StorageId.Value);
        ValidateProtoId(v.ProductId.Value); w.WriteString(v.ProductId.Value, MaxProtoIdBytes);
    }, (r,d) => {
        var StorageId = ReadEntityId(r);
        var ProductId = new Mafi.Core.Products.ProductProto.ID(ReadScalarProtoId(r));
        r.EnsureEnd();
        RequireEntity<IEntity>(d, StorageId);
        RequireResearchTradeProto<Mafi.Core.Products.ProductProto>(d, ProductId.Value);
        return new Mafi.Base.Prototypes.Buildings.ThermalStorages.ThermalStorageSetProductCmd(RequireEntity<Mafi.Base.Prototypes.Buildings.ThermalStorages.ThermalStorage>(d, StorageId), RequireResearchTradeProto<Mafi.Core.Products.ProductProto>(d, ProductId.Value));
    }),
    Scalar<Mafi.Core.Buildings.Cargo.Modules.CargoDepotModuleClearProductCmd>(183, (v,w) => {
        ValidateEntityId(v.ModuleId.Value); w.WriteInt32(v.ModuleId.Value);
    }, (r,d) => {
        var ModuleId = ReadEntityId(r);
        r.EnsureEnd();
        RequireEntity<IEntity>(d, ModuleId);
        return new Mafi.Core.Buildings.Cargo.Modules.CargoDepotModuleClearProductCmd(RequireEntity<Mafi.Core.Buildings.Cargo.Modules.CargoDepotModule>(d, ModuleId));
    }),
    Scalar<Mafi.Core.Buildings.Cargo.Modules.CargoDepotModuleSetProductCmd>(184, (v,w) => {
        ValidateEntityId(v.ModuleId.Value); w.WriteInt32(v.ModuleId.Value);
        ValidateProtoId(v.ProductId.Value); w.WriteString(v.ProductId.Value, MaxProtoIdBytes);
    }, (r,d) => {
        var ModuleId = ReadEntityId(r);
        var ProductId = new Mafi.Core.Products.ProductProto.ID(ReadScalarProtoId(r));
        r.EnsureEnd();
        RequireEntity<IEntity>(d, ModuleId);
        RequireResearchTradeProto<Mafi.Core.Products.ProductProto>(d, ProductId.Value);
        return new Mafi.Core.Buildings.Cargo.Modules.CargoDepotModuleSetProductCmd(RequireEntity<Mafi.Core.Buildings.Cargo.Modules.CargoDepotModule>(d, ModuleId), RequireResearchTradeProto<Mafi.Core.Products.ProductProto>(d, ProductId.Value));
    }),
    Scalar<Mafi.Core.Buildings.OreSorting.SortingPlantNoSingleProductCmd>(185, (v,w) => {
        ValidateEntityId(v.SortingPlantId.Value); w.WriteInt32(v.SortingPlantId.Value);
        w.WriteBoolean(v.DoNotAcceptSingleProduct);
    }, (r,d) => {
        var SortingPlantId = ReadEntityId(r);
        var DoNotAcceptSingleProduct = r.ReadBoolean();
        r.EnsureEnd();
        RequireEntity<IEntity>(d, SortingPlantId);
        return new Mafi.Core.Buildings.OreSorting.SortingPlantNoSingleProductCmd(RequireEntity<Mafi.Core.Buildings.OreSorting.OreSortingPlant>(d, SortingPlantId), DoNotAcceptSingleProduct);
    }),
    Scalar<Mafi.Core.Buildings.OreSorting.ToggleSorterAutoAssignBuffers>(186, (v,w) => {
        ValidateEntityId(v.SortingPlantId.Value); w.WriteInt32(v.SortingPlantId.Value);
    }, (r,d) => {
        var SortingPlantId = ReadEntityId(r);
        r.EnsureEnd();
        RequireEntity<IEntity>(d, SortingPlantId);
        return new Mafi.Core.Buildings.OreSorting.ToggleSorterAutoAssignBuffers(RequireEntity<Mafi.Core.Buildings.OreSorting.OreSortingPlant>(d, SortingPlantId));
    }),
    Scalar<Mafi.Core.Buildings.Storages.StorageClearProductCmd>(187, (v,w) => {
        ValidateEntityId(v.StorageId.Value); w.WriteInt32(v.StorageId.Value);
    }, (r,d) => {
        var StorageId = ReadEntityId(r);
        r.EnsureEnd();
        RequireEntity<IEntity>(d, StorageId);
        return new Mafi.Core.Buildings.Storages.StorageClearProductCmd(RequireEntity<Mafi.Core.Buildings.Storages.Storage>(d, StorageId));
    }),
};
private sealed class ScalarCommandSpec
{
    internal ushort Id = 0;
    internal Type Type = null!;
    internal Action<IInputCommand, PayloadWriter> Write = null!;
    internal Func<PayloadReader, DependencyResolver, IInputCommand> Read = null!;
}
private static ScalarCommandSpec Scalar<T>(ushort id, Action<T, PayloadWriter> write, Func<PayloadReader,DependencyResolver,T> read) where T : IInputCommand
    => new ScalarCommandSpec { Id=id, Type=typeof(T), Write=(v,w)=>write((T)v,w), Read=(r,d)=>read(r,d) };
private static string ReadScalarProtoId(PayloadReader reader) { string id=reader.ReadString(MaxProtoIdBytes); ValidateProtoId(id); return id; }
private static bool TryEncodeScalarCommand(IInputCommand command, PayloadWriter writer)
{
    foreach (var schema in ScalarCommands)
        if (command.GetType() == schema.Type) { writer.WriteUInt16(schema.Id); schema.Write(command, writer); return true; }
    return false;
}
private static IInputCommand? DecodeScalarCommand(GameCommandKind kind, PayloadReader reader, DependencyResolver resolver)
{
    foreach (var schema in ScalarCommands) if (schema.Id == (ushort)kind) return schema.Read(reader, resolver);
    return null;
}
}}
