using System;

namespace CoiDedicated.Protocol;

/// <summary>Checkpoint identity binds a digest to an exact authoritative tick and simulation step.</summary>
public static class CheckpointCodec
{
    public static ProtocolFrame Encode(ulong sequence, int step, byte[] digest)
    {
        Validate(sequence, step, digest);
        using var writer = new PayloadWriter();
        writer.WriteUInt64(sequence); writer.WriteInt32(step); writer.WriteBytes(digest, 32);
        return writer.ToFrame(MessageKind.CheckpointReply);
    }

    public static (ulong Sequence, int Step, byte[] Digest) Decode(ReadOnlyMemory<byte> payload)
    {
        var reader = new PayloadReader(payload);
        ulong sequence = reader.ReadUInt64(); int step = reader.ReadInt32(); byte[] digest = reader.ReadBytes(32);
        reader.EnsureEnd(); Validate(sequence, step, digest);
        return (sequence, step, digest);
    }

    private static void Validate(ulong sequence, int step, byte[] digest)
    {
        if (sequence == 0 || step < 0 || digest == null || digest.Length != 32)
            throw new ProtocolException("Invalid gameplay checkpoint.");
    }
}
