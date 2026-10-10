using System.Buffers.Binary;
using CoiDedicated.Protocol;

internal static class PresenceCodecTests
{
    public static async Task Run()
    {
        var quad = new[] { new PresencePoint(10.5f, 2, 5), new PresencePoint(25, 2, 5), new PresencePoint(25, 2, 12), new PresencePoint(10.5f, 2, 12) };
        var state = new PlayerPresenceState
        {
            Visible = true, Center = new PresencePoint(-12.25f, 0, 98), ViewCorners = quad,
            Previews = Enumerable.Range(0, PresenceCodec.MaxPreviews).Select(i => new PresencePreview { Prototype = new string('X', 96), Corners = quad }).ToArray(),
            PreviewsTruncated = true
        };
        var update = PresenceCodec.EncodeUpdate(state);
        Check(update.Payload.Length < PresenceCodec.MaxUpdateBytes, "Maximum update does not fit.");
        var decoded = PresenceCodec.DecodeUpdate(update.Payload);
        Check(decoded.Center.X == -12.25f && decoded.Previews.Length == 32 && decoded.PreviewsTruncated && decoded.Previews[0].Corners[2].Z == 12, "Update changed coordinates or flags.");
        var entries = Enumerable.Range(1, 8).Select(i => new PlayerPresenceEntry { PeerId = (uint)i, Name = "Player_" + i, State = state }).ToArray();
        var snapshot = PresenceCodec.EncodeSnapshot(entries);
        Check(snapshot.Payload.Length < PresenceCodec.MaxSnapshotBytes, "Maximum snapshot does not fit.");
        using var stream = new MemoryStream();
        await FrameCodec.WriteAsync(stream, update);
        await FrameCodec.WriteAsync(stream, snapshot);
        stream.Position = 0;
        Check((await FrameCodec.ReadAsync(stream))!.Kind == MessageKind.PresenceUpdate, "Update frame rejected.");
        var wireSnapshot = (await FrameCodec.ReadAsync(stream))!;
        Check(wireSnapshot.Kind == MessageKind.PresenceSnapshot && PresenceCodec.DecodeSnapshot(wireSnapshot.Payload)[7].Name == "Player_8", "Snapshot frame failed.");

        Check(!PresenceCodec.DecodeUpdate(PresenceCodec.EncodeUpdate(new PlayerPresenceState()).Payload).Visible, "Hidden state failed.");
        Check(PresenceCodec.DecodeSnapshot(PresenceCodec.EncodeSnapshot(Array.Empty<PlayerPresenceEntry>()).Payload).Length == 0, "Empty snapshot cannot clear players.");
        Reject(() => PresenceCodec.DecodeUpdate(new byte[PresenceCodec.MaxUpdateBytes + 1]));
        Reject(() => PresenceCodec.DecodeSnapshot(new byte[PresenceCodec.MaxSnapshotBytes + 1]));
        Reject(() => PresenceCodec.DecodeUpdate(update.Payload.ToArray().Concat(new byte[] { 0 }).ToArray()));
        Reject(() => PresenceCodec.DecodeUpdate(update.Payload.Slice(0, update.Payload.Length - 1)));
        Reject(() => PresenceCodec.DecodeUpdate(new byte[] { 2, 0, 0 }));
        foreach (float bad in new[] { float.NaN, float.PositiveInfinity, float.NegativeInfinity, PresenceCodec.MaxCoordinate + 1 })
        {
            byte[] corrupted = update.Payload.ToArray();
            BinaryPrimitives.WriteInt32LittleEndian(corrupted.AsSpan(3), BitConverter.SingleToInt32Bits(bad));
            Reject(() => PresenceCodec.DecodeUpdate(corrupted));
            Reject(() => PresenceCodec.EncodeUpdate(new PlayerPresenceState { Visible = true, Center = new PresencePoint(bad, 0, 0) }));
        }
        using (var tooMany = new PayloadWriter())
        {
            tooMany.WriteUInt16(1); tooMany.WriteUInt16(9);
            Reject(() => PresenceCodec.DecodeSnapshot(tooMany.ToArray()));
        }
        state.Previews = Enumerable.Repeat(state.Previews[0], 33).ToArray();
        Reject(() => PresenceCodec.EncodeUpdate(state));
        state.Previews = Array.Empty<PresencePreview>();
        state.ViewCorners = new PresencePoint[3];
        Reject(() => PresenceCodec.EncodeUpdate(state));
        state.ViewCorners = quad;
        state.Previews = new[] { new PresencePreview { Prototype = "<color=red>fake</color>", Corners = quad } };
        Reject(() => PresenceCodec.EncodeUpdate(state));
        state.Previews = Array.Empty<PresencePreview>();
        entries[1].PeerId = entries[0].PeerId;
        Reject(() => PresenceCodec.EncodeSnapshot(entries));
        entries[1].PeerId = 2;
        entries[0].Name = "server";
        Reject(() => PresenceCodec.EncodeSnapshot(entries));
        entries[0].Name = "Fake\nName";
        Reject(() => PresenceCodec.EncodeSnapshot(entries));
        entries[0].Name = "Legit";
        entries[0].PeerId = 0;
        Reject(() => PresenceCodec.EncodeSnapshot(entries));

        // Identity is absent from client updates. Appending an identity cannot forge the server's envelope.
        Reject(() => PresenceCodec.DecodeUpdate(update.Payload.ToArray().Concat(new byte[] { 1, 0, 0, 0 }).ToArray()));
    }

    private static void Check(bool value, string message) { if (!value) throw new Exception(message); }
    private static void Reject(Action action)
    {
        try { action(); }
        catch (ProtocolException) { return; }
        throw new Exception("Malformed presence input was accepted.");
    }
}
