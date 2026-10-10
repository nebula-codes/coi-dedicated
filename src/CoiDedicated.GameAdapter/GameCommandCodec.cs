using System;
using System.Text;
using CoiDedicated.Protocol;
using Mafi;
using Mafi.Core;
using Mafi.Core.Buildings.Storages;
using Mafi.Core.Entities;
using Mafi.Core.Entities.Commands;
using Mafi.Core.Entities.Static;
using Mafi.Core.Entities.Static.Commands;
using Mafi.Core.Entities.Static.Layout;
using Mafi.Core.Factory.Machines;
using Mafi.Core.Factory.Recipes;
using Mafi.Core.Input;
using Mafi.Core.Prototypes;
using Mafi.Core.Simulation;
using Mafi.Core.Terrain;

namespace CoiDedicated.GameAdapter
{
    /// <summary>
    /// Explicit allowlist for network-originated game commands. Decode must run on the simulation
    /// thread immediately before scheduling, so world references are checked against the current island.
    /// No game serializer, network-supplied CLR type, reflection, or object graph deserializer is used.
    /// </summary>
    public static partial class GameCommandCodec
    {
        public const ushort Version = 1;
        public const int MaxCommandBytes = 64 * 1024;
        public const int MaxProtoIdBytes = 256;
        public const int MinGameSpeed = 1;
        public const int MaxGameSpeed = 20;
        public const int MinConstructionPriority = 0;
        public const int MaxConstructionPriority = 14;
        private static readonly UTF8Encoding Utf8 = new UTF8Encoding(false, true);

        /// <summary>
        /// Native schedule insert/remove and root-priority handlers omit some execution-time
        /// index/membership checks. Admit these alone so validation sees preceding tick results.
        /// This is admission policy only; the full decoder still validates every accepted payload.
        /// </summary>
        public static bool RequiresIsolatedTick(ReadOnlyMemory<byte> payload)
        {
            var bytes=payload.Span;
            if(bytes.Length<4 || (bytes[0]|bytes[1]<<8)!=Version)return false;
            int kind=bytes[2]|bytes[3]<<8;
            return kind==217 || kind==227 || kind==241 || kind==252;
        }

        public static bool TryEncode(IInputCommand command, out byte[] payload) => TryEncode(command, out payload, out _);

