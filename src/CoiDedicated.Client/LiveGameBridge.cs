using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.IO;
using System.Globalization;
using System.Security.Cryptography;
using BepInEx;
using CoiDedicated.GameAdapter;
using HarmonyLib;
using Mafi;
using Mafi.Collections;
using Mafi.Core;
using Mafi.Core.Input;
using Mafi.Core.Simulation;
using Mafi.Core.Prototypes;
using Mafi.Core.Entities.Static;
using Mafi.Core.Entities.Static.Commands;
using Mafi.Core.Factory.Machines;
using Mafi.Core.SaveGame;

namespace CoiDedicated.Client;

internal sealed class LiveGameBridge : IDisposable
{
    private static readonly Type RunnerType = typeof(InputScheduler).Assembly.GetType("Mafi.Core.GameLoop.GameRunner", true)!;
    private static readonly MethodInfo UpdateMethod = AccessTools.Method(RunnerType, "Update");
    private static readonly MethodInfo InputMethod = AccessTools.Method(RunnerType, "performInputUpdate");
    private static readonly MethodInfo RenderMethod = AccessTools.Method(RunnerType, "performRenderUpdate");
    private static readonly FieldInfo TimeField = AccessTools.Field(RunnerType, "m_gameTime");
    private static readonly FieldInfo MainCommandsField = AccessTools.Field(typeof(InputScheduler), "m_mainCommands");
    private static readonly Type FriendType = typeof(IInputCommand).Assembly.GetType("Mafi.Core.Input.IInputCommandFriend", true)!;
    private static readonly MethodInfo MarkFailed = FriendType.GetMethod("MarkFailed")!;
    private static readonly MethodInfo SyncCommand = FriendType.GetMethod("Sync")!;
    internal static LiveGameBridge? Current { get; private set; }
    private readonly DependencyResolver resolver;
    private readonly LiveSession session;
    private readonly object runner;
    private readonly InputScheduler scheduler;
    private readonly SimLoopEvents simulation;
    private readonly Lyst<IInputCommand> mainCommands;
    private readonly Dictionary<ulong, (IInputCommand Command, byte[] Payload)> pending = new Dictionary<ulong, (IInputCommand, byte[])>();
    private readonly HashSet<IInputCommand> admitted = new HashSet<IInputCommand>();
    private readonly Action<string> report;
    private readonly Stopwatch sinceTick = Stopwatch.StartNew();
    private bool allowTick;
    private bool disposed;
    private ulong nextRequest;
    private int appliedTicks;
    private readonly int testTicks;
    private readonly bool testCommands;
    private readonly Dictionary<string, (IInputCommand Command, bool Success)> testPending = new Dictionary<string, (IInputCommand, bool)>();
    private int testCompleted;
    internal bool IsFrozen { get; private set; }
    internal bool RecoveryRequested { get; private set; }
    internal string? FreezeReason { get; private set; }
    internal ulong LastVerifiedCheckpoint { get; private set; }
    internal int CurrentStep => simulation.CurrentStep.Value;
    internal string ClientName => session.Name;

    internal static void Install(Harmony harmony)
    {
        harmony.Patch(UpdateMethod, prefix: new HarmonyMethod(typeof(LiveGameBridge), nameof(UpdatePrefix)));
        harmony.Patch(InputMethod, prefix: new HarmonyMethod(typeof(LiveGameBridge), nameof(InputPrefix)));
        harmony.Patch(AccessTools.Method(typeof(InputScheduler), "CollectCommands"),
            prefix: new HarmonyMethod(typeof(LiveGameBridge), nameof(CollectPrefix)));
    }

