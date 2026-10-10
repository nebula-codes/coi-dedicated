using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text.Json;
using CoiDedicated.Manager.Core;

public static class ServerSupervisorTests
{
    public static async Task RunAsync()
    {
        string directory = Path.Combine(Path.GetTempPath(), "coi-manager-process-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        string host = Assembly.GetExecutingAssembly().Location;
        ServerSupervisor? supervisor = null;
        try
        {
            var profile = Profile(directory, "normal");
            string runtime = Path.Combine(directory, "runtime");
            supervisor = new ServerSupervisor(profile, host, runtime, new ControlConnection(TimeSpan.FromSeconds(1)));
            await supervisor.StartAsync();
            int processId = supervisor.Snapshot.ProcessId!.Value;
            if (supervisor.Snapshot.State != ServerState.Starting) throw new Exception("Spawn was reported ready before authentication.");
            await UntilReadyAsync(supervisor);
            if (supervisor.Snapshot.Step != 42 || !supervisor.Snapshot.IsOwned) throw new Exception("Authenticated status was not applied.");
            Console.WriteLine("PASS supervisor authenticates readiness and captures process identity");

            await supervisor.DisposeAsync();
            supervisor = new ServerSupervisor(profile, host, runtime, new ControlConnection(TimeSpan.FromSeconds(1)));
            await supervisor.InitializeAsync();
            if (supervisor.Snapshot.ProcessId != processId || supervisor.Snapshot.State != ServerState.Running || !supervisor.Snapshot.IsOwned)
                throw new Exception("Manager did not reattach to its detached server.");
            Console.WriteLine("PASS server survives manager detach and reconnects by PID plus start time");

            await supervisor.StopAsync();
            if (supervisor.Snapshot.State != ServerState.Stopped || supervisor.Snapshot.ProcessId is not null || !File.Exists(Path.Combine(profile.DataDirectory, "saved.txt")))
                throw new Exception("Graceful stop did not wait for the fixture's final save and process exit.");
            Console.WriteLine("PASS graceful stop waits for the save and actual process exit");
            await supervisor.DisposeAsync(); supervisor = null;

            profile = Profile(Path.Combine(directory, "cancel"), "slow-ready");
            supervisor = new ServerSupervisor(profile, host, Path.Combine(directory, "cancel-runtime"), new ControlConnection(TimeSpan.FromSeconds(5)));
            await supervisor.StartAsync();
            await supervisor.StopAsync();
            if (supervisor.Snapshot.State != ServerState.Stopped || !File.Exists(Path.Combine(profile.DataDirectory, "saved.txt"))) throw new Exception("Initialization cancellation did not wait for a graceful stop.");
            Console.WriteLine("PASS stopping during initialization waits for readiness and a final save");
            await supervisor.DisposeAsync(); supervisor = null;

            profile = Profile(Path.Combine(directory, "unresponsive"), "drop-shutdown-once");
            supervisor = new ServerSupervisor(profile, host, Path.Combine(directory, "unresponsive-runtime"), new ControlConnection(TimeSpan.FromSeconds(1)));
            await supervisor.StartAsync(); await UntilReadyAsync(supervisor);
            bool rejected = false;
            try { await supervisor.StopAsync(); }
            catch (IOException) { rejected = true; }
            if (!rejected || supervisor.Snapshot.State != ServerState.Unresponsive || supervisor.Snapshot.ProcessId is null)
                throw new Exception("A failed graceful shutdown did not retain the live process.");
            using (var process = Process.GetProcessById(supervisor.Snapshot.ProcessId.Value))
                if (process.HasExited) throw new Exception("The running island was force-killed.");
            await supervisor.StopAsync();
            Console.WriteLine("PASS an unresponsive ready server is never silently force-killed");
            await supervisor.DisposeAsync(); supervisor = null;

            profile = Profile(Path.Combine(directory, "mismatch"), "normal");
            runtime = Path.Combine(directory, "mismatch-runtime"); Directory.CreateDirectory(runtime);
            using (var current = Process.GetCurrentProcess())
            {
                await File.WriteAllTextAsync(Path.Combine(runtime, "process.json"), JsonSerializer.Serialize(new
                {
                    ProfileId = profile.Id, ProcessId = current.Id, StartTimeUtcTicks = current.StartTime.ToUniversalTime().Ticks - 1,
                    HostDllPath = host, DataDirectory = profile.DataDirectory, WasReady = true, StopRequested = false
                }));
            }
            supervisor = new ServerSupervisor(profile, host, runtime);
            await supervisor.InitializeAsync();
            if (supervisor.Snapshot.IsOwned || supervisor.Snapshot.ProcessId is not null)
                throw new Exception("Mismatched PID start time was accepted as an owned process.");
            await supervisor.StopAsync();
            Console.WriteLine("PASS stale process metadata cannot attach to an unrelated process");
            await supervisor.DisposeAsync(); supervisor = null;

            profile = Profile(Path.Combine(directory, "occupied"), "normal");
            var occupied = new TcpListener(IPAddress.Loopback, profile.ControlPort); occupied.Start();
            try
            {
                supervisor = new ServerSupervisor(profile, host, Path.Combine(directory, "occupied-runtime"));
                bool prevented = false;
                try { await supervisor.StartAsync(); }
                catch (InvalidOperationException) { prevented = true; }
                if (!prevented || supervisor.Snapshot.ProcessId is not null) throw new Exception("An occupied server port did not prevent startup.");
            }
            finally { occupied.Stop(); }
            Console.WriteLine("PASS occupied ports prevent duplicate startup");
            await supervisor!.DisposeAsync(); supervisor = null;

            profile = Profile(Path.Combine(directory, "logs"), "log-burst");
            supervisor = new ServerSupervisor(profile, host, Path.Combine(directory, "logs-runtime"));
            await supervisor.StartAsync(); await UntilReadyAsync(supervisor);
            await supervisor.RefreshAsync();
            var lines = supervisor.GetLogs(0, 5000);
            if (lines.Count != 2000 || lines.Zip(lines.Skip(1)).Any(pair => pair.First.Sequence >= pair.Second.Sequence))
                throw new Exception("The bounded server log did not retain ordered recent entries.");
            await supervisor.StopAsync();
            Console.WriteLine("PASS server log history stays bounded and ordered");
        }
        finally
        {
            if (supervisor is not null)
            {
                if (supervisor.Snapshot.ProcessId is not null)
                {
                    try { await supervisor.StopAsync(); }
                    catch (Exception exception) { Console.Error.WriteLine("Fixture shutdown failed: " + exception.Message); }
                }
                await supervisor.DisposeAsync();
            }
            string resolved = Path.GetFullPath(directory);
            string temp = Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if (resolved.StartsWith(temp, StringComparison.OrdinalIgnoreCase) && Path.GetFileName(resolved).StartsWith("coi-manager-process-", StringComparison.Ordinal))
                Directory.Delete(resolved, recursive: true);
        }
    }

    public static async Task<int> RunFixtureAsync(string[] args)
    {
        string Value(string key) => args[Array.IndexOf(args, key) + 1];
        string data = Value("--data-dir"), log = Value("--log-file"), mode = Value("--map");
        Directory.CreateDirectory(data); Directory.CreateDirectory(Path.GetDirectoryName(log)!);
        await File.AppendAllTextAsync(log, "fixture bootstrap\n");
        if (mode == "slow-ready") await Task.Delay(500);
        if (mode == "log-burst") await File.AppendAllTextAsync(log, string.Concat(Enumerable.Range(1, 3000).Select(number => $"fixture log line {number}\n")));
        const string secret = "manager-fixture-secret-not-real";
        await File.WriteAllTextAsync(Path.Combine(data, "control.key"), secret);
        var listener = new TcpListener(IPAddress.Loopback, int.Parse(Value("--port"))); listener.Start();
        await File.AppendAllTextAsync(log, "[server] Simulation continues with no players. Joining briefly holds ticks while the snapshot loads.\n");
        bool dropped = false;
        try
        {
            while (true)
            {
                using var client = await listener.AcceptTcpClientAsync();
                await using var stream = client.GetStream();
                string command;
                try { command = await ControlConnectionTests.AuthenticateAsync(stream, secret); }
                catch (IOException) { continue; }
                if (command == "shutdown")
                {
                    if (mode == "drop-shutdown-once" && !dropped) { dropped = true; continue; }
                    await ControlConnectionTests.AckAsync(stream, 1, "Saving and shutting down");
                    await Task.Delay(250);
                    await File.WriteAllTextAsync(Path.Combine(data, "saved.txt"), "saved before exit");
                    return 0;
                }
                await ControlConnectionTests.AckAsync(stream, 1, "{\"step\":42,\"paused\":false,\"players\":0,\"joining\":false,\"uptimeSeconds\":1,\"world\":{\"Population\":90}}");
            }
        }
        finally { listener.Stop(); }
    }

    private static ServerProfile Profile(string directory, string mode)
    {
        Directory.CreateDirectory(directory);
        int control = FreePort(), game;
        do { game = FreePort(); } while (control == game);
        return new ServerProfile(Guid.NewGuid().ToString("N"), "Test island", directory, Path.Combine(directory, "data"), mode, "127.0.0.1", control, game, 60, true, DateTimeOffset.UtcNow);
    }

    private static int FreePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start();
        try { return ((IPEndPoint)listener.LocalEndpoint).Port; }
        finally { listener.Stop(); }
    }

    private static async Task UntilReadyAsync(ServerSupervisor supervisor)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        while (true)
        {
            await supervisor.RefreshAsync(timeout.Token);
            if (supervisor.Snapshot.State == ServerState.Running) return;
            if (supervisor.Snapshot.State == ServerState.Faulted) throw new Exception(supervisor.Snapshot.LastError);
            await Task.Delay(100, timeout.Token);
        }
    }
}
