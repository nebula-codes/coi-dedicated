using CoiDedicated.Protocol;
using Mafi;
using Mafi.Collections.ImmutableCollections;
using Mafi.Core;
using Mafi.Core.Entities.Static;
using Mafi.Core.Factory.Transports;
using Mafi.Core.Input;
using Mafi.Core.Prototypes;

namespace CoiDedicated.GameAdapter
{
    public static partial class GameCommandCodec
    {
        public const int MaxTransportPoints = 1024;

        private static bool TryEncodeTransport(IInputCommand command, PayloadWriter writer)
        {
            if (command.GetType() != typeof(BuildTransportCmd)) return false;
            var value = (BuildTransportCmd)command;
            if (value.IsFree) throw new ProtocolException("Transport construction must pay normal construction costs.");
            ValidateProtoId(value.ProtoId.Value);
            ValidateTransportCount(value.PivotPositions.Length, 1);
            ValidateTransportCount(value.PillarHints.Length, 0);
            writer.WriteUInt16((ushort)GameCommandKind.BuildTransport);
            writer.WriteString(value.ProtoId.Value, MaxProtoIdBytes);
            writer.WriteUInt16((ushort)value.PivotPositions.Length);
            foreach (Tile3i position in value.PivotPositions)
            {
                ValidatePlacementScalars(position, 0);
                writer.WriteInt32(position.X); writer.WriteInt32(position.Y); writer.WriteInt32(position.Z);
            }
            writer.WriteUInt16((ushort)value.PillarHints.Length);
            foreach (Tile2i position in value.PillarHints)
            {
                ValidateTile(position); writer.WriteInt32(position.X); writer.WriteInt32(position.Y);
            }
            WriteTransportDirection(writer, value.StartDirection);
            WriteTransportDirection(writer, value.EndDirection);
            writer.WriteBoolean(value.DisablePortSnapping);
            writer.WriteBoolean(value.AllowDirectConnection);
            return true;
        }

        private static BuildTransportCmd DecodeTransport(PayloadReader reader, DependencyResolver resolver)
        {
            string id = reader.ReadString(MaxProtoIdBytes); ValidateProtoId(id);
            int count = reader.ReadUInt16(); ValidateTransportCount(count, 1);
            var positions = new ImmutableArrayBuilder<Tile3i>(count);
            for (int i = 0; i < count; i++)
            {
                var position = new Tile3i(reader.ReadInt32(), reader.ReadInt32(), reader.ReadInt32());
                ValidatePlacementScalars(position, 0); positions[i] = position;
            }
            count = reader.ReadUInt16(); ValidateTransportCount(count, 0);
            var pillars = new ImmutableArrayBuilder<Tile2i>(count);
            for (int i = 0; i < count; i++)
            {
                var position = new Tile2i(reader.ReadInt32(), reader.ReadInt32()); ValidateTile(position); pillars[i] = position;
            }
            Direction903d? start = ReadTransportDirection(reader), end = ReadTransportDirection(reader);
            bool noSnapping = reader.ReadBoolean(), direct = reader.ReadBoolean(); reader.EnsureEnd();
            var protoId = new StaticEntityProto.ID(id);
            if (!resolver.Resolve<ProtosDb>().TryGetProto<TransportProto>(protoId, out TransportProto proto)
                || !proto.IsUnlockedAndAvailable || proto.CannotBeBuiltByPlayer)
                throw new ProtocolException("This transport is not available for player construction.");
            var pivotArray = positions.GetImmutableArrayAndClear();
            var pillarArray = pillars.GetImmutableArrayAndClear();
            foreach (Tile3i p in pivotArray) RequireMapTile(resolver, new Tile2i(p.X, p.Y));
            foreach (Tile2i p in pillarArray) RequireMapTile(resolver, p);
            // The original transport processor checks trajectory, connection, and construction cost.
            return new BuildTransportCmd(protoId, pivotArray, pillarArray, start, end, noSnapping, false, direct);
        }

        private static void ValidateTransportCount(int count, int minimum)
        {
            if (count < minimum || count > MaxTransportPoints) throw new ProtocolException("Invalid transport point count.");
        }

        private static void WriteTransportDirection(PayloadWriter writer, Direction903d? direction)
        {
            int index = direction.HasValue ? direction.Value.DirectionIndex : -1;
            if (direction.HasValue && (index < 0 || index > 5)) throw new ProtocolException("Invalid transport direction.");
            writer.WriteInt32(index);
        }

        private static Direction903d? ReadTransportDirection(PayloadReader reader)
        {
            int index = reader.ReadInt32();
            if (index < -1 || index > 5) throw new ProtocolException("Invalid transport direction.");
            return index == -1 ? (Direction903d?)null : new Direction903d(index);
        }
    }
}
