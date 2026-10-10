using CoiDedicated.Protocol;
using Mafi;
using Mafi.Core.Buildings.Shipyard;
using Mafi.Core.Input;
using Mafi.Core.Population.Edicts;
using Mafi.Core.Prototypes;
using Mafi.Core.Research;
using Mafi.Core.World.Contracts;
using Mafi.Core.World.QuickTrade;

namespace CoiDedicated.GameAdapter
{
    public static partial class GameCommandCodec
    {
        private static bool TryEncodeResearchAndTrade(IInputCommand command, PayloadWriter writer)
        {
            // Each field is a bounded scalar. Prototypes and entities are resolved in the receiving world.
            var type = command.GetType();
            if (type == typeof(ResearchStartCmd))
                WriteResearchTradeProto(writer, 40, ((ResearchStartCmd)command).NodeId.Value);
            else if (type == typeof(ResearchQueueDequeueCmd))
            {
                var value = (ResearchQueueDequeueCmd)command;
                WriteResearchTradeProto(writer, 41, value.NodeId.Value);
                writer.WriteBoolean(value.IsEnqueue);
            }
            else if (type == typeof(ResearchStopCmd))
                writer.WriteUInt16(42);
            else if (type == typeof(ToggleEdictEnabledCmd))
                WriteResearchTradeProto(writer, 43, ((ToggleEdictEnabledCmd)command).EdictProtoId.Value);
            else if (type == typeof(ToggleContractCmd))
                WriteResearchTradeProto(writer, 44, ((ToggleContractCmd)command).ContractId.Value);
            else if (type == typeof(QuickTradeCmd))
                WriteResearchTradeProto(writer, 45, ((QuickTradeCmd)command).TradeId.Value);
            else if (type == typeof(ShipyardSetRepairingCmd))
            {
                var value = (ShipyardSetRepairingCmd)command;
                ValidateEntityId(value.ShipyardId.Value);
                writer.WriteUInt16(46);
                writer.WriteInt32(value.ShipyardId.Value);
                writer.WriteBoolean(value.IsRepairing);
            }
            else return false;
            return true;
        }

        private static IInputCommand? DecodeResearchAndTrade(GameCommandKind kind, PayloadReader reader, DependencyResolver resolver)
        {
            switch ((ushort)kind)
            {
                case 40:
                {
                    string id = reader.ReadString(MaxProtoIdBytes);
                    reader.EnsureEnd();
                    RequireResearchTradeProto<ResearchNodeProto>(resolver, id);
                    return new ResearchStartCmd(new ResearchNodeProto.ID(id));
                }
                case 41:
                {
                    string id = reader.ReadString(MaxProtoIdBytes);
                    bool enqueue = reader.ReadBoolean();
                    reader.EnsureEnd();
                    RequireResearchTradeProto<ResearchNodeProto>(resolver, id);
                    return new ResearchQueueDequeueCmd(new ResearchNodeProto.ID(id), enqueue);
                }
                case 42:
                    reader.EnsureEnd();
                    return new ResearchStopCmd();
                case 43:
                {
                    string id = reader.ReadString(MaxProtoIdBytes);
                    reader.EnsureEnd();
                    var proto = RequireResearchTradeProto<EdictProto>(resolver, id);
                    return new ToggleEdictEnabledCmd(proto);
                }
                case 44:
                {
                    string id = reader.ReadString(MaxProtoIdBytes);
                    reader.EnsureEnd();
                    RequireResearchTradeProto<ContractProto>(resolver, id);
                    return new ToggleContractCmd(new Proto.ID(id));
                }
                case 45:
                {
                    string id = reader.ReadString(MaxProtoIdBytes);
                    reader.EnsureEnd();
                    RequireResearchTradeProto<QuickTradePairProto>(resolver, id);
                    return new QuickTradeCmd(new Proto.ID(id));
                }
                case 46:
                {
                    var id = ReadEntityId(reader);
                    bool repairing = reader.ReadBoolean();
                    reader.EnsureEnd();
                    RequireEntity<Shipyard>(resolver, id);
                    return new ShipyardSetRepairingCmd(id, repairing);
                }
                default:
                    return null;
            }
        }

        private static void WriteResearchTradeProto(PayloadWriter writer, ushort kind, string id)
        {
            ValidateProtoId(id);
            writer.WriteUInt16(kind);
            writer.WriteString(id, MaxProtoIdBytes);
        }

        private static T RequireResearchTradeProto<T>(DependencyResolver resolver, string id) where T : Proto
        {
            ValidateProtoId(id);
            if (!resolver.Resolve<ProtosDb>().TryGetProto<T>(new Proto.ID(id), out T proto) || proto.IsNotAvailable)
                throw new ProtocolException($"Prototype '{id}' is unavailable or has the wrong type for this command.");
            // Research prerequisites, edict costs, trade reputation/cooldowns and contract payments
            // remain the responsibility of the normal game command processor at the ordered tick.
            return proto;
        }
    }
}
