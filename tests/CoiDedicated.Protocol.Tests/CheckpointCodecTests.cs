using CoiDedicated.Protocol;

internal static class CheckpointCodecTests
{
    public static Task Run()
    {
        var digest = Enumerable.Range(0, 32).Select(i => (byte)i).ToArray();
        var frame = CheckpointCodec.Encode(77, 1234, digest);
        var decoded = CheckpointCodec.Decode(frame.Payload);
        if (decoded.Sequence != 77 || decoded.Step != 1234 || !decoded.Digest.SequenceEqual(digest)) throw new Exception("Checkpoint changed identity or digest.");
        Reject(() => CheckpointCodec.Encode(0, 1, digest));
        Reject(() => CheckpointCodec.Encode(1, -1, digest));
        Reject(() => CheckpointCodec.Encode(1, 1, new byte[31]));
        Reject(() => CheckpointCodec.Decode(frame.Payload.Slice(0, frame.Payload.Length - 1)));
        Reject(() => CheckpointCodec.Decode(frame.Payload.ToArray().Concat(new byte[1]).ToArray()));
        return Task.CompletedTask;
    }
    private static void Reject(Action action)
    {
        try { action(); } catch (ProtocolException) { return; }
        throw new Exception("Malformed checkpoint was accepted.");
    }
}
