using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Scene-view drawing for BuildingLayout authoring: room outlines and names, doorway openings,
/// and optionally tour node positions. Called by TourGeometryWindow (duringSceneGui) and, through
/// [DrawGizmo], for any GeneratedTourGeometry root in the scene.
/// All drawing uses Handles and is skipped for non-Repaint GUI events.
/// </summary>
public static class TourGeometryGizmos
{
    #region Colours

    private static readonly Color RoomColor = new Color(0.25f, 0.8f, 1f, 1f);
    private static readonly Color RoomFaintColor = new Color(0.25f, 0.8f, 1f, 0.35f);
    private static readonly Color StaleRoomColor = new Color(1f, 0.6f, 0.15f, 1f);
    private static readonly Color StaleRoomFaintColor = new Color(1f, 0.6f, 0.15f, 0.35f);
    private static readonly Color DoorwayColor = new Color(1f, 0.9f, 0.2f, 1f);
    private static readonly Color ErrorColor = new Color(1f, 0.25f, 0.25f, 1f);
    private static readonly Color NodeColor = new Color(0.3f, 1f, 0.4f, 1f);
    private static readonly Color RadiusColor = new Color(0.3f, 1f, 0.4f, 0.25f);

    private const float NodeDiscRadius = 0.35f;

    #endregion

    #region Window Coordination

    /// <summary>
    /// Layout currently drawn by the open TourGeometryWindow, or null. The [DrawGizmo] path skips
    /// generated roots of this layout so the same layout is not drawn twice.
    /// </summary>
    public static BuildingLayout WindowLayout { get; set; }

    #endregion

    #region Styles

    private static GUIStyle _roomLabelStyle;
    private static GUIStyle _smallLabelStyle;

    private static GUIStyle RoomLabelStyle
    {
        get
        {
            if (_roomLabelStyle == null)
            {
                _roomLabelStyle = new GUIStyle(EditorStyles.boldLabel)
                {
                    alignment = TextAnchor.MiddleCenter,
                    fontSize = 12
                };
                _roomLabelStyle.normal.textColor = Color.white;
            }
            return _roomLabelStyle;
        }
    }

    private static GUIStyle SmallLabelStyle
    {
        get
        {
            if (_smallLabelStyle == null)
            {
                _smallLabelStyle = new GUIStyle(EditorStyles.miniLabel)
                {
                    alignment = TextAnchor.MiddleCenter
                };
                _smallLabelStyle.normal.textColor = Color.white;
            }
            return _smallLabelStyle;
        }
    }

    #endregion

    #region Layout Drawing

    /// <summary>
    /// Draws rooms and doorways of <paramref name="layout"/>. Validates the layout to colour invalid
    /// rooms and doorways red. <paramref name="stale"/> draws rooms in the stale colour (generated root out of date).
    /// </summary>
    public static void DrawLayout(BuildingLayout layout, bool stale = false, string titleSuffix = null)
    {
        if (layout == null || (Event.current != null && Event.current.type != EventType.Repaint))
            return;

        List<LayoutRoom> rooms = layout.rooms;
        if (rooms == null)
            return;

        LayoutValidationResult result = BuildingLayoutValidator.Validate(layout);

        // --- Rooms. ---
        for (int i = 0; i < rooms.Count; i++)
        {
            LayoutRoom room = rooms[i];
            if (room == null)
                continue;

            bool valid = i < result.roomBoundsValid.Length && result.roomBoundsValid[i];
            Color strong = !valid ? ErrorColor : stale ? StaleRoomColor : RoomColor;
            Color faint = !valid ? ErrorColor : stale ? StaleRoomFaintColor : RoomFaintColor;

            DrawRoom(layout, room, strong, faint, titleSuffix);
        }

        // --- Doorways. ---
        foreach (ResolvedDoorway rd in result.doorways)
        {
            if (rd.doorway != null)
                DrawDoorway(layout, rooms, rd);
        }
    }

    private static void DrawRoom(BuildingLayout layout, LayoutRoom room, Color strong, Color faint, string titleSuffix)
    {
        float floorY = layout.GetFloorY(room);
        float ceilingY = layout.GetCeilingY(room);

        Vector3[] floor = RectXZ(room.xMin, room.xMax, room.zMin, room.zMax, floorY);
        Vector3[] ceiling = RectXZ(room.xMin, room.xMax, room.zMin, room.zMax, ceilingY);

        Handles.color = strong;
        Handles.DrawPolyLine(floor);

        Handles.color = faint;
        Handles.DrawPolyLine(ceiling);
        for (int c = 0; c < 4; c++)
            Handles.DrawLine(floor[c], ceiling[c]);

        // Label at camera height (y = 0) when that lies inside the room, otherwise mid-height.
        float labelY = (0f > floorY && 0f < ceilingY) ? 0f : (floorY + ceilingY) * 0.5f;
        Vector2 center = room.CenterXZ;
        string text = $"{room.name}\n{BuildingLayoutValidator.Fmt(room.Width)} x {BuildingLayoutValidator.Fmt(room.Depth)} ft";
        if (!string.IsNullOrEmpty(titleSuffix))
            text += $"\n{titleSuffix}";

        Handles.Label(new Vector3(center.x, labelY, center.y), text, RoomLabelStyle);
    }

