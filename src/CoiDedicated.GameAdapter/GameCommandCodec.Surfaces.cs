using CoiDedicated.Protocol;
using Mafi;
using Mafi.Collections.ImmutableCollections;
using Mafi.Core.Input;
using Mafi.Core.Prototypes;
using Mafi.Core.Terrain;
using Mafi.Core.Terrain.Designation;
using Mafi.Core.Terrain.Surfaces;

namespace CoiDedicated.GameAdapter
{
    public static partial class GameCommandCodec
    {
        public const int MaxSurfaceAreaTiles = 16384;

        private static bool TryEncodeSurfaces(IInputCommand command, PayloadWriter writer)
        {
            var type = command.GetType();
            if (type == typeof(AddSurfaceDesignationsCmd))
            {
                var v = (AddSurfaceDesignationsCmd)command;
                writer.WriteUInt16(81); ValidateProtoId(v.ProtoId.Value); writer.WriteString(v.ProtoId.Value, MaxProtoIdBytes);
                ValidateDesignationCount(v.Data.Length); writer.WriteUInt16((ushort)v.Data.Length);
                foreach (var d in v.Data)
                {
                    WriteDesignationOrigin(writer, d.OriginTile); writer.WriteUInt16(d.UnassignedTilesBitmap);
                    ValidateRange(d.SurfaceProtoSlimId.Value, 0, 31, "Surface ID"); writer.WriteUInt16(d.SurfaceProtoSlimId.Value);
                }
            }
            else if (type == typeof(RemoveSurfaceDesignationsCmd))
            {
                var v = (RemoveSurfaceDesignationsCmd)command; writer.WriteUInt16(82);
                ValidateDesignationCount(v.Origins.Length); writer.WriteUInt16((ushort)v.Origins.Length);
                foreach (var p in v.Origins) WriteDesignationOrigin(writer, p);
                WriteSurfaceArea(writer, v.Area);
            }
            else if (type == typeof(AddSurfaceDecalCmd))
            {
                var v = (AddSurfaceDecalCmd)command; writer.WriteUInt16(83);
                ValidateProtoId(v.ProtoId.Value); writer.WriteString(v.ProtoId.Value, MaxProtoIdBytes);
                WriteSurfaceArea(writer, v.Area); ValidateRange(v.Rotation.AngleIndex, 0, 3, "Decal rotation");
                writer.WriteUInt16((byte)v.Rotation.AngleIndex); writer.WriteBoolean(v.IsReflected); WriteDecalColor(writer, v.ColorKey);
            }
            else if (type == typeof(RemoveSurfaceDecalCmd))
            { writer.WriteUInt16(84); WriteSurfaceArea(writer, ((RemoveSurfaceDecalCmd)command).Area); }
            else if (type == typeof(ChangeSurfaceDecalColorCmd))
            {
                var v = (ChangeSurfaceDecalColorCmd)command; writer.WriteUInt16(85);
                WriteSurfaceArea(writer, v.Area); WriteDecalColor(writer, v.ColorKey);
            }
            else return false;
            return true;
        }

