using CoiDedicated.Protocol;
using Mafi;
using Mafi.Collections.ImmutableCollections;
using Mafi.Numerics;
using Mafi.Core;
using Mafi.Core.Buildings.Mine;
using Mafi.Core.Buildings.Forestry;
using Mafi.Core.Input;
using Mafi.Core.Products;
using Mafi.Core.Terrain;

namespace CoiDedicated.GameAdapter
{
    public static partial class GameCommandCodec
    {
        public const int MaxWorkAreaVertices = 256;
        private static bool TryEncodeWorkArea(IInputCommand command, PayloadWriter writer)
        {
            var type=command.GetType();
            if (type==typeof(MineTowerAreaChangeCmd))
            {
                var v=(MineTowerAreaChangeCmd)command; writer.WriteUInt16(62);
                ValidateEntityId(v.MineTowerId.Value); writer.WriteInt32(v.MineTowerId.Value); WriteWorkArea(writer,v.Area);
            }
            else if (type==typeof(ForestryTowerAreaChangeCmd))
            {
                var v=(ForestryTowerAreaChangeCmd)command; writer.WriteUInt16(63);
                ValidateEntityId(v.ForestryTowerId.Value); writer.WriteInt32(v.ForestryTowerId.Value); WriteWorkArea(writer,v.Area);
            }
            else if (type==typeof(AddProductToDumpCmd))
            {
                var v=(AddProductToDumpCmd)command; writer.WriteUInt16(64); WriteDumpProduct(writer,v.MineTowerId,v.ProductId);
            }
            else if (type==typeof(RemoveProductToDumpCmd))
            {
                var v=(RemoveProductToDumpCmd)command; writer.WriteUInt16(65); WriteDumpProduct(writer,v.MineTowerId,v.ProductId);
            }
            else return false;
            return true;
        }
        private static IInputCommand? DecodeWorkArea(GameCommandKind kind, PayloadReader reader, DependencyResolver resolver)
        {
            if ((ushort)kind==62 || (ushort)kind==63)
            {
                EntityId id=ReadEntityId(reader); var area=ReadWorkArea(reader); reader.EnsureEnd();
                // Polygon vertices may sit on the exclusive tile boundary.
                if (!area.IsEmpty)
                {
                    RequireMapTile(resolver,area.BoundingBoxMin);
                    RequireMapTile(resolver,new Tile2i(area.BoundingBoxMax.X-1,area.BoundingBoxMax.Y-1));
                }
                if ((ushort)kind==62) { RequireEntity<MineTower>(resolver,id); return new MineTowerAreaChangeCmd(id,area); }
                RequireEntity<ForestryTower>(resolver,id); return new ForestryTowerAreaChangeCmd(id,area);
            }
            if ((ushort)kind==64 || (ushort)kind==65)
            {
                EntityId? id=reader.ReadBoolean() ? ReadEntityId(reader) : (EntityId?)null;
                string product=reader.ReadString(MaxProtoIdBytes); reader.EnsureEnd();
                var prototype=RequireResearchTradeProto<ProductProto>(resolver,product);
                Option<MineTower> tower=id.HasValue ? Option<MineTower>.Create(RequireEntity<MineTower>(resolver,id.Value)) : Option<MineTower>.None;
                return (ushort)kind==64 ? new AddProductToDumpCmd(tower,prototype) : new RemoveProductToDumpCmd(tower,prototype);
            }
            return null;
        }
        private static void WriteDumpProduct(PayloadWriter writer,EntityId? entity,ProductProto.ID product)
        {
            writer.WriteBoolean(entity.HasValue);
            if (entity.HasValue) { ValidateEntityId(entity.Value.Value); writer.WriteInt32(entity.Value.Value); }
            ValidateProtoId(product.Value); writer.WriteString(product.Value,MaxProtoIdBytes);
        }
        private static void WriteWorkArea(PayloadWriter writer,PolygonTerrainArea2i area)
        {
            int count=area.Polygon.Vertices.Length; ValidateWorkAreaCount(count); writer.WriteUInt16((ushort)count);
            foreach (var point in area.Polygon.Vertices)
            { ValidateTile(new Tile2i(point.X,point.Y)); writer.WriteInt32(point.X); writer.WriteInt32(point.Y); }
        }
        private static PolygonTerrainArea2i ReadWorkArea(PayloadReader reader)
        {
            int count=reader.ReadUInt16(); ValidateWorkAreaCount(count); var vertices=new ImmutableArrayBuilder<Vector2i>(count);
            if (count==0) return PolygonTerrainArea2i.FromVertices(ImmutableArray<Vector2i>.Empty);
            for (int i=0;i<count;i++)
            {
                var point=new Vector2i(reader.ReadInt32(),reader.ReadInt32()); ValidateTile(new Tile2i(point.X,point.Y)); vertices[i]=point;
            }
            if (!PolygonTerrainArea2i.TryCreate(new Polygon2i(vertices.GetImmutableArrayAndClear()),out var area,out string error))
                throw new ProtocolException("Invalid work area: "+error);
            return area;
        }
        private static void ValidateWorkAreaCount(int count)
        { if ((count!=0 && count<3) || count>MaxWorkAreaVertices) throw new ProtocolException("Work area requires zero or 3 to 256 vertices."); }
    }
}
