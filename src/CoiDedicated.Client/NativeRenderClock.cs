using System;
using System.Reflection;
using Mafi;
using Mafi.Core;

namespace CoiDedicated.Client;

/// <summary>Advances only the native renderer's clock between authoritative updates.</summary>
internal sealed class NativeRenderClock
{
    private delegate void UpdateTime(GameTime time, float deltaMs, Fix64 fromStart, Fix64 fromLoad,
        Fix64 wallTime, Fix32 fromSync, float absolute, float relative, int stepsPerUpdate,
        float speed, float targetSpeed, int step, int stepsSinceLoad, float deltaSteps);
    private delegate void PauseTime(GameTime time, Fix64 wallTime, int step, int stepsSinceLoad);
    private static readonly UpdateTime Update = (UpdateTime)Delegate.CreateDelegate(typeof(UpdateTime),
        typeof(GameTime).GetMethod("Update", BindingFlags.Instance | BindingFlags.NonPublic,
            null, new[] { typeof(float), typeof(Fix64), typeof(Fix64), typeof(Fix64), typeof(Fix32),
                typeof(float), typeof(float), typeof(int), typeof(float), typeof(float), typeof(int),
                typeof(int), typeof(float) }, null) ?? throw new MissingMethodException("GameTime.Update"));
    private static readonly PauseTime Pause = (PauseTime)Delegate.CreateDelegate(typeof(PauseTime),
        typeof(GameTime).GetMethod("UpdatePaused", BindingFlags.Instance | BindingFlags.NonPublic,
            null, new[] { typeof(Fix64), typeof(int), typeof(int) }, null)
            ?? throw new MissingMethodException("GameTime.UpdatePaused"));

    private readonly GameTime time;
    private Fix64 duration, durationSinceLoad;
    private Fix32 elapsed;
    private Fix32 interval;
    private int step, stepsSinceLoad, stepsPerUpdate;
    private float speed, targetSpeed;
    private bool paused;

    internal NativeRenderClock(GameTime time) { this.time = time; }

    internal void CaptureSync(Fix64 gameDuration, Fix64 gameDurationSinceLoad)
    {
        // These are the states copied for rendering by native Sync, which may lag
        // CurrentStep. Reading live simulation state here mixes two generations.
        duration = gameDuration;
        durationSinceLoad = gameDurationSinceLoad;
        step = time.SimStepsCount;
        stepsSinceLoad = time.SimStepsSinceLoad;
        stepsPerUpdate = time.SimStepsPerUpdate;
        speed = time.GameSpeedMult;
        targetSpeed = time.TargetGameSpeed;
        paused = time.IsGamePaused;
        interval = time.CurrSimUpdateDurationMs;
        if (interval <= Fix32.Zero) throw new InvalidOperationException("Invalid native render interval.");
        elapsed = Fix32.Zero;
        // Reset DeltaT once per sync, never as the clock for an unpaused frame.
        time.UpdateForSync(0, paused, stepsPerUpdate, speed, targetSpeed, step, stepsSinceLoad, interval);
    }

    internal void Render(Fix32 frameDelta, Fix64 wallTime, bool forcePaused = false)
    {
        if (paused || forcePaused)
        {
            // UpdatePaused alone leaves DeltaT/SimStepsPerUpdate unchanged.
            // Some native animations consume them without checking IsGamePaused.
            time.UpdateForSync(0, true, 0, 0, 0, step, stepsSinceLoad, interval);
            Pause(time, wallTime, step, stepsSinceLoad);
            return;
        }
        if (frameDelta < Fix32.Zero) frameDelta = Fix32.Zero;
        Fix32 previous = elapsed;
        Fix32 remaining = interval - previous;
        // Hold at the newest known pose when packets are late; never extrapolate.
        elapsed += frameDelta < remaining ? frameDelta : remaining;
        float absolute = elapsed.ToFloat() / interval.ToFloat();
        float relative = remaining > Fix32.Zero ? (elapsed - previous).ToFloat() / remaining.ToFloat() : 0f;
        float deltaSteps = (elapsed - previous).ToFloat() / interval.ToFloat();
        Update(time, frameDelta.ToFloat(), duration + elapsed, durationSinceLoad + elapsed,
            wallTime, elapsed, absolute, relative, stepsPerUpdate, speed, targetSpeed,
            step, stepsSinceLoad, deltaSteps);
    }
}
