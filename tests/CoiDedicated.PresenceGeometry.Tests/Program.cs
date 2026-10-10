using System.Reflection;
using CoiDedicated.Client;
using CoiDedicated.Protocol;
using Mafi.Collections;
using Mafi.Core.Factory.Transports;
using Mafi.Core.Terrain.Designation;
using Mafi.Unity.InputControl.Factory;
using Mafi.Unity.Ui.Controllers;
using Mafi.Unity.Ui.Controllers.Designations;
using Mafi.Unity.Ui.Controllers.Trains;

int passed = 0;
void Check(bool value, string message) { if (!value) throw new Exception(message); }
void Test(string name, Action run) { run(); passed++; Console.WriteLine("PASS " + name); }

Test("behind-camera and off-screen locations remain at the visible screen edge", () =>
{
    Check(PresenceGeometry.Marker(-100, 540, -1, 1920, 1080, out float x, out float y, out bool off) && off, "Behind camera marker missing");
    Check(x == 1735 && y >= 75 && y <= 1050, "Behind-camera mirroring points to the wrong edge");
    Check(PresenceGeometry.Marker(-100, 540, 1, 1920, 1080, out x, out y, out off) && x == 10 && off, "Left marker not pinned");
    Check(PresenceGeometry.Marker(960, 540, -1, 1920, 1080, out x, out y, out off) && off && y == 1050, "Directly-behind marker disappeared");
    Check(PresenceGeometry.Marker(500, 300, 1, 1920, 1080, out x, out y, out off) && !off && x == 500 && y == 300, "Visible marker moved");
    Check(!PresenceGeometry.Marker(float.NaN, 300, 1, 1920, 1080, out _, out _, out _), "Nonfinite marker admitted");
});

Test("outlines crossing viewport edges clip instead of drawing huge rectangles", () =>
{
    float ax = -500000, ay = 100, bx = 500000, by = 100;
    Check(PresenceGeometry.ClipLine(ref ax, ref ay, ref bx, ref by, 1920, 1080), "Crossing line was lost");
    Check(Math.Abs(ax) < .1f && Math.Abs(bx - 1920) < .1f && ay == 100 && by == 100, "Line not clipped to viewport");
    ax = -4; ay = 20; bx = -1; by = 20;
    Check(!PresenceGeometry.ClipLine(ref ax, ref ay, ref bx, ref by, 1920, 1080), "Offscreen line admitted");
    ax = 100; ay = -200; bx = 100; by = 2000;
    Check(PresenceGeometry.ClipLine(ref ax, ref ay, ref bx, ref by, 1920, 1080) && Math.Abs(ay) < .001f && Math.Abs(by - 1080) < .001f, "Vertical line not clipped");
});

Test("surface designations do not include unassigned bitmap cells", () =>
{
    ushort selection = unchecked((ushort)~((1 << 0) | (1 << 11) | (1 << 15)));
    int count = 0;
    for (int y = 0; y < 4; y++) for (int x = 0; x < 4; x++) if (PresenceGeometry.SurfaceTileIsAssigned(selection, x, y)) count++;
    Check(count == 3 && PresenceGeometry.SurfaceTileIsAssigned(selection, 3, 2)
        && !PresenceGeometry.SurfaceTileIsAssigned(selection, 2, 3), "Surface bitmap orientation or mask incorrect");
});

Test("large transport paths keep both endpoints within the existing presence frame limit", () =>
{
    const int points = 100000, segments = PresenceCodec.MaxPreviews;
    var previews = new PresencePreview[segments];
    int previous = 0;
    for (int i = 0; i < segments; i++)
    {
        int from = PresenceGeometry.SampleIndex(i, segments, points), to = PresenceGeometry.SampleIndex(i + 1, segments, points);
        Check(from == previous && to > from, "Sample contains a gap or reversed segment");
        previews[i] = new PresencePreview { Prototype = "transport path preview (sampled)", Corners = PresenceGeometry.Segment(new(from, 4, 0), new(to, 4, 0)) };
        previous = to;
    }
    Check(previous == points - 1, "Last endpoint lost");
    var frame = PresenceCodec.EncodeUpdate(new PlayerPresenceState { Visible = true, Previews = previews });
    var decoded = PresenceCodec.DecodeUpdate(frame.Payload);
    Check(decoded.Previews.Length == segments && decoded.Previews[^1].Corners[1].X == points - 1, "Path did not round trip");
    Check(decoded.Previews[0].Corners[0].X == decoded.Previews[0].Corners[3].X, "Segment invented a footprint width");
});

Test("installed CoI has the typed preview fields used by read-only extraction", () =>
{
    FieldInfo Need(Type owner, string name, Type expected)
    {
        var field = owner.GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
        Check(field != null && field.FieldType == expected, owner.Name + "." + name + " changed");
        return field!;
    }
    Need(typeof(PathFindingTransportPreview), "m_previewSpec", typeof(TransportPreviewSpec?));
    Need(typeof(PathFindingTransportPreview), "m_isActive", typeof(bool));
    Check(typeof(TransportTrajectory).GetField("m_geometry", BindingFlags.Instance | BindingFlags.NonPublic) != null, "Transport cache guard changed");
    Need(typeof(PathFindingTrainTrackPreview), "m_isActive", typeof(bool));
    Need(typeof(TerrainDesignationController), "m_previewAreas", typeof(Dict<Mafi.Tile2i, DesignationData>));
    Need(typeof(SurfaceDesignationController), "m_previewAreas", typeof(Dict<Mafi.Tile2i, SurfaceDesignationData>));
});
Console.WriteLine($"{passed} presence geometry/metadata checks passed. Unity drawing still requires an in-game check.");