        private static IInputCommand? DecodeSurfaces(GameCommandKind kind, PayloadReader reader, DependencyResolver resolver)
        {
            switch ((ushort)kind)
            {
                case 81:
                {
                    string id = ReadScalarProtoId(reader); int count = reader.ReadUInt16(); ValidateDesignationCount(count);
                    var data = new ImmutableArrayBuilder<SurfaceDesignationData>(count);
                    for (int i = 0; i < count; i++)
                    {
                        var p = ReadDesignationOrigin(reader); ushort mask = reader.ReadUInt16(); int surface = reader.ReadUInt16();
                        ValidateRange(surface, 0, 31, "Surface ID"); data[i] = new SurfaceDesignationData(p, mask, new TileSurfaceSlimId((byte)surface));
                    }
                    reader.EnsureEnd();
                    var designation = RequireResearchTradeProto<SurfaceDesignationProto>(resolver, id);
                    var items = data.GetImmutableArrayAndClear();
                    foreach (var d in items)
                    {
                        RequireDesignationMap(resolver, d.OriginTile);
                        if (designation.IsPlacing)
                        {
                            // Slim IDs belong to the shared snapshot, never accept a phantom or internal surface.
                            TerrainTileSurfaceProto? surface = null;
                            foreach (var candidate in resolver.Resolve<ProtosDb>().All<TerrainTileSurfaceProto>())
                                if (candidate.SlimId == d.SurfaceProtoSlimId) { surface = candidate; break; }
                            if (surface == null || d.SurfaceProtoSlimId.IsPhantom || !surface.CanBePlacedByPlayer || !surface.IsUnlockedAndAvailable)
                                throw new ProtocolException("Surface is not available for player construction.");
                        }
                    }
                    return new AddSurfaceDesignationsCmd(new Proto.ID(id), items);
                }
                case 82:
                {
                    int count = reader.ReadUInt16(); ValidateDesignationCount(count);
                    var items = new ImmutableArrayBuilder<Tile2i>(count);
                    for (int i = 0; i < count; i++) items[i] = ReadDesignationOrigin(reader);
                    var area = ReadSurfaceArea(reader); reader.EnsureEnd(); RequireSurfaceAreaMap(resolver, area);
                    var origins = items.GetImmutableArrayAndClear(); foreach (var p in origins) RequireDesignationMap(resolver, p);
                    return new RemoveSurfaceDesignationsCmd(origins, area);
                }
                case 83:
                {
                    string id = ReadScalarProtoId(reader); var area = ReadSurfaceArea(reader);
                    int rotation = reader.ReadUInt16(); ValidateRange(rotation, 0, 3, "Decal rotation");
                    bool reflected = reader.ReadBoolean(); int color = ReadDecalColor(reader); reader.EnsureEnd();
                    RequireSurfaceAreaMap(resolver, area);
                    var proto = RequireResearchTradeProto<TerrainTileSurfaceDecalProto>(resolver, id);
                    if (!proto.IsUnlockedAndAvailable) throw new ProtocolException("Decal research is locked.");
                    return new AddSurfaceDecalCmd(new Proto.ID(id), area, new Rotation90(rotation), reflected, color);
                }
                case 84:
                {
                    var area = ReadSurfaceArea(reader); reader.EnsureEnd(); RequireSurfaceAreaMap(resolver, area);
                    return new RemoveSurfaceDecalCmd(area);
                }
                case 85:
                {
                    var area = ReadSurfaceArea(reader); int color = ReadDecalColor(reader); reader.EnsureEnd(); RequireSurfaceAreaMap(resolver, area);
                    return new ChangeSurfaceDecalColorCmd(area, color);
                }
                default: return null;
            }
        }
        private static void WriteSurfaceArea(PayloadWriter writer, RectangleTerrainArea2i area)
        {
            ValidateSurfaceArea(area.Origin, area.Size);
            writer.WriteInt32(area.Origin.X); writer.WriteInt32(area.Origin.Y); writer.WriteInt32(area.Size.X); writer.WriteInt32(area.Size.Y);
        }
        private static RectangleTerrainArea2i ReadSurfaceArea(PayloadReader reader)
        {
            var origin = new Tile2i(reader.ReadInt32(), reader.ReadInt32()); var size = new RelTile2i(reader.ReadInt32(), reader.ReadInt32());
            ValidateSurfaceArea(origin, size); return new RectangleTerrainArea2i(origin, size);
        }
        private static void ValidateSurfaceArea(Tile2i origin, RelTile2i size)
        {
            ValidateAreaScalars(origin, size);
            if ((long)size.X * size.Y > MaxSurfaceAreaTiles) throw new ProtocolException("Surface selection exceeds 16384 tiles.");
        }
        private static void RequireSurfaceAreaMap(DependencyResolver resolver, RectangleTerrainArea2i area)
        { RequireMapTile(resolver, area.Origin); RequireMapTile(resolver, area.PlusXyTileIncl); }
        private static void WriteDecalColor(PayloadWriter writer, int color)
        { ValidateRange(color, 0, 7, "Decal color"); writer.WriteInt32(color); }
        private static int ReadDecalColor(PayloadReader reader)
        { int color = reader.ReadInt32(); ValidateRange(color, 0, 7, "Decal color"); return color; }
    }
}
