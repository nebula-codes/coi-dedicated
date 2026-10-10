using System;
using System.Collections.Concurrent;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using BepInEx;
using HarmonyLib;
using Mafi;
using Mafi.Collections.ImmutableCollections;
using Mafi.Core;
using Mafi.Core.Game;
using Mafi.Core.GameLoop;
using Mafi.Core.Simulation;
using Mafi.Unity;
using Mafi.UnityCore;
using UnityEngine;

namespace CoiDedicated.Client;

[BepInPlugin(PluginId, "CoI Dedicated Client Prototype", "0.2.0")]
[BepInProcess("Captain of Industry.exe")]
public sealed class SnapshotPreviewPlugin : BaseUnityPlugin
{
    private const string PluginId = "local.coi.dedicated.client";
    private const string PreviewLabel = "SNAPSHOT PREVIEW — PAUSED\nMultiplayer synchronization not connected";
    private static readonly FieldInfo MainField = typeof(GameMainMb).GetField("m_main", BindingFlags.Instance | BindingFlags.NonPublic)!;
    private readonly ConcurrentQueue<Action> mainThread = new ConcurrentQueue<Action>();
    private readonly Harmony harmony = new Harmony(PluginId);
    private Main? main;
    private CancellationTokenSource? download;
    private LiveSession? pendingLive;
    private LiveGameBridge? liveBridge;
    private bool joiningLive;
    private bool autoJoin;
    private bool autoJoinAttempted;
    private int testTicks;
    private bool testCommands;
    private string buildIdentity = "";
    private string clientName = "";
    private StreamWriter? gameLog;
    private string host = "127.0.0.1";
    private string port = "28551";
    private string keyPath = "";
    private string status = "Connect from the main menu. Live port 28551 uses join.key; control snapshot port 28550 uses control.key.";
    private bool visible;
    private bool patchReady;
    private static bool pauseGuardReady;
    private float nextDiscovery;
    private Rect window = new Rect(30, 85, 680, 340);
    internal static bool PreviewActive { get; private set; }

