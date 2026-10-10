using System;
using CoiDedicated.Protocol;
using Mafi;
using Mafi.Core;
using Mafi.Core.Buildings.Farms;
using Mafi.Core.Buildings.Storages;
using Mafi.Core.Buildings.VehicleDepots;
using Mafi.Core.Entities;
using Mafi.Core.Entities.Commands;
using Mafi.Core.Entities.Dynamic;
using Mafi.Core.Entities.Static;
using Mafi.Core.Entities.Static.Commands;
using Mafi.Core.Input;
using Mafi.Core.Prototypes;
using Mafi.Core.Vehicles.Commands;

namespace CoiDedicated.GameAdapter
{
    public static partial class GameCommandCodec
    {
        private static bool TryEncodeManagement(IInputCommand command, PayloadWriter writer)
        {
            Type type=command.GetType();
            if (type==typeof(StorageSetSliderStepCmd))
            {
                var v=(StorageSetSliderStepCmd)command; WriteManagementEntity(writer,70,v.StorageId);
                foreach(int step in new[]{v.ImportStep,v.ExportStep,v.TransportFrom,v.TransportUntil})
                { ValidateRange(step,-1,10,"Storage slider"); writer.WriteInt32(step); }
            }
            else if(type==typeof(FarmAssignCropCmd))
            {
                var v=(FarmAssignCropCmd)command; WriteManagementEntity(writer,71,v.FarmId);
                WriteOptionalProto(writer,v.CropId?.Value); ValidateRange(v.ScheduleSlot,0,255,"Crop slot"); writer.WriteInt32(v.ScheduleSlot);
            }
            else if(type==typeof(SetGeneralPriorityCmd))
            {
                var v=(SetGeneralPriorityCmd)command; WriteManagementEntity(writer,72,v.EntityId);
                ValidatePriority(v.Priority); writer.WriteInt32(v.Priority);
            }
            else if(type==typeof(AddVehicleToBuildQueueCmd))
            {
                var v=(AddVehicleToBuildQueueCmd)command; WriteManagementEntity(writer,73,v.VehicleDepotId);
                ValidateProtoId(v.ProtoId.Value); writer.WriteString(v.ProtoId.Value,MaxProtoIdBytes);
                ValidateRange(v.Count,1,128,"Vehicle batch"); writer.WriteInt32(v.Count);
            }
            else if(type==typeof(AssignVehicleToEntityCmd))
            { var v=(AssignVehicleToEntityCmd)command; WriteManagementPair(writer,74,v.VehicleId,v.EntityId); }
            else if(type==typeof(AssignStaticEntityCmd))
            { var v=(AssignStaticEntityCmd)command; WriteManagementPair(writer,75,v.FirstEntityId,v.SecondEntityId); }
            else if(type==typeof(UnassignStaticEntityCmd))
            { var v=(UnassignStaticEntityCmd)command; WriteManagementPair(writer,76,v.FirstEntityId,v.SecondEntityId); }
            else if(type==typeof(AssignVehicleTypeToEntityCmd))
            {
                var v=(AssignVehicleTypeToEntityCmd)command;
                WriteVehicleTypeAssignment(writer,GameCommandKind.AssignVehicleTypeToEntity,v.EntityId,v.VehicleId,v.Count,v.ZoneId);
            }
            else if(type==typeof(UnassignVehicleFromEntityCmd))
            {
                var v=(UnassignVehicleFromEntityCmd)command;
                WriteVehicleTypeAssignment(writer,GameCommandKind.UnassignVehicleFromEntity,v.EntityId,v.VehicleId,v.Count,v.ZoneToAssign);
            }
            else if(type==typeof(SetEntityNameCmd))
            {
                var v=(SetEntityNameCmd)command; WriteManagementEntity(writer,77,v.EntityId);
                ValidateEntityTitle(v.Title); writer.WriteString(v.Title,256);
            }
            else if(type==typeof(SetConstructionPausedCmd))
            {
                var v=(SetConstructionPausedCmd)command; WriteManagementEntity(writer,78,v.EntityId);
                writer.WriteBoolean(v.IsPaused.HasValue); if(v.IsPaused.HasValue)writer.WriteBoolean(v.IsPaused.Value);
            }
            else return false;
            return true;
        }
        private static IInputCommand? DecodeManagement(GameCommandKind kind,PayloadReader reader,DependencyResolver resolver)
        {
            if((ushort)kind<70 || (ushort)kind>80)return null;
            EntityId id=ReadEntityId(reader);
            switch((ushort)kind)
            {
                case 70:
                    int import=reader.ReadInt32(),export=reader.ReadInt32(),from=reader.ReadInt32(),until=reader.ReadInt32(); reader.EnsureEnd();
                    foreach(int step in new[]{import,export,from,until})ValidateRange(step,-1,10,"Storage slider");
                    RequireEntity<Storage>(resolver,id); return new StorageSetSliderStepCmd(id,(int?)import,(int?)export,(int?)from,(int?)until);
                case 71:
                    string? crop=ReadOptionalProto(reader); int slot=reader.ReadInt32(); reader.EnsureEnd();
                    ValidateRange(slot,0,255,"Crop slot"); Farm farm=RequireEntity<Farm>(resolver,id);
                    if(slot>=farm.Schedule.Length)throw new ProtocolException("Crop slot is outside this farm's schedule.");
                    if(crop!=null)RequireResearchTradeProto<CropProto>(resolver,crop);
                    return new FarmAssignCropCmd(id,crop==null?(Proto.ID?)null:new Proto.ID(crop),slot);
                case 72:
                    int priority=reader.ReadInt32(); reader.EnsureEnd(); ValidatePriority(priority); RequireEntity<IEntity>(resolver,id);
                    return new SetGeneralPriorityCmd(id,priority);
                case 73:
                    string proto=ReadScalarProtoId(reader); int count=reader.ReadInt32(); reader.EnsureEnd(); ValidateRange(count,1,128,"Vehicle batch");
                    var vehicle=RequireResearchTradeProto<DrivingEntityProto>(resolver,proto);
                    if(!vehicle.IsUnlockedAndAvailable)throw new ProtocolException("Vehicle research is locked.");
                    var depot=RequireEntity<VehicleDepotBase>(resolver,id);
                    if(!depot.Prototype.BuildableEntities.Contains(vehicle))throw new ProtocolException("Vehicle is not supported by this depot.");
                    return new AddVehicleToBuildQueueCmd(new DynamicEntityProto.ID(proto),id,count);
                case 74:
                    var target=ReadEntityId(reader); reader.EnsureEnd(); RequireEntity<Vehicle>(resolver,id); RequireEntity<IEntityAssignedWithVehicles>(resolver,target);
                    return new AssignVehicleToEntityCmd(id,target);
                case 75:
                case 76:
                    var second=ReadEntityId(reader); reader.EnsureEnd();
                    var output=RequireEntity<IEntityAssignedAsOutput>(resolver,id); var input=RequireEntity<IEntityAssignedAsInput>(resolver,second);
                    return (ushort)kind==75?new AssignStaticEntityCmd(output,input):new UnassignStaticEntityCmd(output,input);
                case 77:
                    string title=reader.ReadString(256); reader.EnsureEnd(); ValidateEntityTitle(title); RequireEntity<IEntity>(resolver,id);
                    return new SetEntityNameCmd(id,title);
                case 78:
                    bool? paused=reader.ReadBoolean()?reader.ReadBoolean():(bool?)null; reader.EnsureEnd(); RequireEntity<StaticEntity>(resolver,id);
                    return new SetConstructionPausedCmd(id,paused);
                case 79:
                case 80:
                {
                    string vehicleProtoId=ReadScalarProtoId(reader);
                    int vehicleCount=reader.ReadInt32();
                    ValidateRange(vehicleCount,1,128,"Vehicle assignment count");
                    LogisticsZoneId? zone=null;
                    if(reader.ReadBoolean())
                    {
                        int zoneId=reader.ReadInt32();
                        ValidateRange(zoneId,0,int.MaxValue,"Logistics zone ID");
                        zone=new LogisticsZoneId(zoneId);
                    }
                    reader.EnsureEnd();
                    RequireResearchTradeProto<DynamicEntityProto>(resolver,vehicleProtoId);
                    RequireEntity<IEntityAssignedWithVehicles>(resolver,id);
                    // Keep native vehicle selection and zone handling on the ordered simulation tick.
                    // Zone IDs are monotonically allocated IDs, not indices into the 64 active slots.
                    var vehicleType=new DynamicEntityProto.ID(vehicleProtoId);
                    return kind==GameCommandKind.AssignVehicleTypeToEntity
                        ? new AssignVehicleTypeToEntityCmd(vehicleType,id,vehicleCount,zone)
                        : new UnassignVehicleFromEntityCmd(vehicleType,id,vehicleCount,zone);
                }
                default:return null;
            }
        }
        private static void WriteManagementEntity(PayloadWriter writer,ushort kind,EntityId id)
        { ValidateEntityId(id.Value); writer.WriteUInt16(kind); writer.WriteInt32(id.Value); }
        private static void WriteManagementPair(PayloadWriter writer,ushort kind,EntityId first,EntityId second)
        { WriteManagementEntity(writer,kind,first); ValidateEntityId(second.Value); writer.WriteInt32(second.Value); }
        private static void WriteVehicleTypeAssignment(PayloadWriter writer,GameCommandKind kind,EntityId entity,
            DynamicEntityProto.ID vehicle,int count,LogisticsZoneId? zone)
        {
            ValidateProtoId(vehicle.Value);
            ValidateRange(count,1,128,"Vehicle assignment count");
            if(zone.HasValue)ValidateRange(zone.Value.Value,0,int.MaxValue,"Logistics zone ID");
            WriteManagementEntity(writer,(ushort)kind,entity);
            writer.WriteString(vehicle.Value,MaxProtoIdBytes);
            writer.WriteInt32(count);
            writer.WriteBoolean(zone.HasValue);
            if(zone.HasValue)writer.WriteInt32(zone.Value.Value);
        }
        private static void ValidateRange(int value,int min,int max,string label)
        { if(value<min || value>max)throw new ProtocolException($"{label} must be between {min} and {max}."); }
        private static void WriteOptionalProto(PayloadWriter writer,string? id)
        { writer.WriteBoolean(id!=null); if(id!=null){ValidateProtoId(id);writer.WriteString(id,MaxProtoIdBytes);} }
        private static string? ReadOptionalProto(PayloadReader reader)=>reader.ReadBoolean()?ReadScalarProtoId(reader):null;
        private static void ValidateEntityTitle(string title)
        {
            if(title==null || Utf8.GetByteCount(title)>256)throw new ProtocolException("Entity name exceeds 256 UTF-8 bytes.");
            foreach(char c in title)if(char.IsControl(c))throw new ProtocolException("Entity name contains a control character.");
        }
    }
}
