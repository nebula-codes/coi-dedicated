namespace CoiDedicated.Client;

/// <summary>Bounded retry policy; a briefly successful join does not reset a failure loop.</summary>
internal sealed class ReconnectPolicy
{
    internal const int MaxAttempts = 3;
    private static readonly double[] Delays = { 2, 5, 15 };
    private double? connectedSince;
    internal int Attempts { get; private set; }
    internal bool Waiting { get; private set; }
    internal double NextAttemptAt { get; private set; }

    internal bool Schedule(double now)
    {
        connectedSince = null;
        if (Attempts >= MaxAttempts) { Waiting = false; return false; }
        if (!Waiting) NextAttemptAt = now + Delays[Attempts];
        Waiting = true;
        return true;
    }

    internal bool TryStart(double now)
    {
        if (!Waiting || now < NextAttemptAt || Attempts >= MaxAttempts) return false;
        Waiting = false;
        Attempts++;
        return true;
    }

    internal void Connected(double now) { Waiting = false; connectedSince = now; }
    internal void ObserveHealthy(double now)
    {
        if (connectedSince.HasValue && now - connectedSince.Value >= 60) Attempts = 0;
    }
    internal void Cancel() { Waiting = false; Attempts = 0; connectedSince = null; }
}
