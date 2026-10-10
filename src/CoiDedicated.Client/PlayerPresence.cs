using System;
using System.Collections.Generic;
using System.Reflection;
using CoiDedicated.Protocol;
using Mafi;
using Mafi.Collections;
using Mafi.Core;
using Mafi.Unity;
using Mafi.Unity.Camera;
using Mafi.Unity.InputControl.Factory;
using Mafi.UnityCore;
using UnityEngine;

namespace CoiDedicated.Client;

/// <summary>Reads rendering state and draws IMGUI annotations. Never creates entities or schedules input.</summary>
internal sealed class PlayerPresence
{
    private static readonly FieldInfo? ActivePreviewsField = typeof(LayoutEntityPreviewManager).GetField("m_activePreviews", BindingFlags.Instance | BindingFlags.NonPublic);
    private static readonly FieldInfo? RemovalPreviewField = typeof(LayoutEntityPreview).GetField("m_isForRemoval", BindingFlags.Instance | BindingFlags.NonPublic);
    private readonly LiveSession session;
    private readonly CameraController? cameraController;
    private readonly LayoutEntityPreviewManager? previewManager;
    private readonly PresenceToolPreviews toolPreviews;
    private readonly Action<string> report;
    private float nextSend;
    private bool failed;
    private bool drawingFailed;
    private bool wasEnabled = true;
    private GUIStyle? label;
    private GUIStyle? roster;

    internal PlayerPresence(DependencyResolver resolver, LiveSession session, Action<string> report)
    {
        this.session = session;
        this.report = report;
        if (resolver.TryResolve(out CameraController camera)) cameraController = camera;
        if (resolver.TryResolve(out LayoutEntityPreviewManager previews)) previewManager = previews;
        toolPreviews = new PresenceToolPreviews(resolver, report);
    }

    internal void Update(bool enabled)
    {
        if (failed || session.Failure != null) return;
        if (Time.unscaledTime < nextSend && enabled == wasEnabled) return;
        nextSend = Time.unscaledTime + 0.21f;
        wasEnabled = enabled;
        try
        {
            if (!enabled || cameraController == null || !cameraController.Camera)
            {
                session.SendPresence(new PlayerPresenceState());
                return;
            }
            Camera camera = cameraController.Camera;
            Vector3 center = cameraController.CameraModel.GetApproxScreenCenter().ToVector3();
            var state = new PlayerPresenceState { Visible = true, Center = Point(center), ViewCorners = ViewQuad(camera, center.y) };
            var previews = new List<PresencePreview>();
            toolPreviews.Collect(previews, state);
            if (previewManager != null && ActivePreviewsField?.GetValue(previewManager) is Lyst<LayoutEntityPreview> active)
            {
                foreach (LayoutEntityPreview preview in active)
                {
                    if (preview.IsDestroyed || !preview.EntityPreviewGameObject || !preview.EntityPreviewGameObject.activeInHierarchy
                        || RemovalPreviewField?.GetValue(preview) is true) continue;
                    if (previews.Count == PresenceCodec.MaxPreviews) { state.PreviewsTruncated = true; break; }
                    // The game's point-transform accounts for pivot, rotation and reflection.
                    // This is the layout's bounding footprint, not a simulated/validated ghost entity.
                    var layout = preview.LayoutEntityProto.Layout;
                    var transform = preview.Transform;
                    int width = layout.LayoutSize.X, height = layout.LayoutSize.Y;
                    if (width <= 0 || height <= 0) continue;
                    PresencePoint Corner(int x, int y) => Point(new Tile3f(layout.TransformF_Point(new RelTile2f(x, y), transform), transform.Position.Z).ToVector3());
                    previews.Add(new PresencePreview
                    {
                        Prototype = SafeLabel("preview: " + preview.LayoutEntityProto.Id),
                        Corners = new[] { Corner(0, 0), Corner(width, 0), Corner(width, height), Corner(0, height) }
                    });
                }
            }
            state.Previews = previews.ToArray();
            session.SendPresence(state);
        }
        catch (Exception error)
        {
            failed = true;
            // Visual incompatibility must not interrupt game synchronization.
            try { session.SendPresence(new PlayerPresenceState()); } catch { }
            report("Player location sharing stopped: " + error.GetBaseException().Message);
        }
    }

    internal void Draw(bool enabled)
    {
        if (!enabled || drawingFailed || session.Failure != null || cameraController == null || !cameraController.Camera) return;
        if (UnityEngine.Event.current == null || UnityEngine.Event.current.type != EventType.Repaint) return;
        Color previous = GUI.color;
        try { DrawCore(cameraController.Camera); }
        catch (Exception error)
        {
            drawingFailed = true;
            report("Player presence drawing stopped: " + error.GetBaseException().Message);
        }
        finally { GUI.color = previous; }
    }