    internal LiveGameBridge(DependencyResolver resolver, LiveSession session, Action<string> report, int testTicks = 0, bool testCommands = false)
    {
        if (Current != null) throw new InvalidOperationException("A live island is already attached.");
        this.resolver = resolver;
        this.session = session;
        this.report = report;
        this.testTicks = testTicks;
        this.testCommands = testCommands;
        if (testCommands && testTicks < 80) throw new ArgumentException("Command diagnostics require at least 80 test ticks.");
        runner = resolver.Resolve(RunnerType);
        scheduler = resolver.Resolve<InputScheduler>();
        simulation = resolver.Resolve<SimLoopEvents>();
        mainCommands = (Lyst<IInputCommand>)MainCommandsField.GetValue(scheduler);
        RunnerType.GetProperty("RunSimulationInBackgroundThread")!.SetValue(runner, false);
        RunnerType.GetProperty("ExtraSimUpdateMs")!.SetValue(runner, Fix32.Zero);
        simulation.SetSimSpeed(1);
        DeterministicSimulation.Configure(simulation);
        if (CurrentStep != session.SnapshotStep)
            throw new InvalidOperationException($"Loaded step {CurrentStep} differs from snapshot step {session.SnapshotStep}.");
        Current = this;
        // Initialization-generated commands cannot be carried silently into live play.
        try { session.SendReady(CurrentStep); CaptureCommands(); }
        catch { Current = null; session.Dispose(); throw; }
    }

    internal void Pump()
    {
        if (disposed || IsFrozen) return;
        try
        {
            if (session.Failure != null) { RecoveryRequested = session.RecoveryRequired; throw new InvalidOperationException(session.Failure); }
            while (session.Rejections.TryDequeue(out LiveRejection rejected))
            {
                if (!pending.TryGetValue(rejected.RequestId, out var local))
                    throw new InvalidOperationException("Server rejected an unknown local command ID.");
                pending.Remove(rejected.RequestId);
                Reject(local.Command, rejected.Reason);
            }
            if (sinceTick.Elapsed > TimeSpan.FromSeconds(75)) throw new TimeoutException("Server ticks stopped; island held paused.");
            for (int i = 0; i < 4 && !IsFrozen && session.Ticks.TryDequeue(out LiveTick tick); i++) Apply(tick);
        }
        catch (Exception error) { Freeze(error.GetBaseException().Message, recoverable: true); }
    }

    private void Apply(LiveTick tick)
    {
        if (CurrentStep != tick.BeforeStep)
            throw new InvalidOperationException($"Tick {tick.Sequence} expects step {tick.BeforeStep}; client is at {CurrentStep}.");
        CaptureCommands();
        foreach (LiveCommand item in tick.Commands)
        {
            IInputCommand command;
            if (item.Author == session.Name && item.RequestId != 0)
            {
                if (!pending.TryGetValue(item.RequestId, out var local))
                    throw new InvalidOperationException("Server broadcast an unknown local command ID.");
                if (!local.Payload.SequenceEqual(item.Payload))
                    throw new InvalidOperationException("Server changed an admitted local command.");
                command = local.Command;
                pending.Remove(item.RequestId);
            }
            else command = GameCommandCodec.Decode(item.Payload, resolver);
            admitted.Add(command);
            mainCommands.Add(command);
        }
        // Wire 'paused' is the state before this update, matching GameRunner's sync snapshot.
        simulation.SetSimPause(tick.Paused);
        simulation.SetSimSpeed(tick.Speed);
        allowTick = true;
        try { UpdateMethod.Invoke(runner, new object[] { 100.ToFix32() }); }
        finally { allowTick = false; admitted.Clear(); }
        if (CurrentStep != tick.AfterStep)
        {
            RecoveryRequested = true;
            throw new InvalidOperationException($"Tick {tick.Sequence} ended at {CurrentStep}; server expected {tick.AfterStep}.");
        }
        if (tick.Checkpoint.Length != 0)
        {
            byte[] digest = GameplayCheckpoint.Compute(resolver);
            session.Checkpoint(tick.Sequence, CurrentStep, digest);
            if (!digest.SequenceEqual(tick.Checkpoint))
            {
                RecoveryRequested = true;
                throw new InvalidOperationException($"Gameplay checkpoint mismatch at step {CurrentStep}; reload a fresh server snapshot.");
            }
            LastVerifiedCheckpoint = tick.Sequence;
            report($"Gameplay checkpoint verified at sequence {tick.Sequence}, step {CurrentStep}.");
        }
        session.Acknowledge(tick.Sequence, CurrentStep);
        if (appliedTicks++ % 100 == 0) report($"Applied authoritative tick {tick.Sequence}: step {tick.BeforeStep} -> {CurrentStep}; client {session.Name}.");
        sinceTick.Restart();
        if (testCommands) ExerciseCommandPath();
        if (testTicks > 0 && appliedTicks >= testTicks)
        {
            WriteDiagnostics(tick.Sequence);
            Freeze("Opt-in runtime diagnostic completed; quitting this client.");
            UnityEngine.Application.Quit();
        }
    }

