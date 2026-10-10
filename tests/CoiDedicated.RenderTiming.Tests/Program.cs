using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using CoiDedicated.Client;
using Mafi;
using Mafi.Core;

// Exercises the installed game's real GameTime implementation. No island,
// simulation, Unity process, network listener, or installed plugin is touched.
internal static class Program
{
    private static int Main(string[] args)
    {
        string game = Path.GetFullPath(args.Length == 0
            ? Environment.GetEnvironmentVariable("COI_GAME_DIR")
                ?? @"F:\SteamLibrary\steamapps\common\Captain of Industry"
            : args[0]);
        AssemblyLoadContext.Default.Resolving += (_, name) => name.Name is "Mafi" or "Mafi.Core" or "Mafi.Base"
            ? AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.Combine(game,
                "Captain of Industry_Data", "Managed", name.Name + ".dll")) : null;
        return Run();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static int Run()
    {
        (string Name, Action Run)[] tests =
        [
            ("native sync-only clock reproduces frozen world interpolation", SyncOnlyControl),
            ("60 Hz frames interpolate absolute and relative poses", () => SmoothFrames(60)),
            ("144 Hz frames interpolate absolute and relative poses", () => SmoothFrames(144)),
            ("speeds 1 through 4 preserve metadata without multiplying phase", Speeds),
            ("render clock retains the synced generation and native time bases", SyncedMetadata),
            ("pause, resume, and forced freeze preserve native pause semantics", PauseResume),
            ("negative delta and delayed packets cannot extrapolate", ClampFrames),
            ("a fresh sync resets interpolation exactly once", FreshSync),
            ("zero and negative native intervals are rejected", InvalidInterval)
        ];
        int failed = 0;
        foreach (var test in tests)
        {
            try { test.Run(); Console.WriteLine("PASS " + test.Name); }
            catch (Exception error)
            {
                failed++;
                Console.Error.WriteLine("FAIL " + test.Name + ": " + error);
            }
        }
        Console.WriteLine($"{tests.Length - failed}/{tests.Length} native render timing checks passed.");
        return failed == 0 ? 0 : 1;
    }

    private static (GameTime Time, NativeRenderClock Clock) Create(int speed = 1)
    {
        var time = new GameTime();
        Sync(time, speed: speed);
        var clock = new NativeRenderClock(time);
        clock.CaptureSync(10000.ToFix64(), 2000.ToFix64());
        return (time, clock);
    }

    private static void Sync(GameTime time, bool paused = false, int speed = 1,
        int step = 777, int sinceLoad = 222, int interval = 100)
        => time.UpdateForSync(100, paused, speed, speed, speed, step, sinceLoad, interval.ToFix32());

    private static void SyncOnlyControl()
    {
        var time = new GameTime();
        float displayedPosition = 10;
        for (int i = 0; i < 6; i++)
        {
            // This is the previous bridge's per-frame call, deliberately kept
            // as a control so the regression demonstrates the visible failure.
            time.UpdateForSync(1000f / 60, false, 3, 3, 3, 777, 222, 100.ToFix32());
            displayedPosition += (40 - displayedPosition) * time.RelativeT;
            Near(time.AbsoluteT, 0, "sync-only absolute phase");
        }
        Near(displayedPosition, 10, "sync-only moving entity remains at previous pose");
    }

    private static void SmoothFrames(int hz)
    {
        var (time, clock) = Create(speed: 3);
        Fix32 frame = (1000f / hz).ToFix32();
        float previousPhase = 0, relativePosition = 10;
        int movingFrames = 0;
        for (int i = 0; i < hz / 10 + 3; i++)
        {
            clock.Render(frame, (3000 + i * frame.ToFloat()).ToFix64());
            Check(float.IsFinite(time.AbsoluteT) && float.IsFinite(time.RelativeT), "finite interpolation");
            Check(time.AbsoluteT >= previousPhase && time.AbsoluteT <= 1, "monotonic bounded phase");
            Check(time.RelativeT >= 0 && time.RelativeT <= 1, "bounded relative phase");
            if (time.AbsoluteT > previousPhase) movingFrames++;
            Check(time.AbsoluteT - previousPhase <= frame.ToFloat() / 100 + 0.00001f,
                "no unexpected pose jump inside a network interval");
            float absolutePosition = 10 + 30 * time.AbsoluteT;
            relativePosition += (40 - relativePosition) * time.RelativeT;
            Near(relativePosition, absolutePosition, "absolute and incremental native interpolation agree", 0.0001);
            Near(time.DeltaT, time.AbsoluteT - previousPhase, "native DeltaT advances each render");
            Near(time.DeltaSimStepsApprox, time.AbsoluteT - previousPhase, "unscaled fractional step advance");
            Near(time.DeltaTimeMs, frame.ToFloat(), "actual frame duration");
            Check(time.SimStepsCount == 777 && time.SimStepsSinceLoad == 222,
                "render frames cannot advance authoritative simulation counters");
            previousPhase = time.AbsoluteT;
        }
        Check(movingFrames >= hz / 10, "world pose changes on individual render frames");
        Near(time.AbsoluteT, 1, "last known pose reached");
        Near(relativePosition, 40, "last known position reached");
    }

