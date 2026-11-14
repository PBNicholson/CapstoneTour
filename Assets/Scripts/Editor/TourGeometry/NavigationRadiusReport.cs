using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Coverage figures for one floor area (a room, or a doorway passage).
/// </summary>
public class AreaCoverage
{
    public string name;
    public bool isPassage;
    public float floorY;
    public int nodesInside;

    /// <summary>Ids of the nodes inside this area (edges included), in tour order.</summary>
    public readonly List<string> nodeIdsInside = new List<string>();
    public int samples;

    /// <summary>Largest nearest-node distance over the area's samples (ft).</summary>
    public float maxNearest;
    public Vector2 worstNearestPoint;
    public string worstNearestNodeId;
    public Vector2 worstNearestNodePos;

    /// <summary>Largest second-nearest-node distance over the area's samples (ft). NaN if the tour has fewer than 2 nodes.</summary>
    public float maxSecond = float.NaN;
    public Vector2 worstSecondPoint;

    /// <summary>Samples whose nearest node is not strictly within the current navigationRadius.</summary>
    public int uncoveredNearest;

    /// <summary>Samples whose second-nearest node is not strictly within the current navigationRadius.</summary>
    public int uncoveredSecond;
}

/// <summary>
/// Result of a navigation radius report.
/// </summary>
public class RadiusReport
{
    public string layoutName;
    public string tourName;
    public float gridSpacing;
    public float currentRadius;
    public int nodeCount;
    public int floorCount;

    public readonly List<AreaCoverage> areas = new List<AreaCoverage>();
    public readonly List<string> roomsWithoutNodes = new List<string>();
    public readonly List<string> nodesOutsideRooms = new List<string>();

    public AreaCoverage WorstNearestArea;
    public AreaCoverage WorstSecondArea;

    public float R1 => WorstNearestArea != null ? WorstNearestArea.maxNearest : float.NaN;
    public float R2 => WorstSecondArea != null ? WorstSecondArea.maxSecond : float.NaN;

    public int TotalSamples
    {
        get { int n = 0; foreach (AreaCoverage a in areas) n += a.samples; return n; }
    }

    public int TotalUncoveredNearest
    {
        get { int n = 0; foreach (AreaCoverage a in areas) n += a.uncoveredNearest; return n; }
    }

    public int TotalUncoveredSecond
    {
        get { int n = 0; foreach (AreaCoverage a in areas) n += a.uncoveredSecond; return n; }
    }
}

/// <summary>
/// Samples every room floor (and doorway passage floor) of a layout on a grid, edges and corners included, and
/// measures the horizontal (XZ) distance from each sample to the tour's nodes, the same way
/// TourData.GetNearestNode does (all floors, strict "less than radius").
///
/// R1 = max nearest-node distance: navigationRadius must be greater than R1 for every floor point to resolve.
/// R2 = max second-nearest distance: SpatialNavigator excludes the current node, so with navigationRadius greater
///      than R2 every floor point has a target even when its nearest node is the one the user is standing on.
///
/// Read-only: never modifies TourData.
/// </summary>
public static class NavigationRadiusReport
{
    /// <summary>Refuse to run above this many sample x node distance evaluations.</summary>
    private const long MaxEvaluations = 200_000_000L;

    #region Run

