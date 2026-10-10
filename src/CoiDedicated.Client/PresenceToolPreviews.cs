using System;
using System.Collections.Generic;
using System.Reflection;
using CoiDedicated.Protocol;
using Mafi;
using Mafi.Collections;
using Mafi.Core;
using Mafi.Core.Factory.Transports;
using Mafi.Core.Terrain;
using Mafi.Core.Terrain.Designation;
using Mafi.Unity;
using Mafi.Unity.InputControl.Factory;
using Mafi.Unity.Ui.Controllers;
using Mafi.Unity.Ui.Controllers.Designations;
using Mafi.Unity.Ui.Controllers.Trains;
using Mafi.UnityCore;
using UnityEngine;

namespace CoiDedicated.Client;

/// <summary>Copies current UI previews only. Never creates plans, calls pathfinding, or schedules commands.</summary>
internal sealed class PresenceToolPreviews
{
    private static FieldInfo? Field(Type type, string name) => type.GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
    private static readonly FieldInfo? TransportState = Field(typeof(PathFindingTransportPreview), "m_previewSpec");
    private static readonly FieldInfo? TransportActive = Field(typeof(PathFindingTransportPreview), "m_isActive");
    private static readonly FieldInfo? TransportGeometry = Field(typeof(TransportTrajectory), "m_geometry");
    private static readonly FieldInfo? RailActive = Field(typeof(PathFindingTrainTrackPreview), "m_isActive");
    private static readonly FieldInfo? TerrainAreas = Field(typeof(TerrainDesignationController), "m_previewAreas");
    private static readonly FieldInfo? SurfaceAreas = Field(typeof(SurfaceDesignationController), "m_previewAreas");
    private readonly PathFindingTransportPreview? transport;
    private readonly PathFindingTrainTrackPreview? rail;
    private readonly SurfaceDesignationController? surface;
    private readonly TerrainManager? terrain;
    private readonly List<(TerrainDesignationController Controller, string Label)> designations = new();
    private readonly Action<string> report;
    private readonly HashSet<string> unavailable = new();

    internal PresenceToolPreviews(DependencyResolver resolver, Action<string> report)
    {
        this.report = report;
        transport = Resolve<PathFindingTransportPreview>(resolver);
        rail = Resolve<PathFindingTrainTrackPreview>(resolver);
        surface = Resolve<SurfaceDesignationController>(resolver);
        terrain = Resolve<TerrainManager>(resolver);
        if (Resolve<MiningDesignationController>(resolver) is { } mining) designations.Add((mining, "mining preview"));
        if (Resolve<DumpingDesignationController>(resolver) is { } dumping) designations.Add((dumping, "dumping preview"));
        if (Resolve<LevelTerrainDesignationController>(resolver) is { } level) designations.Add((level, "level terrain preview"));
        if (Resolve<ForestryDesignationController>(resolver) is { } forestry) designations.Add((forestry, "forestry preview"));
    }

    private T? Resolve<T>(DependencyResolver resolver) where T : class
    {
        try { return resolver.TryResolve(out T value) ? value : null; }
        catch (Exception error)
        {
            report(typeof(T).Name + " preview sharing unavailable: " + error.GetBaseException().Message);
            return null;
        }
    }

    internal void Collect(List<PresencePreview> output, PlayerPresenceState state)
    {
        Try("transport", () => CollectTransport(output, state));
        Try("rail", () => CollectRail(output, state));
        Try("terrain", () => CollectTerrain(output, state));
        Try("surface", () => CollectSurface(output, state));
    }

    private void Try(string family, Action read)
    {
        if (unavailable.Contains(family)) return;
        try { read(); }
        catch (Exception error)
        {
            unavailable.Add(family);
            report($"{family} preview sharing unavailable: {error.GetBaseException().Message}");
        }
    }

    private void CollectTransport(List<PresencePreview> output, PlayerPresenceState state)
    {
        if (transport == null || TransportActive?.GetValue(transport) is not true) return;
        if (TransportState?.GetValue(transport) is not TransportPreviewSpec spec) return;
        if (spec.OkTraj.HasValue) AddTrajectory(spec.OkTraj.Value, "transport path preview", output, state);
        if (spec.ErrTraj.HasValue && (!spec.OkTraj.HasValue || !ReferenceEquals(spec.OkTraj.Value, spec.ErrTraj.Value)))
            AddTrajectory(spec.ErrTraj.Value, "invalid transport path preview", output, state);
    }