    private void Awake()
    {
        try
        {
            string gameLogPath = Path.Combine(Paths.BepInExRootPath, "coi-client-game-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture) + ".log");
            gameLog = new StreamWriter(new FileStream(gameLogPath, FileMode.CreateNew, FileAccess.Write, FileShare.Read)) { AutoFlush = true };
            Mafi.Log.LogReceived += OnGameLog;
            buildIdentity = CoiDedicated.GameAdapter.GameBuildIdentity.Compute(Path.GetDirectoryName(typeof(Mafi.Core.Input.InputScheduler).Assembly.Location)!);
            Logger.LogInfo("Game diagnostic log: " + gameLogPath);
            host = Config.Bind("Connection", "Host", "127.0.0.1", "Dedicated gameplay server host.").Value;
            port = Config.Bind("Connection", "Port", 28551, "Dedicated gameplay server port.").Value.ToString(CultureInfo.InvariantCulture);
            keyPath = Config.Bind("Connection", "JoinKeyFile", "", "Path to join.key; key contents are never saved in this config.").Value;
            clientName = Config.Bind("Connection", "ClientName", "", "Optional name; empty chooses a random client ID.").Value;
            autoJoin = Config.Bind("Connection", "AutoJoinOnce", false, "Join once when the main menu is ready. Disabled by default.").Value;
            foreach (string argument in Environment.GetCommandLineArgs())
            {
                if (argument == "--coi-dedicated-auto-join") autoJoin = true;
                else if (argument == "--coi-dedicated-test-commands") testCommands = true;
                else if (argument.StartsWith("--coi-dedicated-host=", StringComparison.Ordinal)) host = argument.Substring(21);
                else if (argument.StartsWith("--coi-dedicated-port=", StringComparison.Ordinal)) port = argument.Substring(21);
                else if (argument.StartsWith("--coi-dedicated-key-file=", StringComparison.Ordinal)) keyPath = argument.Substring(25);
                else if (argument.StartsWith("--coi-dedicated-name=", StringComparison.Ordinal)) clientName = argument.Substring(21);
                else if (argument.StartsWith("--coi-dedicated-test-ticks=", StringComparison.Ordinal))
                    testTicks = int.Parse(argument.Substring("--coi-dedicated-test-ticks=".Length), CultureInfo.InvariantCulture);
            }
            harmony.Patch(AccessTools.PropertyGetter(typeof(SimLoopEvents), nameof(SimLoopEvents.IsSimPaused)),
                postfix: new HarmonyMethod(typeof(SnapshotPreviewPlugin), nameof(ForcePaused)));
            harmony.Patch(AccessTools.Method(typeof(SimLoopEvents), nameof(SimLoopEvents.SetSimPause)),
                prefix: new HarmonyMethod(typeof(SnapshotPreviewPlugin), nameof(RejectUnpause)));
            LiveGameBridge.Install(harmony);
            SnapshotTypeCompatibility.Install(harmony);
            patchReady = true;
            Logger.LogInfo("Dedicated client prototype loaded. Press F8 to join; supported actions are limited by the network codec.");
        }
        catch (Exception error)
        {
            status = "Cannot guarantee paused preview; connection disabled: " + error.Message;
            Logger.LogError(error);
        }
    }

    private static void ForcePaused(ref bool __result)
    {
        if (PreviewActive && pauseGuardReady) __result = true;
    }

    private static void RejectUnpause(ref bool isPaused)
    {
        if (PreviewActive && pauseGuardReady) isPaused = true;
    }

    private void Update()
    {
        if (Input.GetKeyDown(KeyCode.F8)) visible = !visible;
        while (mainThread.TryDequeue(out Action action))
        {
            try { action(); }
            catch (Exception error) { Fail(error); }
        }
        if (main != null && !main.IsInitializingScene) liveBridge?.Pump();
        if (main == null && Time.unscaledTime >= nextDiscovery)
        {
            nextDiscovery = Time.unscaledTime + 1;
            try
            {
                GameMainMb component = UnityEngine.Object.FindAnyObjectByType<GameMainMb>();
                if (component == null || MainField == null) return;
                var found = (Option<Main>)MainField.GetValue(component);
                if (found.IsNone) return;
                main = found.Value;
                main.ConfigsCreated += OnConfigs;
                main.ResolverCreated += OnResolver;
                main.SceneInitializationFinished += OnSceneFinished;
            }
            catch (Exception error) { Fail(error); }
        }
        if (autoJoin && !autoJoinAttempted && patchReady && main != null && !main.IsInitializingScene && IsMainMenu(main))
        {
            autoJoinAttempted = true;
            Logger.LogInfo("Main menu ready; attempting opt-in dedicated autojoin.");
            StartDownload(true);
        }
    }

    private void OnConfigs(ImmutableArray<IConfig> configs)
    {
        if (!PreviewActive && !joiningLive) return;
        foreach (IConfig config in configs)
            if (config is IGameRunnerConfig runner) runner.DisableSimulationBackgroundThread = true;
    }

    private void OnResolver(DependencyResolver resolver)
    {
        // GameRunner.Initialize requires an unpaused state on entry. Loaded games
        // pause themselves during initialization, before their ten UI warmup updates.
    }

    private void OnSceneFinished(Option<IGameScene> scene)
    {
        SnapshotTypeCompatibility.LoadingDedicatedSnapshot = false;
        bool gameplay = scene.HasValue && scene.Value.GetType().FullName == "Mafi.Unity.GamePlayScene";
        if (joiningLive)
        {
            joiningLive = false;
            if (gameplay && pendingLive != null && scene.Value.Resolver.ValueOrNull is DependencyResolver loadedResolver)
            {
                try
                {
                    liveBridge = new LiveGameBridge(loadedResolver, pendingLive, message => { status = message; Logger.LogWarning(message); }, testTicks, testCommands);
                    pendingLive = null;
                    status = "Joined dedicated island. Experimental synchronized ticks; unsupported actions are blocked.";
                    Logger.LogInfo(status);
                }
                catch (Exception error)
                {
                    pendingLive?.Dispose(); pendingLive = null;
                    PreviewActive = true; pauseGuardReady = true;
                    if (loadedResolver.TryResolve(out SimLoopEvents simulation)) simulation.SetSimPause(true);
                    Fail(error);
                }
            }
            else
            {
                pendingLive?.Dispose(); pendingLive = null;
                status = "Dedicated island failed to load. See the game's load error and BepInEx log.";
                Logger.LogError(status);
            }
            return;
        }
        if (!gameplay && liveBridge != null) { liveBridge.Dispose(); liveBridge = null; }
        if (!PreviewActive) return;
        if (gameplay)
        {
            pauseGuardReady = true;
            if (scene.Value.Resolver.ValueOrNull is DependencyResolver resolver
                && resolver.TryResolve(out SimLoopEvents simulation)) simulation.SetSimPause(true);
            status = "Snapshot loaded and held paused. Multiplayer synchronization not connected.";
            Logger.LogInfo(status);
        }
        else
        {
            PreviewActive = false;
            pauseGuardReady = false;
            status = "Preview ended or failed to load. See the game error screen and BepInEx log.";
        }
    }

    private void OnGUI()
    {
        if (PreviewActive) GUI.Box(new Rect(10, 10, 580, 58), PreviewLabel);
        else if (liveBridge != null) GUI.Box(new Rect(10, 10, 650, 58), liveBridge.IsFrozen
            ? "DEDICATED CONNECTION STOPPED — ISLAND FROZEN\nReturn to the main menu to reconnect. F8 for details."
            : $"DEDICATED MULTIPLAYER PROTOTYPE — STEP {liveBridge.CurrentStep}\nSupported actions only; F8 for connection details.");
        if (visible) window = GUI.Window(0xC01D, window, DrawWindow, "Dedicated island client prototype (F8)");
    }

    private void DrawWindow(int id)
    {
        GUILayout.Label("Live join uses port 28551 + join.key. Snapshot preview uses port 28550 + control.key.");
        GUILayout.Label("Server host"); host = GUILayout.TextField(host, 255);
        GUILayout.BeginHorizontal();
        GUILayout.Label("Port", GUILayout.Width(45)); port = GUILayout.TextField(port, 5);
        GUILayout.EndHorizontal();
        GUILayout.Label("Shared key file path (key contents are never shown)");
        keyPath = GUILayout.TextField(keyPath, 4096);
        GUI.enabled = patchReady && download == null && main != null && !main.IsInitializingScene && liveBridge == null;
        if (GUILayout.Button("Join live dedicated island (experimental)")) StartDownload(true);
        if (GUILayout.Button("Download paused snapshot preview (control endpoint)")) StartDownload(false);
        GUI.enabled = true;
        if (download != null && GUILayout.Button("Cancel download")) download.Cancel();
        if (liveBridge != null && !liveBridge.IsFrozen && GUILayout.Button("Disconnect and freeze this island")) liveBridge.Freeze("Disconnected by player.");
        GUILayout.Label(status);
        GUI.DragWindow(new Rect(0, 0, 650, 25));
    }

    private void StartDownload(bool live)
    {
        try
        {
            if (main == null || main.IsInitializingScene) throw new InvalidOperationException("Game is still initializing.");
            if (!PreviewActive && !IsMainMenu(main))
                throw new InvalidOperationException("Return to the main menu before connecting; the current island will not be replaced.");
            if (string.IsNullOrWhiteSpace(host)) throw new ArgumentException("Enter a server hostname.");
            if (!int.TryParse(port, NumberStyles.None, CultureInfo.InvariantCulture, out int number) || number < 1 || number > 65535)
                throw new ArgumentException("Port must be from 1 to 65535.");
            string key = Path.GetFullPath(keyPath);
            string group = "Dedicated Snapshot " + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture) + "-" + Guid.NewGuid().ToString("N").Substring(0, 8);
            string directory = main.FileSystemHelper.GetDirPath(FileType.GameSave, true, group);
            string path = Path.Combine(directory, "server-snapshot.save");
            var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(120));
            download = deadline;
            status = "Authenticating and downloading snapshot…";
            string server = host.Trim();
            _ = Task.Run(async () =>
            {
                try
                {
                    LiveSession? session = live ? await LiveSession.JoinAsync(server, number, key, path, buildIdentity, deadline.Token, clientName).ConfigureAwait(false) : null;
                    int step = session?.SnapshotStep ?? await SnapshotDownload.DownloadAsync(server, number, key, path, buildIdentity, deadline.Token).ConfigureAwait(false);
                    mainThread.Enqueue(() =>
                    {
                        FinishDownload(deadline);
                        if (main == null || main.IsInitializingScene || (!PreviewActive && !IsMainMenu(main)))
                        {
                            session?.Dispose();
                            throw new InvalidOperationException("Game scene changed during download. Snapshot retained; return to menu and reconnect.");
                        }
                        PreviewActive = !live;
                        pauseGuardReady = false;
                        joiningLive = live;
                        pendingLive = session;
                        SnapshotTypeCompatibility.LoadingDedicatedSnapshot = true;
                        status = $"Loading verified island at server step {step}; waiting for scene initialization.";
                        var file = new FileInfo(path);
                        try { main.LoadGame(new SaveFileInfo("server-snapshot", group, file.LastWriteTime, file.Length)); }
                        catch { SnapshotTypeCompatibility.LoadingDedicatedSnapshot = false; PreviewActive = false; joiningLive = false; pendingLive?.Dispose(); pendingLive = null; throw; }
                    });
                }
                catch (Exception error)
                {
                    mainThread.Enqueue(() => { FinishDownload(deadline); Fail(error); });
                }
            });
        }
        catch (Exception error) { Fail(error); }
    }

    private void FinishDownload(CancellationTokenSource completed)
    {
        if (ReferenceEquals(download, completed)) download = null;
        completed.Dispose();
    }

    private static bool IsMainMenu(Main value) => value.CurrentScene.ValueOrNull?.GetType().FullName == "Mafi.Unity.MainMenu.MainMenuScene";

    private void Fail(Exception error)
    {
        status = error is OperationCanceledException ? "Snapshot download canceled or timed out." : "Preview error: " + error.Message;
        Logger.LogError(status);
        Logger.LogDebug(error);
    }

    private void OnGameLog(Mafi.Logging.LogEntry entry)
    {
        StreamWriter? output = gameLog;
        if (output == null) return;
        try { lock (output) output.WriteLine(entry.ToString()); }
        catch (IOException) { }
        catch (ObjectDisposedException) { }
    }

    private void OnDestroy()
    {
        Mafi.Log.LogReceived -= OnGameLog;
        gameLog?.Dispose(); gameLog = null;
        download?.Cancel();
        pendingLive?.Dispose();
        if (liveBridge != null) { liveBridge.Freeze("Client plugin unloaded."); liveBridge.Dispose(); liveBridge = null; }
        if (main != null)
        {
            main.ConfigsCreated -= OnConfigs;
            main.ResolverCreated -= OnResolver;
            main.SceneInitializationFinished -= OnSceneFinished;
            // Persist the pause before removing the runtime pause guard.
            if (PreviewActive && main.CurrentScene.ValueOrNull?.Resolver.ValueOrNull is DependencyResolver resolver
                && resolver.TryResolve(out SimLoopEvents simulation)) simulation.SetSimPause(true);
        }
        PreviewActive = false;
        SnapshotTypeCompatibility.LoadingDedicatedSnapshot = false;
        harmony.UnpatchSelf();
    }
}
