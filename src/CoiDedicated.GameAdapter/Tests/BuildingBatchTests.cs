using CoiDedicated.GameAdapter;
using CoiDedicated.Protocol;
using Mafi;
using Mafi.Collections.ImmutableCollections;
using Mafi.Core.Entities;
using Mafi.Core.Entities.Static.Commands;

internal static class BuildingBatchTests
{
    internal static void RejectMalformed()
    {
        var resolver = new DependencyResolverBuilder().BuildAndClear();
        byte[] Wire(int count = 1, int mode = 0, int x = 17, int y = 18, int z = 3, int rotation = 0, string id = "StorageUnit")
        {
            using var writer = new PayloadWriter(GameCommandCodec.MaxCommandBytes);
            writer.WriteUInt16(GameCommandCodec.Version);
            writer.WriteUInt16((ushort)GameCommandKind.BatchCreateBuildings);
            writer.WriteUInt16((ushort)mode);
            writer.WriteUInt16((ushort)count);
            for (int i = 0; i < Math.Min(Math.Max(count, 0), 1); i++)
            {
                writer.WriteString(id, GameCommandCodec.MaxProtoIdBytes);
                writer.WriteInt32(x); writer.WriteInt32(y); writer.WriteInt32(z); writer.WriteInt32(rotation);
                writer.WriteBoolean(false);
            }
            return writer.ToArray();
        }
        void Rejected(byte[] wire)
        {
            try { GameCommandCodec.Decode(wire, resolver); }
            catch (ProtocolException) { return; }
            throw new Exception("Invalid batch payload was accepted.");
        }
        foreach (byte[] malformed in new[] {
            Wire(count: 0), Wire(count: GameCommandCodec.MaxBuildingsPerBatch + 1), Wire(count: ushort.MaxValue),
            Wire(count: 2), Wire(mode: 3), Wire(mode: ushort.MaxValue), Wire(x: -1), Wire(y: int.MaxValue),
            Wire(z: short.MaxValue + 1), Wire(z: short.MinValue - 1), Wire(rotation: -1), Wire(rotation: 4),
            Wire(id: ""), Wire(id: "Bad\nId") }) Rejected(malformed);
        byte[] valid = Wire();
        for (int length = 0; length < valid.Length; length++) Rejected(valid.Take(length).ToArray());
        Rejected(valid.Concat(new byte[] { 0 }).ToArray());
        valid[^1] = 2;
        Rejected(valid);
        var empty = ImmutableArray<EntityConfigData>.Empty;
        foreach (var command in new[] {
            new BatchCreateStaticEntitiesCmd(empty),
            new BatchCreateStaticEntitiesCmd(empty, isFree: true),
            new BatchCreateStaticEntitiesCmd(empty, allowValidationSuppression: true),
            new BatchCreateStaticEntitiesCmd(empty, applyConfiguration: true) })
        {
            if (GameCommandCodec.TryEncode(command, out byte[] bytes, out string reason) || bytes.Length != 0 || string.IsNullOrWhiteSpace(reason))
                throw new Exception("Unsafe or empty batch was not rejected with an explanation.");
        }
    }
}