    private static void Speeds()
    {
        for (int speed = 1; speed <= 4; speed++)
        {
            var (time, clock) = Create(speed);
            clock.Render(25.ToFix32(), 3000.ToFix64());
            Near(time.AbsoluteT, 0.25, "speed does not change network interpolation interval");
            Near(time.RelativeT, 0.25, "speed does not multiply relative interpolation");
            Near(time.DeltaSimStepsApprox, 0.25, "speed does not multiply fractional step clock");
            Check(time.SimStepsPerUpdate == speed, "completed native steps retained");
            Near(time.GameSpeedMult, speed, "achieved speed retained for animations");
            Near(time.TargetGameSpeed, speed, "requested speed retained");
            Near(time.TimeSinceStartMs.ToDouble(), 10025, "duration remains native milliseconds");
        }
    }

    private static void SyncedMetadata()
    {
        var time = new GameTime();
        time.UpdateForSync(100, false, 2, 2, 4, 444, 111, 100.ToFix32());
        var clock = new NativeRenderClock(time);
        clock.CaptureSync(8000.ToFix64(), 500.ToFix64());
        // An unrelated write represents newer live metadata. Rendering must
        // keep the generation whose entity endpoints were actually copied.
        time.UpdateForSync(100, false, 4, 4, 4, 448, 115, 100.ToFix32());
        clock.Render(50.ToFix32(), 9000.ToFix64());
        Check(time.SimStepsCount == 444 && time.SimStepsSinceLoad == 111, "synced counters retained");
        Check(time.SimStepsPerUpdate == 2, "synced batch size retained");
        Near(time.GameSpeedMult, 2, "achieved speed separate from requested speed");
        Near(time.TargetGameSpeed, 4, "target speed retained");
        Near(time.TimeSinceStartMs.ToDouble(), 8050, "start duration plus presentation elapsed");
        Near(time.TimeSinceLoadMs.ToDouble(), 550, "load duration plus presentation elapsed");
        Near(time.WallTimeSinceLoadMs.ToDouble(), 9000, "wall clock supplied independently");
        Near(time.TimeSinceLastSimUpdateMs.ToFloat(), 50, "native time since synced endpoints");
    }

    private static void PauseResume()
    {
        var (time, clock) = Create(speed: 3);
        clock.Render(25.ToFix32(), 3000.ToFix64());
        double beforePause = time.TimeSinceStartMs.ToDouble();
        Sync(time, paused: true, speed: 3, step: 780, sinceLoad: 225);
        clock.CaptureSync(10100.ToFix64(), 2100.ToFix64());
        clock.Render(50.ToFix32(), 3050.ToFix64());
        Check(time.IsGamePaused, "paused render state");
        Near(time.AbsoluteT, 1, "native pause holds completed pose");
        Near(time.RelativeT, 0, "native pause does not move pose");
        Near(time.GameSpeedMult, 0, "paused animation speed");
        Near(time.DeltaTimeMs, 0, "paused game delta");
        Near(time.DeltaT, 0, "paused interpolation delta");
        Near(time.DeltaSimStepsApprox, 0, "paused fractional step delta");
        Check(time.SimStepsPerUpdate == 0, "paused simulation batch size");
        Near(time.TimeSinceStartMs.ToDouble(), beforePause, "paused game time held");
        Near(time.WallTimeSinceLoadMs.ToDouble(), 3050, "wall clock continues during pause");
        Check(time.SimStepsCount == 780 && time.SimStepsSinceLoad == 225, "paused synced counters");

        Sync(time, speed: 2, step: 780, sinceLoad: 225);
        clock.CaptureSync(10100.ToFix64(), 2100.ToFix64());
        clock.Render(25.ToFix32(), 3075.ToFix64());
        Check(!time.IsGamePaused, "resume clears native paused state");
        Near(time.AbsoluteT, 0.25, "resume starts a fresh interval");
        Near(time.GameSpeedMult, 2, "resume restores synced speed");
        double beforeFreeze = time.TimeSinceStartMs.ToDouble();
        clock.Render(1000.ToFix32(), 4075.ToFix64(), forcePaused: true);
        Near(time.DeltaT, 0, "first forced freeze clears the preceding frame's interpolation delta");
        Near(time.DeltaSimStepsApprox, 0, "first forced freeze clears fractional step delta");
        Check(time.SimStepsPerUpdate == 0, "first forced freeze clears previous native batch size");
        clock.Render(1000.ToFix32(), 5075.ToFix64(), forcePaused: true);
        Check(time.IsGamePaused, "forced freeze applies without another server tick");
        Near(time.GameSpeedMult, 0, "forced freeze stops animation speed");
        Near(time.RelativeT, 0, "forced freeze stops interpolation");
        Near(time.DeltaT, 0, "repeated forced freeze keeps interpolation delta zero");
        Near(time.DeltaSimStepsApprox, 0, "repeated forced freeze keeps fractional step delta zero");
        Check(time.SimStepsPerUpdate == 0, "repeated forced freeze keeps native batch size zero");
        Near(time.TimeSinceStartMs.ToDouble(), beforeFreeze, "forced freeze holds game time");
        Near(time.WallTimeSinceLoadMs.ToDouble(), 5075, "forced freeze preserves wall time");
    }