        /// <summary>Returns an empty payload and explanation for unsupported or invalid commands.</summary>
        public static bool TryEncode(IInputCommand command, out byte[] payload, out string reason)
        {
            payload = Array.Empty<byte>();
            if (command == null) { reason = "Cannot send a null game command."; return false; }
            try
            {
                using var writer = new PayloadWriter(MaxCommandBytes);
                writer.WriteUInt16(Version);
                // Exact types prevent subclasses with different execution behavior being silently truncated.
                Type type = command.GetType();
                if (type == typeof(BatchCreateStaticEntitiesCmd))
                {
                    EncodeBuildingBatch((BatchCreateStaticEntitiesCmd)command, writer);
                }
                else if (type == typeof(SetSimPauseStateCmd))
                {
                    var value = (SetSimPauseStateCmd)command;
                    writer.WriteUInt16((ushort)GameCommandKind.SetSimPauseState);
                    writer.WriteBoolean(value.IsPaused);
                }
                else if (type == typeof(GameSpeedChangeCmd))
                {
                    var value = (GameSpeedChangeCmd)command;
                    ValidateSpeed(value.NewSpeedMultiplier);
                    writer.WriteUInt16((ushort)GameCommandKind.GameSpeedChange);
                    writer.WriteInt32(value.NewSpeedMultiplier);
                }
                else if (type == typeof(SetConstructionPriorityCmd))
                {
                    var value = (SetConstructionPriorityCmd)command;
                    ValidateEntityId(value.EntityId.Value);
                    ValidatePriority(value.Priority);
                    writer.WriteUInt16((ushort)GameCommandKind.SetConstructionPriority);
                    writer.WriteInt32(value.EntityId.Value);
                    writer.WriteInt32(value.Priority);
                }
                else if (type == typeof(MachineSetRecipeActiveCmd))
                {
                    var value = (MachineSetRecipeActiveCmd)command;
                    ValidateEntityId(value.MachineId.Value);
                    ValidateProtoId(value.RecipeId.Value);
                    writer.WriteUInt16((ushort)GameCommandKind.MachineSetRecipeActive);
                    writer.WriteInt32(value.MachineId.Value);
                    writer.WriteString(value.RecipeId.Value, MaxProtoIdBytes);
                    writer.WriteBoolean(value.EnableRecipe);
                }
                else if (type == typeof(StartDeconstructionOfStaticEntityCmd))
                {
                    var value = (StartDeconstructionOfStaticEntityCmd)command;
                    ValidateEntityId(value.EntityId.Value);
                    if (value.RemoveReason != EntityRemoveReason.Remove)
                        throw new ProtocolException("Only normal player-requested deconstruction can be sent; collapse is simulation-owned.");
                    writer.WriteUInt16((ushort)GameCommandKind.StartDeconstructionOfStaticEntity);
                    writer.WriteInt32(value.EntityId.Value);
                    writer.WriteBoolean(value.IsAreaRemove);
                }
                else if (type == typeof(ToggleStaticEntityConstructionCmd))
                {
                    var value = (ToggleStaticEntityConstructionCmd)command;
                    ValidateEntityId(value.EntityId.Value);
                    writer.WriteUInt16((ushort)GameCommandKind.ToggleStaticEntityConstruction);
                    writer.WriteInt32(value.EntityId.Value);
                }
                else if (type == typeof(CreateStaticEntityCmd))
                {
                    var value = (CreateStaticEntityCmd)command;
                    ValidateProtoId(value.ProtoId.Value);
                    if (value.IsFree || value.AllowValidationSuppression)
                        throw new ProtocolException("Building placement must use normal construction and placement validation.");
                    ValidatePlacementScalars(value.Transform.Position, value.Transform.Rotation.AngleIndex);
                    writer.WriteUInt16((ushort)GameCommandKind.CreateBuilding);
                    writer.WriteString(value.ProtoId.Value, MaxProtoIdBytes);
                    writer.WriteInt32(value.Transform.Position.X);
                    writer.WriteInt32(value.Transform.Position.Y);
                    writer.WriteInt32(value.Transform.Position.Z);
                    writer.WriteInt32(value.Transform.Rotation.AngleIndex);
                    writer.WriteBoolean(value.Transform.IsReflected);
                }
                else if (TryEncodeEarlyGame(command, writer)) { }
                else if (TryEncodeTransport(command, writer)) { }
                else if (TryEncodeResearchAndTrade(command, writer)) { }
                else if (TryEncodeScalarCommand(command, writer)) { }
                else if (TryEncodeTerrain(command, writer)) { }
                else if (TryEncodeWorkArea(command, writer)) { }
                else if (TryEncodeManagement(command, writer)) { }
                else if (TryEncodeSurfaces(command, writer)) { }
                else if (TryEncodeLogistics(command, writer)) { }
                else if (TryEncodeDetailControls(command, writer)) { }
                else if (TryEncodeBridges(command, writer)) { }
                else if (TryEncodeRails(command, writer)) { }
                else if (TryEncodeTrainControls(command, writer)) { }
                else
                {
                    reason = $"Command '{type.Name}' is not supported by dedicated multiplayer yet.";
                    return false;
                }
                payload = writer.ToArray();
                reason = "";
                return true;
            }
            catch (Exception ex) when (ex is ProtocolException || ex is ArgumentException || ex is InvalidOperationException)
            {
                reason = ex.Message;
                return false;
            }
        }

