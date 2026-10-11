using System.Text.Json;

namespace CoiDedicated.Manager.Core;

public sealed record ProfileInput(string Name, string GameDirectory, string Map, string BindAddress,
    int ControlPort, int GamePort, int AutosaveSeconds, bool StartingFactory);

public sealed record ServerProfile(string Id, string Name, string GameDirectory, string DataDirectory,
    string Map, string BindAddress, int ControlPort, int GamePort, int AutosaveSeconds,
    bool StartingFactory, DateTimeOffset CreatedUtc);

/// <summary>A container owns one island in its existing mounted data directory.</summary>
public sealed record ContainerProfileConfiguration(string GameDirectory, string DataDirectory,
    string Name = "New Haven", string Map = "New Haven", int AutosaveSeconds = 60, bool StartingFactory = true);

public sealed record SaveBackup(string Id, string ProfileId, string Name, long Bytes, string Sha256, DateTimeOffset CreatedUtc);

public enum ServerState { Stopped, Starting, Running, Paused, Stopping, Unresponsive, Faulted }

public sealed record ServerRuntimeSnapshot
{
    public string Id { get; init; } = "";
    public ServerState State { get; init; } = ServerState.Stopped;
    public bool IsOwned { get; init; }
    public int? ProcessId { get; init; }
    public DateTimeOffset? StartedUtc { get; init; }
    public DateTimeOffset? LastStatusUtc { get; init; }
    public string? LastError { get; init; }
    public int? ExitCode { get; init; }
    public int? Step { get; init; }
    public int? Players { get; init; }
    public bool? Joining { get; init; }
    public int? UptimeSeconds { get; init; }
    public long? MemoryBytes { get; init; }
    public double? CpuPercent { get; init; }
    public JsonElement? World { get; init; }
}

public sealed record ServerLogEntry(long Sequence, DateTimeOffset TimestampUtc, string Level, string Text);