    /// <summary>
    /// Runs the report. Returns null and fills <paramref name="error"/> if the inputs are unusable.
    /// </summary>
    public static RadiusReport Run(BuildingLayout layout, TourData tour, float gridSpacing, out string error)
    {
        error = null;

        if (layout == null || tour == null)
        {
            error = "Assign both a Building Layout and a Tour.";
            return null;
        }

        if (!(gridSpacing >= 0.1f))
        {
            error = "Grid spacing must be at least 0.1 ft.";
            return null;
        }

        LayoutValidationResult validation = BuildingLayoutValidator.Validate(layout);
        if (validation.HasErrors)
        {
            error = $"The layout has {validation.errors.Count} validation error(s). Run Validate Layout and fix them first.";
            return null;
        }

        // --- Nodes (all floors, matching SpatialNavigator's floorFilter: null). ---
        List<Vector2> nodePositions = new List<Vector2>();
        List<string> nodeIds = new List<string>();
        foreach (NodeData node in tour.nodes)
        {
            if (node == null)
                continue;
            nodePositions.Add(new Vector2(node.position.x, node.position.z));
            nodeIds.Add(node.id);
        }

        if (nodePositions.Count == 0)
        {
            error = $"Tour '{tour.name}' has no nodes.";
            return null;
        }

        RadiusReport report = new RadiusReport
        {
            layoutName = layout.layoutName,
            tourName = tour.name,
            gridSpacing = gridSpacing,
            currentRadius = tour.navigationRadius,
            nodeCount = nodePositions.Count,
            floorCount = tour.floors != null ? tour.floors.Count : 0
        };

        // --- Areas: rooms, then passages. ---
        List<AreaRect> rects = new List<AreaRect>();

        foreach (LayoutRoom room in layout.rooms)
        {
            rects.Add(new AreaRect
            {
                name = room.name,
                isPassage = false,
                xMin = room.xMin,
                xMax = room.xMax,
                zMin = room.zMin,
                zMax = room.zMax,
                floorY = layout.GetFloorY(room)
            });
        }

        foreach (ResolvedDoorway rd in validation.doorways)
        {
            if (!rd.needsPassage)
                continue;

            LayoutDoorway d = rd.doorway;
            AreaRect rect = new AreaRect { name = $"Passage {d.name}", isPassage = true, floorY = rd.floorY };

            if (d.runsAlong == WallAxis.Z)
            {
                rect.xMin = rd.passageLineMin; rect.xMax = rd.passageLineMax;
                rect.zMin = d.AlongMin; rect.zMax = d.AlongMax;
            }
            else
            {
                rect.xMin = d.AlongMin; rect.xMax = d.AlongMax;
                rect.zMin = rd.passageLineMin; rect.zMax = rd.passageLineMax;
            }

            rects.Add(rect);
        }

        // --- Guard against runaway sample counts. ---
        long estimatedSamples = 0;
        foreach (AreaRect r in rects)
            estimatedSamples += (long)(GridCount(r.xMin, r.xMax, gridSpacing)) * GridCount(r.zMin, r.zMax, gridSpacing);

        if (estimatedSamples * nodePositions.Count > MaxEvaluations)
        {
            error = $"About {estimatedSamples} samples x {nodePositions.Count} nodes is too many. Increase the grid spacing.";
            return null;
        }

        // --- Sample. ---
        float radius = tour.navigationRadius;

        foreach (AreaRect r in rects)
        {
            AreaCoverage area = SampleArea(r, gridSpacing, nodePositions, nodeIds, radius);
            report.areas.Add(area);

            if (report.WorstNearestArea == null || area.maxNearest > report.WorstNearestArea.maxNearest)
                report.WorstNearestArea = area;

            if (!float.IsNaN(area.maxSecond) &&
                (report.WorstSecondArea == null || area.maxSecond > report.WorstSecondArea.maxSecond))
                report.WorstSecondArea = area;

            if (!r.isPassage && area.nodesInside == 0)
                report.roomsWithoutNodes.Add(r.name);
        }

        // --- Nodes outside every room and passage. ---
        for (int n = 0; n < nodePositions.Count; n++)
        {
            Vector2 p = nodePositions[n];
            bool inside = false;
            foreach (AreaRect r in rects)
            {
                if (r.Contains(p))
                {
                    inside = true;
                    break;
                }
            }
            if (!inside)
                report.nodesOutsideRooms.Add(nodeIds[n]);
        }

        report.areas.Sort((a, b) => b.maxNearest.CompareTo(a.maxNearest));
        return report;
    }

    #endregion

    #region Sampling

    private struct AreaRect
    {
        public string name;
        public bool isPassage;
        public float xMin, xMax, zMin, zMax;
        public float floorY;

        public bool Contains(Vector2 p)
        {
            const float e = LayoutConstants.Epsilon;
            return p.x >= xMin - e && p.x <= xMax + e && p.y >= zMin - e && p.y <= zMax + e;
        }
    }

    /// <summary>Number of grid coordinates from min to max inclusive: min, min+s, ..., and max itself.</summary>
    private static int GridCount(float min, float max, float spacing)
    {
        int steps = Mathf.FloorToInt((max - min) / spacing + 1e-4f);
        float last = min + steps * spacing;
        return steps + 1 + (max - last > LayoutConstants.Epsilon ? 1 : 0);
    }