        /// <summary>
        /// Validates complete payload and live entity/prototype references, then creates an unscheduled command.
        /// Throws ProtocolException for invalid wire data or unavailable world references.
        /// Authentication, permissions, rate limits, command ordering, and scheduling belong to the caller.
        /// </summary>
        public static IInputCommand Decode(ReadOnlyMemory<byte> payload, DependencyResolver resolver)
        {
            if (resolver == null) throw new ArgumentNullException(nameof(resolver));
            if (payload.Length > MaxCommandBytes) throw new ProtocolException("Game command exceeds its configured byte limit.");
            var reader = new PayloadReader(payload);
            if (reader.ReadUInt16() != Version) throw new ProtocolException("Unsupported game-command schema version.");
            var kind = (GameCommandKind)reader.ReadUInt16();
            switch (kind)
            {
                case GameCommandKind.BatchCreateBuildings:
                    return DecodeBuildingBatch(reader, resolver);
                case (GameCommandKind)400:
                    return DecodeBlueprintBatch(reader, resolver);
                case GameCommandKind.BuildTransport:
                    return DecodeTransport(reader, resolver);
                case GameCommandKind.SetSimPauseState:
                {
                    bool isPaused = reader.ReadBoolean();
                    reader.EnsureEnd();
                    return new SetSimPauseStateCmd(isPaused);
                }
                case GameCommandKind.GameSpeedChange:
                {
                    int speed = reader.ReadInt32();
                    reader.EnsureEnd();
                    ValidateSpeed(speed);
                    return new GameSpeedChangeCmd(speed);
                }
                case GameCommandKind.SetConstructionPriority:
                {
                    EntityId entityId = ReadEntityId(reader);
                    int priority = reader.ReadInt32();
                    reader.EnsureEnd();
                    ValidatePriority(priority);
                    RequireEntity<IStaticEntity>(resolver, entityId);
                    return new SetConstructionPriorityCmd(entityId, priority);
                }
                case GameCommandKind.MachineSetRecipeActive:
                {
                    EntityId machineId = ReadEntityId(reader);
                    string id = reader.ReadString(MaxProtoIdBytes);
                    bool enabled = reader.ReadBoolean();
                    reader.EnsureEnd();
                    ValidateProtoId(id);
                    var recipeId = new RecipeProto.ID(id);
                    Machine machine = RequireEntity<Machine>(resolver, machineId);
                    if (!resolver.Resolve<ProtosDb>().TryGetProto<RecipeProto>(recipeId, out RecipeProto recipe))
                        throw new ProtocolException($"Recipe '{id}' does not exist.");
                    if (recipe.IsNotAvailable || !machine.Prototype.Recipes.Contains(recipe))
                        throw new ProtocolException($"Recipe '{id}' is unavailable for machine {machineId.Value}.");
                    return new MachineSetRecipeActiveCmd(machineId, recipeId, enabled);
                }
                case GameCommandKind.StartDeconstructionOfStaticEntity:
                {
                    EntityId entityId = ReadEntityId(reader);
                    bool isAreaRemove = reader.ReadBoolean();
                    reader.EnsureEnd();
                    StaticEntity entity = RequireEntity<StaticEntity>(resolver, entityId);
                    var validation = resolver.Resolve<IEntitiesManager>().CanRemoveEntity(entity, EntityRemoveReason.Remove);
                    if (validation.IsError) throw new ProtocolException($"Entity {entityId.Value} cannot be removed: {validation.ErrorMessage}");
                    return new StartDeconstructionOfStaticEntityCmd(entityId, EntityRemoveReason.Remove, isAreaRemove);
                }
                case GameCommandKind.ToggleStaticEntityConstruction:
                {
                    EntityId entityId = ReadEntityId(reader);
                    reader.EnsureEnd();
                    StaticEntity entity = RequireEntity<StaticEntity>(resolver, entityId);
                    if (entity.IsConstructed) throw new ProtocolException($"Entity {entityId.Value} is already constructed.");
                    return new ToggleStaticEntityConstructionCmd(entityId);
                }
                case GameCommandKind.CreateBuilding:
                {
                    string id = reader.ReadString(MaxProtoIdBytes);
                    int x = reader.ReadInt32(), y = reader.ReadInt32(), z = reader.ReadInt32();
                    int rotation = reader.ReadInt32();
                    bool reflected = reader.ReadBoolean();
                    reader.EnsureEnd();
                    ValidateProtoId(id);
                    var position = new Tile3i(x, y, z);
                    ValidatePlacementScalars(position, rotation);
                    var transform = new TileTransform(position, new Rotation90(rotation), reflected);
                    var protoId = new StaticEntityProto.ID(id);
                    RequireBuildableLayout(resolver, id, transform);
                    // Occupancy, support, terrain, and construction remain subject to
                    // the game's normal command processor at its ordered execution tick.
                    // Neither privileged flag exists in the wire format.
                    return new CreateStaticEntityCmd(protoId, transform, isFree: false, allowValidationSuppression: false);
                }
                default:
                    IInputCommand? extra = DecodeEarlyGame(kind, reader, resolver);
                    extra ??= DecodeResearchAndTrade(kind, reader, resolver);
                    extra ??= DecodeScalarCommand(kind, reader, resolver);
                    extra ??= DecodeTerrain(kind, reader, resolver);
                    extra ??= DecodeWorkArea(kind, reader, resolver);
                    extra ??= DecodeManagement(kind, reader, resolver);
                    extra ??= DecodeSurfaces(kind, reader, resolver);
                    extra ??= DecodeLogistics(kind, reader, resolver);
                    extra ??= DecodeDetailControls(kind, reader, resolver);
                    extra ??= DecodeBridges(kind, reader, resolver);
                    extra ??= DecodeRails(kind, reader, resolver);
                    extra ??= DecodeTrainControls(kind, reader, resolver);
                    if (extra != null) return extra;
                    throw new ProtocolException($"Unsupported game-command kind {(ushort)kind}.");
            }
        }