    private void ExerciseCommandPath()
    {
        foreach (var item in testPending.ToArray())
        {
            if (!item.Value.Command.IsProcessedAndSynced) continue;
            if (item.Value.Command.ResultSet != item.Value.Success)
                throw new InvalidOperationException("Unexpected diagnostic command result: " + item.Key);
            report($"COMMAND TEST PASS: {item.Key}; original object processed and synced at step {item.Value.Command.ProcessedAtStep.Value}; resultSet={item.Value.Command.ResultSet}.");
            testPending.Remove(item.Key);
            testCompleted++;
        }
        if (appliedTicks == 5) ScheduleTest("resume", new SetSimPauseStateCmd(false), true);
        if (appliedTicks == 10) ScheduleTest("pause", new SetSimPauseStateCmd(true), true);
        if (appliedTicks == 20) ScheduleTest("resume again", new SetSimPauseStateCmd(false), true);
        if (appliedTicks == 30)
        {
            var nonMachine = resolver.Resolve<ProtosDb>().All<StaticEntityProto>().First(p => !(p is MachineProto));
            ScheduleTest("server rejects non-machine " + nonMachine.Id.Value,
                new CreateStaticEntityCmd(nonMachine.Id, new TileTransform(new Tile3i(0, 0, 0), new Rotation90(0), false), false, false), false);
            ScheduleTest("client rejects unsupported base command", new InputCommand(), false);
        }
        if (appliedTicks == 60) ScheduleTest("final pause", new SetSimPauseStateCmd(true), true);
        if (appliedTicks >= 80 && (testPending.Count != 0 || testCompleted != 6))
            throw new InvalidOperationException("Diagnostic commands did not complete by tick 80.");
    }

    private void ScheduleTest(string name, IInputCommand command, bool success)
    {
        testPending.Add(name, (command, success));
        scheduler.ScheduleInputCmd(command);
        report("COMMAND TEST scheduled through InputScheduler: " + name);
    }

