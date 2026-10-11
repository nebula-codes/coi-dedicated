using System.Collections.Concurrent;
using System.Net;
using CoiDedicated.Manager.Core;

namespace CoiDedicated.Manager;

internal sealed class ManagerService(ManagerOptions options, ProfileStore profiles, SaveLibrary saves, ILogger<ManagerService> logger) : BackgroundService
{
    private readonly ConcurrentDictionary<string, ServerSupervisor> supervisors = new();
    private readonly ConcurrentDictionary<string, SemaphoreSlim> gates = new();
    private readonly ControlConnection control = new();
    private readonly RequestedRunStateStore requestedState = new(options.DataRoot, options.AutoStart);

    public ServerSupervisor Get(string id) => supervisors.GetOrAdd(id, key => new ServerSupervisor(profiles.Get(key), options.HostDll,
        Path.Combine(options.DataRoot, "runtime", key), preferSignalShutdown: options.ContainerMode));
    public ServerRuntimeSnapshot[] Snapshots() => profiles.List().Select(profile => Get(profile.Id).Snapshot).ToArray();

    public async Task<T> Serialized<T>(string id, Func<ServerSupervisor, Task<T>> action)
    {
        SemaphoreSlim gate = gates.GetOrAdd(id, _ => new(1));
        if (!await gate.WaitAsync(0)) throw new InvalidOperationException("Another operation is already running for this server. Wait for it to finish.");
        try { return await action(Get(id)); }
        finally { gate.Release(); }
    }

    public async Task<object> Action(string id, string action) => await Serialized(id, async server =>
    {
        switch (action)
        {
            case "start":
                profiles.ValidateForStart(server.Profile);
                if (options.ContainerMode) requestedState.Set(id, true);
                await server.StartAsync();
                break;
            case "stop":
                if (options.ContainerMode) requestedState.Set(id, false);
                await server.StopAsync();
                break;
            case "restart":
                profiles.ValidateForStart(server.Profile);
                if (options.ContainerMode) requestedState.Set(id, true);
                await server.RestartAsync();
                break;
            case "pause" or "resume" or "save": await server.ExecuteAsync(action); break;
            default: throw new ArgumentException("Unknown server action.");
        }
        return (object)new { message = action switch { "start" => "Server startup requested.", "stop" => "Server stopped.", "restart" => "Server restart requested.", "save" => "Island saved.", _ => $"{action} requested." } };
    });

    public async Task<ServerProfile> Update(string id, ProfileInput input) => await Serialized(id, async server =>
    {
        await server.RefreshAsync();
        RequireStopped(server);
        ServerProfile changed = profiles.Update(id, input);
        await server.DisposeAsync();
        supervisors.TryRemove(id, out _);
        await Get(id).InitializeAsync();
        return changed;
    });

    public async Task<SaveBackup> Backup(string id, string? name) => await Serialized(id, async server =>
    {
        await server.RefreshAsync();
        ServerProfile profile = server.Profile;
        if (server.Snapshot.State is ServerState.Running or ServerState.Paused)
            return await saves.CaptureAsync(id, name ?? "Manual backup", async (path, token) =>
            {
                await control.DownloadSnapshotAsync(ControlHost(profile), profile.ControlPort,
                    Path.Combine(profile.DataDirectory, "control.key"), path, token);
            });
        RequireStopped(server);
        return await saves.BackupCurrentAsync(id, name);
    });

    public async Task<SaveBackup> Import(string id, string name, Stream input) => await Serialized(id, async server =>
    {
        await server.RefreshAsync();
        RequireStopped(server);
        return await saves.ImportAsync(id, name, input);
    });

    public async Task<object> Restore(string id, string backupId) => await Serialized(id, async server =>
    {
        await server.RefreshAsync();
        RequireStopped(server);
        SaveBackup? previous = await saves.RestoreAsync(id, backupId);
        return (object)new { message = "Backup restored. Start the server to load it.", previousBackup = previous };
    });

    private static void RequireStopped(ServerSupervisor server)
    {
        if (server.Snapshot.ProcessId is not null || server.Snapshot.State is not (ServerState.Stopped or ServerState.Faulted))
            throw new InvalidOperationException("Stop this server before changing settings or replacing its island.");
    }

    internal static string ControlHost(ServerProfile profile) => IPAddress.Parse(profile.BindAddress).Equals(IPAddress.Any) ? "127.0.0.1"
        : IPAddress.Parse(profile.BindAddress).Equals(IPAddress.IPv6Any) ? "::1" : profile.BindAddress;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        foreach (ServerProfile profile in profiles.List())
        {
            try
            {
                await Serialized(profile.Id, async server =>
                {
                    await server.InitializeAsync(stoppingToken);
                    if (options.ContainerMode)
                    {
                        bool shouldRun = requestedState.ShouldRun(profile.Id);
                        requestedState.Set(profile.Id, shouldRun);
                        if (shouldRun && server.Snapshot.ProcessId is null)
                        {
                            profiles.ValidateForStart(profile);
                            await server.StartAsync(stoppingToken);
                        }
                        else if (!shouldRun && server.Snapshot.ProcessId is not null) await server.StopAsync(stoppingToken);
                    }
                    return true;
                });
            }
            catch (Exception error) { logger.LogWarning(error, "Could not reconnect server {Id}", profile.Id); }
        }
        while (!stoppingToken.IsCancellationRequested)
        {
            await Task.WhenAll(profiles.List().Select(async profile =>
            {
                try { await Get(profile.Id).RefreshAsync(stoppingToken); }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
                catch (Exception error) { logger.LogWarning(error, "Could not refresh server {Id}", profile.Id); }
            }));
            try { await Task.Delay(2000, stoppingToken); } catch (OperationCanceledException) { break; }
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        await base.StopAsync(cancellationToken);
        List<Exception>? shutdownErrors = null;
        foreach (ServerSupervisor server in supervisors.Values)
        {
            // Container lifetime owns its child; desktop manager lifetime deliberately does not.
            // Stopping the container preserves run intent so an island resumes on redeployment.
            if (options.ContainerMode)
            {
                try { await server.StopAsync(cancellationToken); }
                catch (Exception error)
                {
                    logger.LogError(error, "Could not finish graceful shutdown for server {Id}", server.Profile.Id);
                    (shutdownErrors ??= []).Add(error);
                }
            }
            await server.DisposeAsync();
        }
        if (shutdownErrors is not null)
            throw new AggregateException("Manager shutdown could not confirm every server's final save and process exit.", shutdownErrors);
    }
}
