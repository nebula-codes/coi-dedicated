using System;
using CoiDedicated.Protocol;
using Mafi;
using Mafi.Core;
using Mafi.Core.Buildings.RuinedBuildings;
using Mafi.Core.Entities;
using Mafi.Core.Entities.Dynamic;
using Mafi.Core.Input;
using Mafi.Core.Products;
using Mafi.Core.Prototypes;
using Mafi.Core.Terrain;
using Mafi.Core.Terrain.Trees;
using Mafi.Core.Vehicles;
using Mafi.Core.Vehicles.Commands;
using Mafi.Core.World;

namespace CoiDedicated.GameAdapter
{
    public static partial class GameCommandCodec
    {
        private static bool TryEncodeEarlyGame(IInputCommand command, PayloadWriter writer)
        {
            Type type = command.GetType();
            if (type == typeof(RuinsScrapCmd))
            {
                var value = (RuinsScrapCmd)command;
                ValidateEntityId(value.RuinsId.Value);
                writer.WriteUInt16((ushort)GameCommandKind.RuinsScrap);
                writer.WriteInt32(value.RuinsId.Value);
                writer.WriteBoolean(value.IsScrapping);
            }
            else if (type == typeof(FleetLoadCrewCmd)) writer.WriteUInt16((ushort)GameCommandKind.FleetLoadCrew);
            else if (type == typeof(DesignateHarvestedTreesCmd))
            {
                var value = (DesignateHarvestedTreesCmd)command;
                ValidateArea(value.Area);
                writer.WriteUInt16((ushort)GameCommandKind.DesignateHarvestedTrees);
                writer.WriteInt32(value.Area.Origin.X); writer.WriteInt32(value.Area.Origin.Y);
                writer.WriteInt32(value.Area.Size.X); writer.WriteInt32(value.Area.Size.Y);
                writer.WriteBoolean(value.AddToHarvest);
                writer.WriteBoolean(value.HarvestedProductId.HasValue);
                if (value.HarvestedProductId.HasValue)
                {
                    ValidateProtoId(value.HarvestedProductId.Value.Value);
                    writer.WriteString(value.HarvestedProductId.Value.Value, MaxProtoIdBytes);
                }
            }
            else if (type == typeof(NavigateVehicleToPositionCmd))
            {
                var value = (NavigateVehicleToPositionCmd)command;
                ValidateEntityId(value.VehicleId.Value); ValidateTile(value.Position);
                writer.WriteUInt16((ushort)GameCommandKind.NavigateVehicleToPosition);
                writer.WriteInt32(value.VehicleId.Value);
                writer.WriteInt32(value.Position.X); writer.WriteInt32(value.Position.Y);
            }
            else if (type == typeof(ToggleEnabledCmd))
            {
                var value = (ToggleEnabledCmd)command;
                ValidateEntityId(value.EntityId.Value);
                writer.WriteUInt16((ushort)GameCommandKind.ToggleEnabled);
                writer.WriteInt32(value.EntityId.Value);
            }
            else return false;
            return true;
        }

        private static IInputCommand? DecodeEarlyGame(GameCommandKind kind, PayloadReader reader, DependencyResolver resolver)
        {
            switch (kind)
            {
                case GameCommandKind.RuinsScrap:
                {
                    EntityId id = ReadEntityId(reader); bool scrap = reader.ReadBoolean(); reader.EnsureEnd();
                    // Avoid the game's legacy invalid-ID fallback to an arbitrary ruin.
                    RequireEntity<Ruins>(resolver, id);
                    return new RuinsScrapCmd(id, scrap);
                }
                case GameCommandKind.FleetLoadCrew:
                    reader.EnsureEnd(); return new FleetLoadCrewCmd();
                case GameCommandKind.DesignateHarvestedTrees:
                {
                    var origin = new Tile2i(reader.ReadInt32(), reader.ReadInt32());
                    var size = new RelTile2i(reader.ReadInt32(), reader.ReadInt32());
                    bool add = reader.ReadBoolean();
                    string? product = reader.ReadBoolean() ? reader.ReadString(MaxProtoIdBytes) : null;
                    reader.EnsureEnd();
                    // Validate before the game's rectangle constructor performs arithmetic.
                    ValidateAreaScalars(origin, size);
                    var area = new RectangleTerrainArea2i(origin, size);
                    RequireMapTile(resolver, origin);
                    RequireMapTile(resolver, new Tile2i(origin.X + size.X - 1, origin.Y + size.Y - 1));
                    ProductProto.ID? productId = null;
                    if (product != null)
                    {
                        ValidateProtoId(product); productId = new ProductProto.ID(product);
                        if (!resolver.Resolve<ProtosDb>().TryGetProto<ProductProto>(productId.Value, out _))
                            throw new ProtocolException("Unknown harvested product.");
                    }
                    return new DesignateHarvestedTreesCmd(area, add, productId);
                }
                case GameCommandKind.NavigateVehicleToPosition:
                {
                    EntityId id = ReadEntityId(reader);
                    var position = new Tile2i(reader.ReadInt32(), reader.ReadInt32()); reader.EnsureEnd();
                    ValidateTile(position); RequireMapTile(resolver, position);
                    PathFindingEntity entity = RequireEntity<PathFindingEntity>(resolver, id);
                    if (!(entity is Vehicle) && !(entity is Mafi.Core.Entities.Ships.Ship))
                        throw new ProtocolException("This entity cannot receive player navigation commands.");
                    return new NavigateVehicleToPositionCmd(id, position);
                }
                case GameCommandKind.ToggleEnabled:
                {
                    EntityId id = ReadEntityId(reader); reader.EnsureEnd();
                    if (!RequireEntity<IEntity>(resolver, id).CanBePaused)
                        throw new ProtocolException("This entity cannot be paused.");
                    return new ToggleEnabledCmd(id);
                }
                default: return null;
            }
        }

        private static void ValidateTile(Tile2i position)
        {
            if (position.X < 0 || position.Y < 0 || position.X >= TerrainManager.TERRAIN_DIMENSION_MAX
                || position.Y >= TerrainManager.TERRAIN_DIMENSION_MAX)
                throw new ProtocolException("Tile is outside supported terrain dimensions.");
        }

        private static void RequireMapTile(DependencyResolver resolver, Tile2i position)
        {
            TerrainManager terrain = resolver.Resolve<TerrainManager>();
            if (position.X < terrain.MinOnLimits.X || position.Y < terrain.MinOnLimits.Y
                || position.X >= terrain.MaxOnLimitsExcl.X || position.Y >= terrain.MaxOnLimitsExcl.Y)
                throw new ProtocolException("Tile is outside the playable map.");
        }

        private static void ValidateArea(RectangleTerrainArea2i area) => ValidateAreaScalars(area.Origin, area.Size);
        private static void ValidateAreaScalars(Tile2i origin, RelTile2i size)
        {
            ValidateTile(origin);
            if (size.X <= 0 || size.Y <= 0 || (long)origin.X + size.X > TerrainManager.TERRAIN_DIMENSION_MAX
                || (long)origin.Y + size.Y > TerrainManager.TERRAIN_DIMENSION_MAX)
                throw new ProtocolException("Selection area exceeds supported terrain dimensions.");
        }
    }
}
