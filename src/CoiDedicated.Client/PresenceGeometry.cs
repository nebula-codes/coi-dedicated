using System;
using CoiDedicated.Protocol;

namespace CoiDedicated.Client;

/// <summary>Display geometry only, kept independent of Unity for headless validation.</summary>
internal static class PresenceGeometry
{
    internal static int SampleIndex(int sample, int segments, int points) => (int)((long)sample * (points - 1) / segments);

    // A degenerate quad represents one path segment without inventing a footprint width.
    internal static PresencePoint[] Segment(PresencePoint from, PresencePoint to) => new[] { from, to, to, from };

    internal static bool SurfaceTileIsAssigned(ushort unassigned, int x, int y) => (unassigned & (1 << (x + y * 4))) == 0;

    internal static bool Marker(float screenX, float screenY, float depth, float width, float height,
        out float x, out float y, out bool offscreen)
    {
        x = y = 0;
        offscreen = false;
        if (!Finite(screenX) || !Finite(screenY) || !Finite(depth) || width < 1 || height < 1) return false;
        offscreen = depth <= 0 || screenX < 0 || screenX > width || screenY < 0 || screenY > height;
        float minX = Math.Min(10, width / 2), maxX = Math.Max(minX, width - 185);
        float minY = Math.Min(75, height / 2), maxY = Math.Max(minY, height - 30);
        if (!offscreen) { x = Clamp(screenX, minX, maxX); y = Clamp(screenY, minY, maxY); return true; }
        // Unity mirrors projected coordinates behind the camera. Reverse that direction, then
        // intersect a ray from screen center with the inset rectangle instead of losing the marker.
        float dx = screenX - width / 2, dy = screenY - height / 2;
        if (depth <= 0) { dx = -dx; dy = -dy; }
        if (Math.Abs(dx) + Math.Abs(dy) < .001f) dy = 1;
        float cx = (minX + maxX) / 2, cy = (minY + maxY) / 2;
        float halfX = (maxX - minX) / 2, halfY = (maxY - minY) / 2;
        float scaleX = Math.Abs(dx) < .001f ? float.PositiveInfinity : halfX / Math.Abs(dx);
        float scaleY = Math.Abs(dy) < .001f ? float.PositiveInfinity : halfY / Math.Abs(dy);
        float scale = Math.Min(scaleX, scaleY);
        x = Clamp(cx + dx * scale, minX, maxX); y = Clamp(cy + dy * scale, minY, maxY);
        return Finite(x) && Finite(y);
    }

    internal static bool ClipLine(ref float ax, ref float ay, ref float bx, ref float by, float width, float height)
    {
        if (!Finite(ax) || !Finite(ay) || !Finite(bx) || !Finite(by) || width < 1 || height < 1) return false;
        float dx = bx - ax, dy = by - ay, low = 0, high = 1;
        if (!Clip(-dx, ax, ref low, ref high) || !Clip(dx, width - ax, ref low, ref high)
            || !Clip(-dy, ay, ref low, ref high) || !Clip(dy, height - ay, ref low, ref high)) return false;
        bx = ax + high * dx; by = ay + high * dy; ax += low * dx; ay += low * dy;
        return true;
    }

    private static bool Clip(float direction, float distance, ref float low, ref float high)
    {
        if (Math.Abs(direction) < .00001f) return distance >= 0;
        float ratio = distance / direction;
        if (direction < 0) { if (ratio > high) return false; if (ratio > low) low = ratio; }
        else { if (ratio < low) return false; if (ratio < high) high = ratio; }
        return true;
    }

    private static float Clamp(float value, float min, float max) => Math.Max(min, Math.Min(max, value));
    private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
}
