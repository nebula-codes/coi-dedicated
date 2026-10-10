using System;
using System.Reflection;
using Mafi.Core.Simulation;

namespace CoiDedicated.GameAdapter;

/// <summary>All peers execute the same number of native steps, independent of CPU/render budget.</summary>
public static class DeterministicSimulation
{
    public const int MaxSpeed = 4;
    private static readonly PropertyInfo Mode = typeof(SimLoopEvents).GetProperty("AdaptiveSimSpeedMode")!;
    public static void Configure(SimLoopEvents simulation) => Mode.SetValue(simulation, SimAdaptiveSpeedMode.Uncapped);
    public static bool IsSupportedSpeed(int speed) => speed >= 1 && speed <= MaxSpeed;
}