    private static void AddTrajectory(TransportTrajectory trajectory, string label, List<PresencePreview> output, PlayerPresenceState state)
    {
        // Waypoints lazily creates geometry when absent. Read only after the game's renderer
        // has already materialized it, so presence cannot change preview or simulation state.
        if (TransportGeometry?.GetValue(trajectory) == null) return;
        var points = trajectory.Waypoints;
        int segments = Math.Min(PresenceCodec.MaxPreviews - output.Count, points.Length - 1);
        if (segments <= 0) { if (points.Length > 1) state.PreviewsTruncated = true; return; }
        if (segments < points.Length - 1) label += " (sampled)";
        for (int i = 0; i < segments; i++)
        {
            var a = Point(points[PresenceGeometry.SampleIndex(i, segments, points.Length)].Position.ToVector3());
            var b = Point(points[PresenceGeometry.SampleIndex(i + 1, segments, points.Length)].Position.ToVector3());
            Add(output, state, label, PresenceGeometry.Segment(a, b));
        }
    }

    private void CollectRail(List<PresencePreview> output, PlayerPresenceState state)
    {
        if (rail == null || RailActive?.GetValue(rail) is not true || !rail.TryGetCurrentPlan(out var plan, out _)) return;
        // These are the actual transformed layout footprints of the current plan, not invented
        // straight lines across rail curves. All remain labeled as preview bounds.
        foreach (var step in plan.Steps)
        {
            if (Full(output, state)) return;
            var layout = step.Proto.Layout;
            int width = layout.LayoutSize.X, height = layout.LayoutSize.Y;
            var transform = step.Transform;
            PresencePoint Corner(int x, int y) => Point(new Tile3f(layout.TransformF_Point(new RelTile2f(x, y), transform), transform.Position.Z).ToVector3());
            Add(output, state, "rail preview bounds", new[] { Corner(0, 0), Corner(width, 0), Corner(width, height), Corner(0, height) });
        }
    }

    private void CollectTerrain(List<PresencePreview> output, PlayerPresenceState state)
    {
        foreach (var item in designations)
        {
            if (!item.Controller.IsActive || TerrainAreas?.GetValue(item.Controller) is not Dict<Tile2i, DesignationData> areas) continue;
            foreach (DesignationData data in areas.Values)
            {
                if (Full(output, state)) return;
                Add(output, state, item.Label, new[]
                {
                    Point(data.OriginTile.ExtendHeight(data.OriginTargetHeight).CornerTile3f.ToVector3()),
                    Point(data.PlusXTileCoord.ExtendHeight(data.PlusXTargetHeight).CornerTile3f.ToVector3()),
                    Point(data.PlusXyTileCoord.ExtendHeight(data.PlusXyTargetHeight).CornerTile3f.ToVector3()),
                    Point(data.PlusYTileCoord.ExtendHeight(data.PlusYTargetHeight).CornerTile3f.ToVector3())
                });
            }
        }
    }

    private void CollectSurface(List<PresencePreview> output, PlayerPresenceState state)
    {
        if (surface == null || terrain == null || !surface.IsActive
            || SurfaceAreas?.GetValue(surface) is not Dict<Tile2i, SurfaceDesignationData> areas) return;
        foreach (SurfaceDesignationData data in areas.Values)
        {
            for (int y = 0; y < 4; y++) for (int x = 0; x < 4; x++)
            {
                if (!PresenceGeometry.SurfaceTileIsAssigned(data.UnassignedTilesBitmap, x, y)) continue;
                if (Full(output, state)) return;
                Tile2i origin = data.OriginTile.AsFull + new RelTile2i(x, y);
                if (!terrain.IsValidCoord(origin) || !terrain.IsValidCoord(origin.AddXy(1))) continue;
                Add(output, state, "surface paving preview", new[]
                {
                    Point(terrain.ExtendHeight(origin).ToVector3()), Point(terrain.ExtendHeight(origin.AddX(1)).ToVector3()),
                    Point(terrain.ExtendHeight(origin.AddXy(1)).ToVector3()), Point(terrain.ExtendHeight(origin.AddY(1)).ToVector3())
                });
            }
        }
    }

    private static bool Full(List<PresencePreview> output, PlayerPresenceState state)
    {
        if (output.Count < PresenceCodec.MaxPreviews) return false;
        state.PreviewsTruncated = true;
        return true;
    }
    private static void Add(List<PresencePreview> output, PlayerPresenceState state, string label, PresencePoint[] corners)
    {
        if (!Full(output, state)) output.Add(new PresencePreview { Prototype = label, Corners = corners });
    }
    private static PresencePoint Point(Vector3 value) => new(value.x, value.y, value.z);
}
