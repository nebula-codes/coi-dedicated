using System;
using CoiDedicated.Protocol;
using Mafi;
using Mafi.Collections.ImmutableCollections;
using Mafi.Core.Input;
using Mafi.Core.Prototypes;
using Mafi.Core.Terrain.Designation;

namespace CoiDedicated.GameAdapter
{
    public static partial class GameCommandCodec
    {
        public const int MaxDesignations = 1024;

        private static bool TryEncodeTerrain(IInputCommand command, PayloadWriter writer)
        {
            if (command.GetType() == typeof(AddTerrainDesignationsCmd))
            {
                var value = (AddTerrainDesignationsCmd)command;
                ValidateProtoId(value.ProtoId.Value); ValidateDesignationCount(value.Data.Length);
                writer.WriteUInt16(60); writer.WriteString(value.ProtoId.Value, MaxProtoIdBytes);
                writer.WriteUInt16((ushort)value.Data.Length);
                foreach (DesignationData d in value.Data)
                {
                    WriteDesignationOrigin(writer, d.OriginTile);
                    WriteDesignationHeight(writer, d.OriginTargetHeight.Value);
                    WriteDesignationHeight(writer, d.PlusXTargetHeight.Value);
                    WriteDesignationHeight(writer, d.PlusXyTargetHeight.Value);
                    WriteDesignationHeight(writer, d.PlusYTargetHeight.Value);
                    ValidateDesignationCenter(d.CenterTargetHeight.Value.RawValue);
                    writer.WriteInt32(d.CenterTargetHeight.Value.RawValue);
                }
                return true;
            }
            if (command.GetType() == typeof(RemoveDesignationsCmd))
            {
                var value = (RemoveDesignationsCmd)command; ValidateDesignationCount(value.Origins.Length);
                writer.WriteUInt16(61); writer.WriteUInt16((ushort)value.Origins.Length);
                foreach (Tile2i origin in value.Origins) WriteDesignationOrigin(writer, origin);
                return true;
            }
            return false;
        }

        private static IInputCommand? DecodeTerrain(GameCommandKind kind, PayloadReader reader, DependencyResolver resolver)
        {
            if ((ushort)kind == 60)
            {
                string id = reader.ReadString(MaxProtoIdBytes); ValidateProtoId(id);
                int count = reader.ReadUInt16(); ValidateDesignationCount(count);
                var data = new ImmutableArrayBuilder<DesignationData>(count);
                for (int i = 0; i < count; i++)
                {
                    Tile2i origin = ReadDesignationOrigin(reader);
                    var h0 = ReadDesignationHeight(reader); var hx = ReadDesignationHeight(reader);
                    var hxy = ReadDesignationHeight(reader); var hy = ReadDesignationHeight(reader);
                    int center = reader.ReadInt32(); ValidateDesignationCenter(center);
                    data[i] = new DesignationData(origin, h0, hx, hxy, hy, new HeightTilesF(Fix32.FromRaw(center)));
                }
                reader.EnsureEnd();
                RequireResearchTradeProto<TerrainDesignationProto>(resolver, id);
                var items = data.GetImmutableArrayAndClear();
                foreach (var item in items) RequireDesignationMap(resolver, item.OriginTile);
                return new AddTerrainDesignationsCmd(new Proto.ID(id), items);
            }
            if ((ushort)kind == 61)
            {
                int count = reader.ReadUInt16(); ValidateDesignationCount(count);
                var data = new ImmutableArrayBuilder<Tile2i>(count);
                for (int i = 0; i < count; i++) data[i] = ReadDesignationOrigin(reader);
                reader.EnsureEnd();
                var items = data.GetImmutableArrayAndClear();
                foreach (var item in items) RequireDesignationMap(resolver, item);
                return new RemoveDesignationsCmd(items);
            }
            return null;
        }

        private static void ValidateDesignationCount(int count)
        {
            if (count < 1 || count > MaxDesignations) throw new ProtocolException("Designation count must be 1 to 1024.");
        }
        private static void ValidateDesignationOrigin(Tile2i origin)
        {
            ValidateTile(origin);
            if ((origin.X & 3) != 0 || (origin.Y & 3) != 0)
                throw new ProtocolException("Designation origin must align with the four-tile grid.");
        }
        private static void WriteDesignationOrigin(PayloadWriter writer, Tile2i origin)
        {
            ValidateDesignationOrigin(origin); writer.WriteInt32(origin.X); writer.WriteInt32(origin.Y);
        }
        private static Tile2i ReadDesignationOrigin(PayloadReader reader)
        {
            var origin = new Tile2i(reader.ReadInt32(), reader.ReadInt32()); ValidateDesignationOrigin(origin); return origin;
        }
        private static void RequireDesignationMap(DependencyResolver resolver, Tile2i origin)
        {
            RequireMapTile(resolver, origin); RequireMapTile(resolver, new Tile2i(origin.X + 3, origin.Y + 3));
        }
        private static void ValidateDesignationCenter(int raw)
        {
            if (raw < short.MinValue * 1024 || raw > short.MaxValue * 1024)
                throw new ProtocolException("Designation height exceeds the game's compact height range.");
        }
        private static void WriteDesignationHeight(PayloadWriter writer, int height)
        {
            if (height < short.MinValue || height > short.MaxValue) throw new ProtocolException("Invalid designation height.");
            writer.WriteInt32(height);
        }
        private static HeightTilesI ReadDesignationHeight(PayloadReader reader)
        {
            int height = reader.ReadInt32();
            if (height < short.MinValue || height > short.MaxValue) throw new ProtocolException("Invalid designation height.");
            return new HeightTilesI(height);
        }
    }
}
