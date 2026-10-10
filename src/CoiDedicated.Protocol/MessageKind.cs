namespace CoiDedicated.Protocol
{
    /// <summary>Stable wire identifiers. Payload semantics belong to the server and client.</summary>
    public enum MessageKind : ushort
    {
        Hello = 1,
        Welcome = 2,
        SnapshotBegin = 3,
        SnapshotChunk = 4,
        SnapshotEnd = 5,
        Command = 6,
        Tick = 7,
        Ack = 8,
        Error = 9,
        Ping = 10,
        CommandRejected = 11,
        PresenceUpdate = 12,
        PresenceSnapshot = 13,
        CheckpointReply = 14,
        ResyncRequired = 15
    }
}
