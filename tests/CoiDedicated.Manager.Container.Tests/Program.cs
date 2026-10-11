using System.Reflection;
using System.Diagnostics;
using CoiDedicated.Manager;
using CoiDedicated.Manager.Core;
using Microsoft.Extensions.Logging.Abstractions;

if (args.Contains("--serve", StringComparer.Ordinal)) return await ServerSupervisorTests.RunFixtureAsync(args);
if (!OperatingSystem.IsLinux()) { Console.WriteLine("SKIP: container lifecycle tests require Linux SIGTERM."); return 0; }

string scratch = Path.Combine(Path.GetTempPath(), "coi-manager-container-" + Guid.NewGuid().ToString("N"));
string data = Path.Combine(scratch, "data"), game = Path.Combine(scratch, "game"), metadata = Path.Combine(data, ".manager");
Directory.CreateDirectory(data);
Directory.CreateDirectory(Path.Combine(game, "Maps"));
Directory.CreateDirectory(Path.Combine(game, "Captain of Industry_Data", "Managed"));
foreach (string assembly in new[] { "Mafi", "Mafi.Core", "Mafi.Base" })
    File.WriteAllBytes(Path.Combine(game, "Captain of Industry_Data", "Managed", assembly + ".dll"), []);
foreach (string map in new[] { "native-running", "native-bootstrap" }) File.WriteAllText(Path.Combine(game, "Maps", map + ".map"), "fixture");
const string island = "preexisting-island-must-remain-in-place";
File.WriteAllText(Path.Combine(data, "island.save"), island);
File.WriteAllText(Path.Combine(data, "join.key"), "private-fixture-key");
var options = new ManagerOptions(scratch, metadata, Assembly.GetExecutingAssembly().Location, 28560, false,
    ContainerMode: true, GameDirectory: game, ServerDataDirectory: data, Map: "native-running");
ManagerService? service = null;
try
{
    service = NewService();
    await service.StartAsync(CancellationToken.None);
    string id = service.Snapshots().Single().Id;
    await Until(() => service.Snapshots().Single().State == ServerState.Running);
    Check(service.Snapshots().Single().ProcessId is not null, "First container start did not launch the island.");
    await StopManager();
    Check(new RequestedRunStateStore(metadata).ShouldRun(id), "Container stop cleared running intent.");
    Check(File.Exists(Path.Combine(data, "saved.txt")), "Container stop did not await final save.");
    Console.WriteLine("PASS first container start autostarts; manager shutdown waits for native final save and keeps restart intent");

    service = NewService();
    await service.StartAsync(CancellationToken.None);
    await Until(() => service.Snapshots().Single().State == ServerState.Running);
    await service.Action(id, "stop");
    Check(service.Snapshots().Single().State == ServerState.Stopped, "Explicit stop did not finish.");
    Check(!new RequestedRunStateStore(metadata).ShouldRun(id), "Explicit stop was not persisted.");
    await StopManager();
    service = NewService();
    await service.StartAsync(CancellationToken.None);
    await Task.Delay(400);
    Check(service.Snapshots().Single().State == ServerState.Stopped && service.Snapshots().Single().ProcessId is null,
        "A stopped island restarted on manager recreation.");
    Console.WriteLine("PASS explicit stop survives manager/container recreation without a restart loop");

    await service.Action(id, "start");
    await Until(() => service.Snapshots().Single().State == ServerState.Running);
    int before = service.Snapshots().Single().ProcessId!.Value;
    await service.Action(id, "restart");
    await Until(() => service.Snapshots().Single().State == ServerState.Running);
    Check(before != service.Snapshots().Single().ProcessId, "Restart reused a stopped process.");
    Check(new RequestedRunStateStore(metadata).ShouldRun(id), "Explicit start/restart did not persist running intent.");
    Console.WriteLine("PASS dashboard start and restart launch a new authenticated child and persist running intent");

    await service.Action(id, "stop");
    ServerProfile profile = service.Get(id).Profile;
    await service.Update(id, new(profile.Name, profile.GameDirectory, "native-bootstrap", profile.BindAddress,
        profile.ControlPort, profile.GamePort, profile.AutosaveSeconds, profile.StartingFactory));
    await service.Action(id, "start");
    await StopManager();
    Check(File.ReadAllText(Path.Combine(data, "island.save")) == island, "Manager moved or replaced the existing island.");
    Check(File.ReadAllText(Path.Combine(data, "join.key")) == "private-fixture-key", "Manager changed the existing join key.");
    Console.WriteLine("PASS manager shutdown cancels bootstrap before control readiness; existing island and join key remain intact");

    service = NewService();
    long priorLogLength = new FileInfo(service.Get(id).LogPath).Length;
    await service.StartAsync(CancellationToken.None);
    await Until(() => service.Snapshots().Single().ProcessId is not null && new FileInfo(service.Get(id).LogPath).Length > priorLogLength);
    using var finalChild = Process.GetProcessById(service.Snapshots().Single().ProcessId!.Value);
    bool failedVisibly = false;
    using (var tooShort = new CancellationTokenSource(TimeSpan.FromMilliseconds(100)))
    {
        try { await service.StopAsync(tooShort.Token); }
        catch (AggregateException error) when (error.InnerExceptions.Any(inner => inner is OperationCanceledException)) { failedVisibly = true; }
    }
    service.Dispose(); service = null;
    using (var exitDeadline = new CancellationTokenSource(TimeSpan.FromSeconds(10))) await finalChild.WaitForExitAsync(exitDeadline.Token);
    Check(failedVisibly, "A timed-out child shutdown was incorrectly reported as successful.");
    Console.WriteLine("PASS a child shutdown deadline failure propagates to the manager host instead of reporting a clean container exit");
    return 0;
}
catch (Exception error) { Console.Error.WriteLine(error); return 1; }
finally
{
    if (service is not null) await StopManager();
    string resolved = Path.GetFullPath(scratch);
    string temp = Path.TrimEndingDirectorySeparator(Path.GetFullPath(Path.GetTempPath())) + Path.DirectorySeparatorChar;
    if (resolved.StartsWith(temp, StringComparison.Ordinal) && Path.GetFileName(resolved).StartsWith("coi-manager-container-", StringComparison.Ordinal))
        Directory.Delete(resolved, recursive: true);
}

ManagerService NewService()
{
    var profiles = new ProfileStore(metadata, new(game, data, Map: options.Map));
    return new(options, profiles, new SaveLibrary(profiles), NullLogger<ManagerService>.Instance);
}
async Task StopManager()
{
    if (service is null) return;
    using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(20));
    await service.StopAsync(deadline.Token);
    Check(service.Snapshots().Single().ProcessId is null, "Manager exited before the server process.");
    service.Dispose();
    service = null;
}
static async Task Until(Func<bool> predicate)
{
    using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(20));
    while (!predicate()) await Task.Delay(50, deadline.Token);
}
static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
