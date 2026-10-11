using System;

namespace CoiDedicated.Protocol;

/// <summary>Shared deadlines for loading an island while authoritative ticks are held.</summary>
public static class GameSessionTiming
{
    public static readonly TimeSpan JoinTimeout = TimeSpan.FromSeconds(600);

    // An already connected player must tolerate another player's entire join,
    // plus time for the server to release the barrier and deliver the next tick.
    public static readonly TimeSpan PeerIdleTimeout = JoinTimeout + TimeSpan.FromSeconds(15);

    // The client's download timer starts before the server's authenticated join
    // timer, so also allow the existing ten-second authentication window.
    public static readonly TimeSpan ClientJoinDownloadTimeout = JoinTimeout + TimeSpan.FromSeconds(10);
}