    private static float GridCoord(float min, float max, float spacing, int index, int count)
    {
        return index == count - 1 ? max : min + index * spacing;
    }

    private static AreaCoverage SampleArea(AreaRect r, float spacing, List<Vector2> nodes, List<string> nodeIds, float radius)
    {
        AreaCoverage area = new AreaCoverage
        {
            name = r.name,
            isPassage = r.isPassage,
            floorY = r.floorY,
            maxNearest = -1f
        };

        for (int n = 0; n < nodes.Count; n++)
        {
            if (r.Contains(nodes[n]))
            {
                area.nodesInside++;
                area.nodeIdsInside.Add(nodeIds[n]);
            }
        }

        int nx = GridCount(r.xMin, r.xMax, spacing);
        int nz = GridCount(r.zMin, r.zMax, spacing);
        float radiusSqr = radius * radius;
        bool hasSecond = nodes.Count >= 2;
        float maxSecondSqr = -1f;

        for (int ix = 0; ix < nx; ix++)
        {
            float x = GridCoord(r.xMin, r.xMax, spacing, ix, nx);

            for (int iz = 0; iz < nz; iz++)
            {
                float z = GridCoord(r.zMin, r.zMax, spacing, iz, nz);

                float best = float.MaxValue;
                float second = float.MaxValue;
                int bestIndex = -1;

                for (int n = 0; n < nodes.Count; n++)
                {
                    float dx = nodes[n].x - x;
                    float dz = nodes[n].y - z;
                    float dSqr = dx * dx + dz * dz;

                    if (dSqr < best)
                    {
                        second = best;
                        best = dSqr;
                        bestIndex = n;
                    }
                    else if (dSqr < second)
                    {
                        second = dSqr;
                    }
                }

                area.samples++;

                // Same comparison as TourData.GetNearestNode: candidate only if distance^2 < radius^2.
                if (!(best < radiusSqr))
                    area.uncoveredNearest++;
                if (hasSecond && !(second < radiusSqr))
                    area.uncoveredSecond++;

                float bestDistance = Mathf.Sqrt(best);
                if (bestDistance > area.maxNearest)
                {
                    area.maxNearest = bestDistance;
                    area.worstNearestPoint = new Vector2(x, z);
                    area.worstNearestNodeId = nodeIds[bestIndex];
                    area.worstNearestNodePos = nodes[bestIndex];
                }

                if (hasSecond && second > maxSecondSqr)
                {
                    maxSecondSqr = second;
                    area.worstSecondPoint = new Vector2(x, z);
                }
            }
        }

        if (hasSecond)
            area.maxSecond = Mathf.Sqrt(maxSecondSqr);

        return area;
    }

    #endregion

    #region Formatting

