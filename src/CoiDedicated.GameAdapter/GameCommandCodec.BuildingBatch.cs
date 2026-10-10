using System;
using CoiDedicated.Protocol;
using Mafi;
using Mafi.Collections.ImmutableCollections;
using Mafi.Core;
using Mafi.Core.Entities;
using Mafi.Core.Entities.Static;
using Mafi.Core.Entities.Static.Commands;
using Mafi.Core.Entities.Static.Layout;
using Mafi.Core.Prototypes;
using Mafi.Core.Terrain;
using Mafi.Core.Trains;

namespace CoiDedicated.GameAdapter
{
    public static partial class GameCommandCodec
    {
        // A placement click remains one native batch, including its single result and
        // completion callback. Splitting it into CreateStaticEntityCmd instances would
        // change the game's mini-zipper, default-logistics, and identity handling.
        public const int MaxBuildingsPerBatch = 128;

        private static void EncodeBuildingBatch(BatchCreateStaticEntitiesCmd command, PayloadWriter writer)
        {
            if (command.IsFree || command.AllowValidationSuppression)
                throw new ProtocolException("Building placement must use normal construction and placement validation.");
            if (command.ApplyConfiguration)
            {
                EncodeBlueprintBatch(command, writer);
                return;
            }
            ValidateBatchCount(command.ConfigData.Length);
            ValidateMiniZippers(command.BuildMiniZippers);
            writer.WriteUInt16((ushort)GameCommandKind.BatchCreateBuildings);
            writer.WriteUInt16((ushort)command.BuildMiniZippers);
            writer.WriteUInt16((ushort)command.ConfigData.Length);
            foreach (EntityConfigData config in command.ConfigData)
            {
                if (config == null || !(config.Prototype.ValueOrNull is LayoutEntityProto prototype)
                    || prototype is ITrainTrackMayBeElevatedProto)
                    throw new ProtocolException("This batch contains a non-building or specialized rail placement that is not supported yet.");
                // The game's ordinary construction path ignores entity settings when
                // ApplyConfiguration is false, but train-pole data is applied regardless.
                // Reject that data until its dedicated typed wire format is implemented.
                if (config.TrackPolesBitmap.HasValue || config.TrackStartEndPolesBitmap.HasValue
                    || config.TrackPillarBlocksBitmap.HasValue || config.TrackFlags.HasValue
                    || config.TrackDirection.HasValue)
                    throw new ProtocolException("This building batch contains specialized rail configuration that is not supported yet.");
                if (!config.Transform.HasValue)
                    throw new ProtocolException("Every building in a placement batch must have a transform.");
                ValidateProtoId(prototype.Id.Value);
                TileTransform transform = config.Transform.Value;
                ValidatePlacementScalars(transform.Position, transform.Rotation.AngleIndex);
                writer.WriteString(prototype.Id.Value, MaxProtoIdBytes);
                WriteBuildingTransform(writer, transform);
            }
        }

        private static BatchCreateStaticEntitiesCmd DecodeBuildingBatch(PayloadReader reader, DependencyResolver resolver)
        {
            var miniZippers = (BuildMiniZippersMode)reader.ReadUInt16();
            ValidateMiniZippers(miniZippers);
            int count = reader.ReadUInt16();
            ValidateBatchCount(count);
            // Parse and bound the entire payload before consulting the world or allocating
            // game configuration objects. No opaque game serializer data crosses the wire.
            var ids = new string[count];
            var transforms = new TileTransform[count];
            for (int i = 0; i < count; i++)
            {
                ids[i] = reader.ReadString(MaxProtoIdBytes);
                ValidateProtoId(ids[i]);
                transforms[i] = ReadBuildingTransform(reader);
            }
            reader.EnsureEnd();
            var prototypes = new LayoutEntityProto[count];
            for (int i = 0; i < count; i++)
                prototypes[i] = RequireBuildableLayout(resolver, ids[i], transforms[i]);
            ConfigSerializationContext context = resolver.Resolve<ConfigSerializationContext>();
            var configs = new ImmutableArrayBuilder<EntityConfigData>(count);
            for (int i = 0; i < count; i++)
                configs[i] = new EntityConfigData(prototypes[i], context) { Transform = transforms[i] };
            return new BatchCreateStaticEntitiesCmd(configs.GetImmutableArrayAndClear(), miniZippers,
                isFree: false, allowValidationSuppression: false, applyConfiguration: false);
        }

        private static LayoutEntityProto RequireBuildableLayout(DependencyResolver resolver, string id, TileTransform transform)
        {
            if (!resolver.Resolve<ProtosDb>().TryGetProto<LayoutEntityProto>(new StaticEntityProto.ID(id), out LayoutEntityProto prototype)
                || prototype is ITrainTrackMayBeElevatedProto)
                throw new ProtocolException($"Prototype '{id}' is not a supported layout building; rail and transport placement require their specialized commands.");
            if (!prototype.IsUnlockedAndAvailable || prototype.CannotBeBuiltByPlayer || prototype.IsAutoBuiltByParent)
                throw new ProtocolException($"Building '{id}' is not available for player construction.");
            if (transform.IsReflected && prototype.CannotBeReflected)
                throw new ProtocolException($"Building '{id}' cannot be reflected.");
            TerrainManager terrain = resolver.Resolve<TerrainManager>();
            var bounds = prototype.Layout.GetBoundingBox2i(transform);
            if (bounds.Origin.X < terrain.MinOnLimits.X || bounds.Origin.Y < terrain.MinOnLimits.Y
                || bounds.PlusXyCoordExcl.X > terrain.MaxOnLimitsExcl.X
                || bounds.PlusXyCoordExcl.Y > terrain.MaxOnLimitsExcl.Y)
                throw new ProtocolException("Building footprint extends outside the playable map.");
            // The game's native processor validates occupancy, terrain, support and cost
            // at the ordered execution tick. Network data cannot enable bypass flags.
            return prototype;
        }

        private static void WriteBuildingTransform(PayloadWriter writer, TileTransform transform)
        {
            writer.WriteInt32(transform.Position.X);
            writer.WriteInt32(transform.Position.Y);
            writer.WriteInt32(transform.Position.Z);
            writer.WriteInt32(transform.Rotation.AngleIndex);
            writer.WriteBoolean(transform.IsReflected);
        }

        private static TileTransform ReadBuildingTransform(PayloadReader reader)
        {
            var position = new Tile3i(reader.ReadInt32(), reader.ReadInt32(), reader.ReadInt32());
            int rotation = reader.ReadInt32();
            bool reflected = reader.ReadBoolean();
            ValidatePlacementScalars(position, rotation);
            return new TileTransform(position, new Rotation90(rotation), reflected);
        }

        private static void ValidateBatchCount(int count)
        {
            if (count < 1 || count > MaxBuildingsPerBatch)
                throw new ProtocolException($"Building batches must contain 1 to {MaxBuildingsPerBatch} buildings.");
        }

        private static void ValidateMiniZippers(BuildMiniZippersMode mode)
        {
            if (mode < BuildMiniZippersMode.DeferToProto || mode > BuildMiniZippersMode.Never)
                throw new ProtocolException("Invalid mini-zipper building mode.");
        }
    }
}