    private void WriteDiagnostics(ulong sequence)
    {
        string directory = Path.Combine(Paths.BepInExRootPath, "CoiDedicated-diagnostics");
        Directory.CreateDirectory(directory);
        string prefix = Path.Combine(directory, "client-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture) + "-step" + CurrentStep);
        string raw = prefix + ".raw";
        var saver = resolver.Resolve<SaveManager>();
        saver.SaveGame(Stream.Null, SaveCompressionType.Gzip, fromTest: true);
        using (var output = new FileStream(raw, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            saver.SaveGameRaw(output);
        string digest;
        using (var source = File.OpenRead(raw))
        using (var sha = SHA256.Create()) digest = BitConverter.ToString(sha.ComputeHash(source)).Replace("-", "").ToLowerInvariant();
        var terrain = resolver.Resolve<Mafi.Core.Terrain.TerrainManager>();
        var duration = (Fix64)RunnerType.GetProperty("GameDurationMs")!.GetValue(runner);
        string summary = $"sequence={sequence}\nstep={CurrentStep}\nticks={appliedTicks}\ncommandTestsCompleted={testCompleted}\nbeforeSavePrepared=true\ndurationRaw={duration.RawValue}\n"
            + $"entities={resolver.Resolve<Mafi.Core.Entities.EntitiesManager>().EntitiesCount}\n"
            + $"population={resolver.Resolve<Mafi.Core.Buildings.Settlements.SettlementsManager>().GetTotalPopulation()}\n"
            + $"terrain={Mafi.Core.Terrain.Generation.TerrainFingerprint.Compute(ref terrain.GetMutableTerrainDataRef())}\n"
            + $"rawLength={new FileInfo(raw).Length}\nrawSha256={digest}\n";
        File.WriteAllText(prefix + ".txt", summary);
        report("Runtime diagnostic written: " + prefix + ".txt; " + summary.Replace('\n', ' '));
    }

    private void CaptureCommands()
    {
        IInputCommand[] queued = mainCommands.ToArray();
        mainCommands.Clear();
        foreach (IInputCommand command in queued)
        {
            if (admitted.Contains(command)) { mainCommands.Add(command); continue; }
            if (IsFrozen)
            {
                Reject(command, "Disconnected island is held paused; return to the main menu to reconnect.");
                continue;
            }
            if (command is GameSpeedChangeCmd speed && !DeterministicSimulation.IsSupportedSpeed(speed.NewSpeedMultiplier))
            {
                Reject(command, $"Multiplayer supports game speeds 1 through {DeterministicSimulation.MaxSpeed}.");
                continue;
            }
            if (!GameCommandCodec.TryEncode(command, out byte[] encoded, out string reason))
            {
                Reject(command, "Unsupported multiplayer action: " + reason);
                continue;
            }
            if (pending.Count >= 128) throw new InvalidOperationException("Too many commands awaiting server approval.");
            ulong requestId = checked(++nextRequest);
            pending.Add(requestId, (command, encoded));
            session.Propose(requestId, encoded);
        }
    }

    private void Reject(IInputCommand command, string reason)
    {
        MarkFailed.Invoke(command, new object[] { simulation.CurrentStep });
        SyncCommand.Invoke(command, null);
        report(reason);
    }

    internal void Freeze(string reason, bool recoverable = false)
    {
        if (IsFrozen) return;
        IsFrozen = true;
        FreezeReason = reason;
        RecoveryRequested = recoverable;
        simulation.SetSimPause(true);
        session.Dispose();
        foreach (var entry in pending.Values)
        {
            try { Reject(entry.Command, reason); } catch { }
        }
        pending.Clear();
        report("MULTIPLAYER STOPPED — ISLAND FROZEN: " + reason);
    }

    private void VisualUpdate(Fix32 delta)
    {
        try
        {
            var time = (GameTime)TimeField.GetValue(runner);
            time.UpdateForSync(delta.ToFloat(), simulation.IsSimPaused, simulation.SimStepsPerUpdate, simulation.SimSpeedMult, simulation.SimSpeedMult,
                CurrentStep, simulation.StepsSinceLoad.Value, 100.ToFix32());
            InputMethod.Invoke(runner, null);
            CaptureCommands();
            RenderMethod.Invoke(runner, null);
        }
        catch (Exception error) { Freeze(error.GetBaseException().Message, recoverable: true); }
    }

    private static bool UpdatePrefix(object __instance, Fix32 deltaTimeMs)
    {
        LiveGameBridge? bridge = Current;
        if (bridge == null || !ReferenceEquals(bridge.runner, __instance) || bridge.allowTick) return true;
        bridge.VisualUpdate(deltaTimeMs);
        return false;
    }

    private static bool InputPrefix(object __instance) => Current == null
        || !ReferenceEquals(Current.runner, __instance) || !Current.allowTick;

    private static void CollectPrefix(InputScheduler __instance)
    {
        LiveGameBridge? bridge = Current;
        if (bridge == null || !ReferenceEquals(bridge.scheduler, __instance)) return;
        bridge.CaptureCommands();
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        session.Dispose();
        if (ReferenceEquals(Current, this)) Current = null;
    }
}