    private static void ClampFrames()
    {
        var (time, clock) = Create();
        clock.Render((-50).ToFix32(), 3000.ToFix64());
        Near(time.AbsoluteT, 0, "negative delta cannot reverse time");
        Near(time.RelativeT, 0, "negative delta cannot move an entity");
        Near(time.DeltaTimeMs, 0, "negative render duration sanitized");
        clock.Render(40.ToFix32(), 3040.ToFix64());
        clock.Render(5000.ToFix32(), 8040.ToFix64());
        Near(time.AbsoluteT, 1, "long frame clamps to known pose");
        Near(time.RelativeT, 1, "long frame reaches current target");
        Near(time.TimeSinceStartMs.ToDouble(), 10100, "no time extrapolation past server state");
        Near(time.TimeSinceLastSimUpdateMs.ToFloat(), 100, "elapsed clamps to interval");
        Near(time.DeltaSimStepsApprox, 0.6, "only remaining interval advanced");
        clock.Render(16.ToFix32(), 8056.ToFix64());
        Near(time.AbsoluteT, 1, "late packet holds known pose");
        Near(time.RelativeT, 0, "completed interval avoids division by zero");
        Near(time.DeltaT, 0, "late packet adds no interpolation");
        Near(time.DeltaSimStepsApprox, 0, "late packet adds no simulation phase");
        Near(time.TimeSinceStartMs.ToDouble(), 10100, "late packet cannot extrapolate time");
    }

    private static void FreshSync()
    {
        var (time, clock) = Create();
        clock.Render(100.ToFix32(), 3100.ToFix64());
        Sync(time, speed: 4, step: 781, sinceLoad: 226);
        clock.CaptureSync(10100.ToFix64(), 2100.ToFix64());
        Near(time.AbsoluteT, 0, "new endpoints reset absolute phase");
        Near(time.RelativeT, 0, "new endpoints reset relative phase");
        Near(time.DeltaT, 0, "sync clears old delta");
        clock.Render(25.ToFix32(), 3125.ToFix64());
        Near(time.AbsoluteT, 0.25, "first frame after sync advances");
        Near(time.DeltaT, 0.25, "first frame delta does not retain previous interval");
        clock.Render(25.ToFix32(), 3150.ToFix64());
        Near(time.AbsoluteT, 0.5, "second frame advances rather than resetting");
        Near(time.RelativeT, 1d / 3, "relative interpolation accounts for remaining distance");
        Near(time.TimeSinceStartMs.ToDouble(), 10150, "new duration base retained");
        Check(time.SimStepsCount == 781, "new synced step retained");
    }

    private static void InvalidInterval()
    {
        foreach (int interval in new[] { 0, -100 })
        {
            var time = new GameTime();
            Sync(time, interval: interval);
            var clock = new NativeRenderClock(time);
            try { clock.CaptureSync(Fix64.Zero, Fix64.Zero); }
            catch (InvalidOperationException) { continue; }
            throw new Exception($"Accepted invalid native interval {interval}.");
        }
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }

    private static void Near(double actual, double expected, string message, double tolerance = 0.00002)
    {
        if (!double.IsFinite(actual) || Math.Abs(actual - expected) > tolerance)
            throw new Exception($"{message}: expected {expected}, got {actual}.");
    }
}