        private static T RequireEntity<T>(DependencyResolver resolver, EntityId id) where T : class, IEntity
        {
            if (!resolver.Resolve<IEntitiesManager>().TryGetEntity<T>(id, out T entity) || entity.IsDestroyed)
                throw new ProtocolException($"Entity {id.Value} does not exist or has the wrong type for this command.");
            return entity;
        }

        private static EntityId ReadEntityId(PayloadReader reader)
        {
            int id = reader.ReadInt32();
            ValidateEntityId(id);
            return new EntityId(id);
        }

        private static void ValidateEntityId(int id)
        {
            if (id <= 0) throw new ProtocolException("Entity ID must be positive.");
        }

        private static void ValidateSpeed(int speed)
        {
            if (speed < MinGameSpeed || speed > MaxGameSpeed) throw new ProtocolException("Simulation speed must be between 1 and 20.");
        }

        private static void ValidatePriority(int priority)
        {
            if (priority < MinConstructionPriority || priority > MaxConstructionPriority)
                throw new ProtocolException("Construction priority must be between 0 and 14.");
        }

        private static void ValidatePlacementScalars(Tile3i position, int rotation)
        {
            if (position.X < 0 || position.X >= TerrainManager.TERRAIN_DIMENSION_MAX
                || position.Y < 0 || position.Y >= TerrainManager.TERRAIN_DIMENSION_MAX)
                throw new ProtocolException("Placement coordinates exceed the supported terrain dimensions.");
            if (position.Z < short.MinValue || position.Z > short.MaxValue)
                throw new ProtocolException("Placement height exceeds the game's compact height range.");
            if (rotation < 0 || rotation > 3)
                throw new ProtocolException("Placement rotation must be 0, 1, 2, or 3 quarter turns.");
        }

        private static void ValidateProtoId(string id)
        {
            if (string.IsNullOrWhiteSpace(id) || Utf8.GetByteCount(id) > MaxProtoIdBytes)
                throw new ProtocolException("Prototype ID must be nonempty and at most 256 UTF-8 bytes.");
            foreach (char character in id)
                if (char.IsControl(character)) throw new ProtocolException("Prototype ID cannot contain control characters.");
        }
    }
}
