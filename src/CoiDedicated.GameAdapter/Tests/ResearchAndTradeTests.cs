using CoiDedicated.GameAdapter;
using CoiDedicated.Protocol;
using Mafi;
using Mafi.Core.Input;
using Mafi.Core.Research;
using Mafi.Core.World;

internal static class ResearchAndTradeTests
{
    internal static void Run()
    {
        var resolver = new DependencyResolverBuilder().BuildAndClear();
        foreach (IInputCommand command in new IInputCommand[] { new ResearchStopCmd(), new FleetLoadCrewCmd() })
        {
            if (!GameCommandCodec.TryEncode(command, out byte[] wire, out string reason)) throw new Exception(reason);
            var decoded = GameCommandCodec.Decode(wire, resolver);
            if (decoded.GetType() != command.GetType() || decoded.IsProcessed) throw new Exception("Scalar command round trip failed.");
            Reject(() => GameCommandCodec.Decode(wire.Concat(new byte[] { 0 }).ToArray(), resolver));
        }
        // These commands require world prototypes; malformed strings must fail before resolver access.
        foreach (ushort kind in new ushort[] { 40, 41, 43, 44, 45 })
        {
            using var writer = new PayloadWriter();
            writer.WriteUInt16(GameCommandCodec.Version); writer.WriteUInt16(kind);
            writer.WriteString("bad\nproto");
            if (kind == 41) writer.WriteBoolean(true);
            Reject(() => GameCommandCodec.Decode(writer.ToArray(), resolver));
        }
        if (GameCommandCodec.TryEncode(new ResearchStartCmd(new ResearchNodeProto.ID("")), out _))
            throw new Exception("Empty research prototype accepted.");
    }
    private static void Reject(Action action)
    {
        try { action(); } catch (ProtocolException) { return; }
        throw new Exception("Expected ProtocolException.");
    }
}