    /// <summary>Report as log lines.</summary>
    public static List<string> Format(RadiusReport report)
    {
        List<string> lines = new List<string>();

        lines.Add($"Radius report: layout '{report.layoutName}', tour '{report.tourName}' ({report.nodeCount} node(s), " +
                  $"all floors), grid {F(report.gridSpacing)} ft, {report.TotalSamples} floor sample(s) (edges included).");

        if (report.floorCount > 1)
        {
            lines.Add($"  Note: the tour has {report.floorCount} floors. Nodes on every floor are included, matching " +
                      "SpatialNavigator (floorFilter: null).");
        }

        lines.Add($"Current navigationRadius: {F(report.currentRadius)} ft. A node is a candidate only if its distance is strictly less.");

        AreaCoverage w1 = report.WorstNearestArea;
        lines.Add($"R1 = {F(report.R1)} ft at {P(w1.worstNearestPoint)} in '{w1.name}' (nearest node '{w1.worstNearestNodeId}'). " +
                  "navigationRadius must be greater than R1 for every floor point to resolve to a node.");

        if (report.WorstSecondArea != null)
        {
            AreaCoverage w2 = report.WorstSecondArea;
            lines.Add($"R2 = {F(report.R2)} ft at {P(w2.worstSecondPoint)} in '{w2.name}'. With navigationRadius greater than R2, " +
                      "every floor point has a target even when its nearest node is the one you are standing on.");
        }
        else
        {
            lines.Add("R2: not available (fewer than 2 nodes).");
        }

        int total = report.TotalSamples;
        lines.Add($"At the current radius: {report.TotalUncoveredNearest} of {total} sample(s) ({Pct(report.TotalUncoveredNearest, total)}) " +
                  $"have no node in range; {report.TotalUncoveredSecond} ({Pct(report.TotalUncoveredSecond, total)}) have no node " +
                  "in range other than their nearest.");

        lines.Add("Per area (sorted by max nearest-node distance):");
        foreach (AreaCoverage a in report.areas)
        {
            string second = float.IsNaN(a.maxSecond) ? "n/a" : $"{F(a.maxSecond)} at {P(a.worstSecondPoint)}";
            lines.Add($"  {a.name}: {a.nodesInside} node(s) inside, {a.samples} sample(s); " +
                      $"max nearest {F(a.maxNearest)} at {P(a.worstNearestPoint)} ('{a.worstNearestNodeId}'); " +
                      $"max 2nd-nearest {second}; out of range at current radius: {a.uncoveredNearest} nearest, {a.uncoveredSecond} 2nd.");
            lines.Add(a.nodeIdsInside.Count > 0
                ? $"    nodes inside: {string.Join(", ", a.nodeIdsInside)}"
                : "    nodes inside: none");
        }

        lines.Add(report.roomsWithoutNodes.Count == 0
            ? "Rooms with no nodes inside: none."
            : $"Rooms with no nodes inside: {string.Join(", ", report.roomsWithoutNodes)}.");

        if (report.nodesOutsideRooms.Count == 0)
        {
            lines.Add("Nodes outside every room: none.");
        }
        else
        {
            int shown = Mathf.Min(10, report.nodesOutsideRooms.Count);
            string list = string.Join(", ", report.nodesOutsideRooms.GetRange(0, shown));
            string more = report.nodesOutsideRooms.Count > shown ? ", ..." : string.Empty;
            lines.Add($"Nodes outside every room: {report.nodesOutsideRooms.Count} ({list}{more}). " +
                      "Expected while the layout covers only part of the building.");
        }

        return lines;
    }

    private static string F(float v) => BuildingLayoutValidator.Fmt(v);
    private static string P(Vector2 p) => $"(x {F(p.x)}, z {F(p.y)})";
    private static string Pct(int n, int total) => total > 0 ? $"{(100f * n / total):0.#}%" : "0%";

    #endregion

    #region Scene Markers

    private static readonly Color WorstNearestColor = new Color(1f, 0.3f, 0.3f, 1f);
    private static readonly Color WorstSecondColor = new Color(1f, 0.6f, 0.15f, 1f);

    /// <summary>
    /// Draws each area's worst nearest-node point (red, with a line to that node) and the overall R2 point (orange)
    /// on the floor plane.
    /// </summary>
    public static void DrawMarkers(RadiusReport report)
    {
        if (report == null || (Event.current != null && Event.current.type != EventType.Repaint))
            return;

        foreach (AreaCoverage a in report.areas)
        {
            Vector3 p = new Vector3(a.worstNearestPoint.x, a.floorY, a.worstNearestPoint.y);
            Vector3 node = new Vector3(a.worstNearestNodePos.x, a.floorY, a.worstNearestNodePos.y);

            Handles.color = WorstNearestColor;
            Handles.DrawWireDisc(p, Vector3.up, 0.5f);
            Handles.DrawDottedLine(p, node, 3f);
            Handles.Label(p + Vector3.up * 0.5f, $"{a.name}: max {F(a.maxNearest)} ft", EditorStyles.whiteMiniLabel);
        }

        AreaCoverage w2 = report.WorstSecondArea;
        if (w2 != null)
        {
            Vector3 p = new Vector3(w2.worstSecondPoint.x, w2.floorY, w2.worstSecondPoint.y);
            Handles.color = WorstSecondColor;
            Handles.DrawWireDisc(p, Vector3.up, 0.7f);
            Handles.Label(p + Vector3.up * 1.2f, $"R2 {F(report.R2)} ft", EditorStyles.whiteMiniLabel);
        }
    }

    #endregion
}