    private static void DrawDoorway(BuildingLayout layout, List<LayoutRoom> rooms, ResolvedDoorway rd)
    {
        LayoutDoorway d = rd.doorway;
        Color color = rd.hasError ? ErrorColor : DoorwayColor;
        Handles.color = color;

        float labelY;

        if (rd.matches.Count == 0)
        {
            // Not on any wall: draw at the doorway's own line from the layout default floor.
            float floorY = layout.defaultFloorY;
            DrawOpening(d.runsAlong, d.LineCoordinate, d.AlongMin, d.AlongMax, floorY, floorY + d.height);
            labelY = floorY + d.height;
        }
        else
        {
            labelY = float.MinValue;
            foreach (DoorwayWallMatch m in rd.matches)
            {
                float floorY = layout.GetFloorY(rooms[m.roomIndex]);
                DrawOpening(d.runsAlong, m.wall.line, d.AlongMin, d.AlongMax, floorY, floorY + d.height);
                labelY = Mathf.Max(labelY, floorY + d.height);
            }

            // Passage: connect the two cut openings so the passage volume is visible.
            if (rd.needsPassage && rd.matches.Count == 2)
            {
                float floorY = rd.floorY;
                float topY = floorY + d.height;
                float[] alongs = { d.AlongMin, d.AlongMax };
                float[] ys = { floorY, topY };

                foreach (float a in alongs)
                {
                    foreach (float y in ys)
                    {
                        Handles.DrawLine(
                            LayoutMath.WallToWorld(d.runsAlong, rd.passageLineMin, a, y),
                            LayoutMath.WallToWorld(d.runsAlong, rd.passageLineMax, a, y));
                    }
                }
            }
        }

        Vector3 labelPos = LayoutMath.WallToWorld(d.runsAlong, d.LineCoordinate, d.AlongCenter, labelY + 0.5f);
        Handles.Label(labelPos, d.name, SmallLabelStyle);
    }

    /// <summary>Closed rectangle in the vertical wall plane at <paramref name="line"/>.</summary>
    private static void DrawOpening(WallAxis runsAlong, float line, float alongMin, float alongMax, float bottomY, float topY)
    {
        Handles.DrawPolyLine(
            LayoutMath.WallToWorld(runsAlong, line, alongMin, bottomY),
            LayoutMath.WallToWorld(runsAlong, line, alongMax, bottomY),
            LayoutMath.WallToWorld(runsAlong, line, alongMax, topY),
            LayoutMath.WallToWorld(runsAlong, line, alongMin, topY),
            LayoutMath.WallToWorld(runsAlong, line, alongMin, bottomY));
    }

    /// <summary>Closed rectangle (5 points) in the horizontal plane at height y.</summary>
    private static Vector3[] RectXZ(float xMin, float xMax, float zMin, float zMax, float y)
    {
        return new[]
        {
            new Vector3(xMin, y, zMin),
            new Vector3(xMax, y, zMin),
            new Vector3(xMax, y, zMax),
            new Vector3(xMin, y, zMax),
            new Vector3(xMin, y, zMin)
        };
    }

    #endregion

    #region Node Drawing

    /// <summary>
    /// Draws every node of <paramref name="tour"/> as a disc at its position, optionally with its id
    /// and a circle of the tour's navigationRadius.
    /// </summary>
    public static void DrawNodes(TourData tour, bool drawLabels, bool drawRadius)
    {
        if (tour == null || tour.nodes == null || (Event.current != null && Event.current.type != EventType.Repaint))
            return;

        foreach (NodeData node in tour.nodes)
        {
            if (node == null)
                continue;

            Handles.color = NodeColor;
            Handles.DrawSolidDisc(node.position, Vector3.up, NodeDiscRadius);

            if (drawRadius)
            {
                Handles.color = RadiusColor;
                Handles.DrawWireDisc(node.position, Vector3.up, tour.navigationRadius);
            }

            if (drawLabels)
                Handles.Label(node.position + Vector3.up * 0.6f, node.id, SmallLabelStyle);
        }
    }

    #endregion

    #region Generated Geometry Gizmo

    /// <summary>
    /// Outlines the source layout of every generated root, so generated geometry is visible with the window closed.
    /// Stale roots (layout changed since generation) are drawn in orange.
    /// </summary>
    [DrawGizmo(GizmoType.NonSelected | GizmoType.Selected)]
    private static void DrawGeneratedGeometryGizmo(GeneratedTourGeometry marker, GizmoType gizmoType)
    {
        BuildingLayout layout = marker.SourceLayout;
        if (layout == null || layout == WindowLayout)
            return;

        bool stale = !marker.IsUpToDate;
        DrawLayout(layout, stale, stale ? "(generated geometry is stale)" : null);
    }

    #endregion
}
