using CoiDedicated.Protocol;
using Mafi;
using Mafi.Core;
using Mafi.Core.Buildings.VehicleDepots;
using Mafi.Core.Entities.Dynamic;
using Mafi.Core.Input;
using Mafi.Core.Prototypes;
using Mafi.Core.Terrain;
using Mafi.Core.Vehicles;
using Mafi.Core.Vehicles.Commands;

namespace CoiDedicated.GameAdapter
{
    public static partial class GameCommandCodec
    {
        private static bool TryEncodeLogistics(IInputCommand command, PayloadWriter w)
        {
            var type = command.GetType();
            if (type == typeof(ChangeLogisticsZoneAreaCmd))
            { var v = (ChangeLogisticsZoneAreaCmd)command; w.WriteUInt16(188); WriteZone(w, v.ZoneId); WriteWorkArea(w, v.Area); }
            else if (type == typeof(RemoveLogisticsZoneCmd))
            { w.WriteUInt16(189); WriteZone(w, ((RemoveLogisticsZoneCmd)command).ZoneId); }
            else if (type == typeof(RenameLogisticsZoneCmd))
            { var v = (RenameLogisticsZoneCmd)command; w.WriteUInt16(190); WriteZone(w, v.ZoneId); ValidateEntityTitle(v.Name); w.WriteString(v.Name, 256); }
            else if (type == typeof(SetLogisticsZoneColorCmd))
            {
                var v = (SetLogisticsZoneColorCmd)command; w.WriteUInt16(191); WriteZone(w, v.ZoneId);
                ValidateRange(v.ColorIndex, 0, LogisticsZone.COLOR_PALETTE.Length - 1, "Zone color"); w.WriteInt32(v.ColorIndex);
            }
            else if (type == typeof(SetVehicleLogisticsZoneCmd))
            { var v = (SetVehicleLogisticsZoneCmd)command; WriteManagementEntity(w, 192, v.TruckId); WriteOptionalZone(w, v.ZoneId); }
            else if (type == typeof(ToggleLogisticsZoneConstructionCmd))
            { var v = (ToggleLogisticsZoneConstructionCmd)command; w.WriteUInt16(193); WriteZone(w, v.ZoneId); WriteZone(w, v.ConstructionZoneId); }
            else if (type == typeof(TruckJobsAllowCmd))
            { var v = (TruckJobsAllowCmd)command; w.WriteUInt16(194); WriteTruckGroup(w, v.TruckProtoId, v.ZoneId); }
            else if (type == typeof(TruckJobsForbidCmd))
            { var v = (TruckJobsForbidCmd)command; w.WriteUInt16(195); WriteTruckGroup(w, v.TruckProtoId, v.ZoneId); }
            else if (type == typeof(TruckConstructionRuleSetCmd))
            {
                var v = (TruckConstructionRuleSetCmd)command; w.WriteUInt16(196); WriteTruckGroup(w, v.TruckProtoId, v.ZoneId);
                ValidateRange((int)v.Rule, 0, 2, "Truck construction rule"); w.WriteUInt16((byte)v.Rule);
            }
            else if (type == typeof(SetVehicleDepotTargetZone))
            { var v = (SetVehicleDepotTargetZone)command; WriteManagementEntity(w, 197, v.VehicleDepotId); WriteOptionalZone(w, v.ZoneId); }
            else return false;
            return true;
        }
        private static IInputCommand? DecodeLogistics(GameCommandKind kind, PayloadReader r, DependencyResolver d)
        {
            switch ((ushort)kind)
            {
                case 188:
                {
                    var zone = ReadZone(r); var area = ReadWorkArea(r); r.EnsureEnd(); RequireZone(d, zone);
                    if (!area.IsEmpty) { RequireMapTile(d, area.BoundingBoxMin); RequireMapTile(d, new Tile2i(area.BoundingBoxMax.X - 1, area.BoundingBoxMax.Y - 1)); }
                    return new ChangeLogisticsZoneAreaCmd(zone, area);
                }
                case 189:
                { var zone = ReadZone(r); r.EnsureEnd(); RequireZone(d, zone); return new RemoveLogisticsZoneCmd(zone); }
                case 190:
                { var zone = ReadZone(r); string name = r.ReadString(256); r.EnsureEnd(); ValidateEntityTitle(name); RequireZone(d, zone); return new RenameLogisticsZoneCmd(zone, name); }
                case 191:
                {
                    var zone = ReadZone(r); int color = r.ReadInt32(); r.EnsureEnd();
                    ValidateRange(color, 0, LogisticsZone.COLOR_PALETTE.Length - 1, "Zone color"); RequireZone(d, zone);
                    return new SetLogisticsZoneColorCmd(zone, color);
                }
                case 192:
                case 197:
                {
                    var id = ReadEntityId(r); var zone = ReadOptionalZone(r); r.EnsureEnd(); if (zone.HasValue) RequireZone(d, zone.Value);
                    if ((ushort)kind == 192) { RequireEntity<Vehicle>(d, id); return new SetVehicleLogisticsZoneCmd(zone, id); }
                    RequireEntity<VehicleDepotBase>(d, id); return new SetVehicleDepotTargetZone(id, zone);
                }
                case 193:
                {
                    var zone = ReadZone(r); var other = ReadZone(r); r.EnsureEnd(); RequireZone(d, zone); RequireZone(d, other);
                    return new ToggleLogisticsZoneConstructionCmd(zone, other);
                }
                case 194:
                case 195:
                case 196:
                {
                    string proto = ReadScalarProtoId(r); var zone = ReadZone(r); int rule = (ushort)kind == 196 ? r.ReadUInt16() : 0;
                    r.EnsureEnd(); ValidateRange(rule, 0, 2, "Truck construction rule"); RequireZone(d, zone);
                    RequireResearchTradeProto<VehicleGroupProto>(d, proto); var id = new Proto.ID(proto);
                    if ((ushort)kind == 194) return new TruckJobsAllowCmd(id, zone);
                    if ((ushort)kind == 195) return new TruckJobsForbidCmd(id, zone);
                    return new TruckConstructionRuleSetCmd(id, zone, (TruckConstructionRule)rule);
                }
                default: return null;
            }
        }
        private static void WriteZone(PayloadWriter w, LogisticsZoneId zone)
        { ValidateRange(zone.Value, 0, int.MaxValue, "Zone ID"); w.WriteInt32(zone.Value); }
        private static LogisticsZoneId ReadZone(PayloadReader r)
        { int id = r.ReadInt32(); ValidateRange(id, 0, int.MaxValue, "Zone ID"); return new LogisticsZoneId(id); }
        private static void WriteOptionalZone(PayloadWriter w, LogisticsZoneId? zone)
        { w.WriteBoolean(zone.HasValue); if (zone.HasValue) WriteZone(w, zone.Value); }
        private static LogisticsZoneId? ReadOptionalZone(PayloadReader r) => r.ReadBoolean() ? ReadZone(r) : (LogisticsZoneId?)null;
        private static void RequireZone(DependencyResolver d, LogisticsZoneId id)
        { if (!d.Resolve<LogisticsZonesManager>().TryGetZone(id, out _)) throw new ProtocolException("Logistics zone does not exist."); }
        private static void WriteTruckGroup(PayloadWriter w, Proto.ID proto, LogisticsZoneId zone)
        { ValidateProtoId(proto.Value); w.WriteString(proto.Value, MaxProtoIdBytes); WriteZone(w, zone); }
    }
}