    private void DrawCore(Camera camera)
    {
        label ??= new GUIStyle(GUI.skin.box) { richText = false, fontSize = 12, alignment = TextAnchor.MiddleLeft };
        roster ??= new GUIStyle(GUI.skin.box) { richText = false, alignment = TextAnchor.UpperLeft, padding = new RectOffset(8, 8, 6, 6) };
        int row = 0;
        foreach (PlayerPresenceEntry player in session.Presence)
        {
            if (player.Name == session.Name || !player.State.Visible) continue;
            Color color = Color.HSVToRGB((player.PeerId * 0.618034f) % 1f, 0.55f, 1f);
            GUI.color = color;
            var state = player.State;
            string description = player.Name + (state.Previews.Length == 0 ? " — viewing" : $" — planning ({state.Previews.Length}{(state.PreviewsTruncated ? "+" : "")} outlines)");
            GUI.Box(new Rect(Mathf.Max(10, Screen.width - 350), 82 + row++ * 33, 340, 30), description, roster);
            DrawQuad(camera, state.ViewCorners, 1.5f);
            Vector3 focus = camera.WorldToScreenPoint(Vector(state.Center));
            Vector2 position = new Vector2(focus.x, Screen.height - focus.y);
            if (PresenceGeometry.Marker(position.x, position.y, focus.z, Screen.width, Screen.height, out float pinX, out float pinY, out bool offscreen))
            {
                GUI.Box(new Rect(pinX, pinY, 175, 24), player.Name + (focus.z <= 0 ? " (behind view)" : offscreen ? " (off-screen)" : " camera"), label);
                if (!offscreen)
                {
                    Line(position + new Vector2(-7, 0), position + new Vector2(7, 0), 2);
                    Line(position + new Vector2(0, -7), position + new Vector2(0, 7), 2);
                }
            }
            var labeledPreviews = new HashSet<string>();
            foreach (PresencePreview preview in state.Previews)
            {
                DrawQuad(camera, preview.Corners, 3);
                Vector3 point = camera.WorldToScreenPoint(Vector(preview.Corners[0]));
                if (point.z > 0 && point.x >= 0 && point.x < Screen.width && point.y >= 0 && point.y < Screen.height && labeledPreviews.Add(preview.Prototype))
                    GUI.Box(new Rect(point.x, Screen.height - point.y - 22, 250, 22), player.Name + ": " + preview.Prototype, label);
            }
        }
    }

    private static PresencePoint[] ViewQuad(Camera camera, float height)
    {
        var plane = new Plane(Vector3.up, new Vector3(0, height, 0));
        var result = new PresencePoint[4];
        var corners = new[] { new Vector3(0, 0), new Vector3(1, 0), new Vector3(1, 1), new Vector3(0, 1) };
        for (int i = 0; i < 4; i++)
        {
            Ray ray = camera.ViewportPointToRay(corners[i]);
            if (!plane.Raycast(ray, out float distance) || distance > 20000) return Array.Empty<PresencePoint>();
            result[i] = Point(ray.GetPoint(distance));
        }
        return result;
    }

    private static void DrawQuad(Camera camera, PresencePoint[] corners, float width)
    {
        if (corners.Length != 4) return;
        for (int i = 0; i < 4; i++)
        {
            Vector3 worldA = Vector(corners[i]), worldB = Vector(corners[(i + 1) % 4]);
            float depthA = camera.transform.InverseTransformPoint(worldA).z;
            float depthB = camera.transform.InverseTransformPoint(worldB).z;
            float near = Mathf.Max(.01f, camera.nearClipPlane);
            if (depthA < near && depthB < near) continue;
            if (depthA < near) worldA = Vector3.Lerp(worldA, worldB, (near - depthA) / (depthB - depthA));
            else if (depthB < near) worldB = Vector3.Lerp(worldB, worldA, (near - depthB) / (depthA - depthB));
            Vector3 a = camera.WorldToScreenPoint(worldA), b = camera.WorldToScreenPoint(worldB);
            Line(new Vector2(a.x, Screen.height - a.y), new Vector2(b.x, Screen.height - b.y), width);
        }
    }

    private static void Line(Vector2 a, Vector2 b, float width)
    {
        float ax = a.x, ay = a.y, bx = b.x, by = b.y;
        if (!PresenceGeometry.ClipLine(ref ax, ref ay, ref bx, ref by, Screen.width, Screen.height)) return;
        a = new Vector2(ax, ay); b = new Vector2(bx, by);
        Vector2 delta = b - a;
        if (delta.sqrMagnitude > 400000000 || delta.sqrMagnitude < 0.1f) return;
        Matrix4x4 matrix = GUI.matrix;
        try
        {
            GUIUtility.RotateAroundPivot(Mathf.Atan2(delta.y, delta.x) * Mathf.Rad2Deg, a);
            GUI.DrawTexture(new Rect(a.x, a.y - width / 2, delta.magnitude, width), Texture2D.whiteTexture);
        }
        finally { GUI.matrix = matrix; }
    }

    private static PresencePoint Point(Vector3 point) => new PresencePoint(point.x, point.y, point.z);
    private static Vector3 Vector(PresencePoint point) => new Vector3(point.X, point.Y, point.Z);
    private static string SafeLabel(string value)
    {
        char[] chars = value.Substring(0, Math.Min(value.Length, 96)).ToCharArray();
        for (int i = 0; i < chars.Length; i++) if (chars[i] < 32 || chars[i] > 126 || chars[i] == '<' || chars[i] == '>') chars[i] = '_';
        return chars.Length == 0 ? "building" : new string(chars);
    }
}
