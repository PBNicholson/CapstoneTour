using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

/// <summary>
/// One room wall that a doorway lies on.
/// </summary>
public class DoorwayWallMatch
{
    /// <summary>Index into BuildingLayout.rooms.</summary>
    public int roomIndex;

    /// <summary>The matched wall.</summary>
    public RoomWall wall;

    /// <summary>Perpendicular distance from the doorway center to the wall line (ft).</summary>
    public float distance;

    /// <summary>True if the whole opening lies within the wall's extent.</summary>
    public bool contained;
}

/// <summary>
/// A doorway after matching against room walls.
/// </summary>
public class ResolvedDoorway
{
    public int doorwayIndex;
    public LayoutDoorway doorway;
    public readonly List<DoorwayWallMatch> matches = new List<DoorwayWallMatch>();

    /// <summary>True if any validation error refers to this doorway (used to draw it in the error colour).</summary>
    public bool hasError;

    /// <summary>Floor Y shared by all matched rooms (valid only when the doorway has no errors).</summary>
    public float floorY;

    /// <summary>True when the doorway matches two opposite walls separated by a wall-thickness gap.</summary>
    public bool needsPassage;

    /// <summary>Lower wall-line coordinate of the passage (valid when needsPassage).</summary>
    public float passageLineMin;

    /// <summary>Upper wall-line coordinate of the passage (valid when needsPassage).</summary>
    public float passageLineMax;
}

/// <summary>
/// Errors, warnings, and resolved doorway matches for a layout.
/// </summary>
public class LayoutValidationResult
{
    public readonly List<string> errors = new List<string>();
    public readonly List<string> warnings = new List<string>();

    /// <summary>One entry per BuildingLayout.doorways element, in the same order.</summary>
    public readonly List<ResolvedDoorway> doorways = new List<ResolvedDoorway>();

    /// <summary>Per room: true if its bounds passed validation (only those rooms take part in doorway matching).</summary>
    public bool[] roomBoundsValid = Array.Empty<bool>();

    public bool HasErrors => errors.Count > 0;
}

/// <summary>
/// Validates a BuildingLayout and resolves which room walls each doorway cuts.
/// Collects every problem before returning. Pure: no scene or asset access, no UnityEditor.
/// </summary>
public static class BuildingLayoutValidator
{
    #region Entry Point

    public static LayoutValidationResult Validate(BuildingLayout layout)
    {
        LayoutValidationResult result = new LayoutValidationResult();

        if (layout == null)
        {
            result.errors.Add("No layout assigned.");
            return result;
        }

        // --- Layout-level fields. ---
        if (string.IsNullOrWhiteSpace(layout.layoutName))
            result.errors.Add("layoutName is required but empty.");

        if (!IsFinite(layout.defaultFloorY) || !IsFinite(layout.defaultCeilingY))
            result.errors.Add("defaultFloorY / defaultCeilingY must be finite numbers.");
        else if (layout.defaultCeilingY <= layout.defaultFloorY + LayoutConstants.Epsilon)
            result.errors.Add($"defaultCeilingY ({Fmt(layout.defaultCeilingY)}) must be above defaultFloorY ({Fmt(layout.defaultFloorY)}). " +
                              "If both are 0, the fields are probably missing from the JSON.");

        float tolerance = layout.wallMatchTolerance;
        if (!IsFinite(tolerance) || tolerance < 0f)
        {
            result.errors.Add($"wallMatchTolerance ({tolerance.ToString(CultureInfo.InvariantCulture)}) must be a finite number >= 0.");
            tolerance = 0f; // Continue checking with exact matching.
        }

        List<LayoutRoom> rooms = layout.rooms ?? new List<LayoutRoom>();
        List<LayoutDoorway> doorways = layout.doorways ?? new List<LayoutDoorway>();

        if (rooms.Count == 0)
            result.errors.Add("Layout has no rooms.");

        ValidateRooms(layout, rooms, tolerance, result);
        ValidateDoorways(layout, rooms, doorways, tolerance, result);

        return result;
    }

    #endregion

    #region Rooms

    private static void ValidateRooms(BuildingLayout layout, List<LayoutRoom> rooms, float tolerance, LayoutValidationResult result)
    {
        result.roomBoundsValid = new bool[rooms.Count];
        Dictionary<string, int> firstIndexByName = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        // A room must be wider than 2 x tolerance so one doorway can never match two opposite walls of the same room.
        float minSide = Mathf.Max(LayoutConstants.MinRoomSide, 2f * tolerance + LayoutConstants.Epsilon);

        for (int i = 0; i < rooms.Count; i++)
        {
            LayoutRoom room = rooms[i];
            if (room == null)
            {
                result.errors.Add($"Room #{i}: entry is null.");
                continue;
            }

            string label = RoomLabel(rooms, i);

            // --- Name. ---
            if (string.IsNullOrWhiteSpace(room.name))
                result.errors.Add($"{label}: name is required but empty.");
            else if (firstIndexByName.TryGetValue(room.name.Trim(), out int firstIndex))
                result.errors.Add($"{label}: duplicate room name (first used by room #{firstIndex}; names are case-insensitive).");
            else
                firstIndexByName[room.name.Trim()] = i;

            // --- Bounds. ---
            if (!IsFinite(room.xMin) || !IsFinite(room.xMax) || !IsFinite(room.zMin) || !IsFinite(room.zMax))
            {
                result.errors.Add($"{label}: bounds contain a non-finite number.");
            }
            else
            {
                bool xOk = CheckSide(label, "x", room.xMin, room.xMax, minSide, result);
                bool zOk = CheckSide(label, "z", room.zMin, room.zMax, minSide, result);
                result.roomBoundsValid[i] = xOk && zOk;
            }

            // --- Heights. ---
            float floorY = layout.GetFloorY(room);
            float ceilingY = layout.GetCeilingY(room);
            string source = room.overrideHeights ? "room override" : "layout defaults";

            if (!IsFinite(floorY) || !IsFinite(ceilingY))
            {
                result.errors.Add($"{label}: floorY / ceilingY must be finite numbers ({source}).");
            }
            else if (ceilingY <= floorY + LayoutConstants.Epsilon)
            {
                result.errors.Add($"{label}: ceilingY ({Fmt(ceilingY)}) must be above floorY ({Fmt(floorY)}) ({source}).");
            }
            else if (ceilingY - floorY < LayoutConstants.SuspiciousRoomHeight)
            {
                result.warnings.Add($"{label}: room height is {Fmt(ceilingY - floorY)} ft (under {Fmt(LayoutConstants.SuspiciousRoomHeight)}). " +
                                    $"Check floorY / ceilingY ({source}); a missing field reads as 0.");
            }
        }

        // --- Overlap (rooms may share edges, not area). ---
        for (int i = 0; i < rooms.Count; i++)
        {
            if (!result.roomBoundsValid[i])
                continue;

            for (int j = i + 1; j < rooms.Count; j++)
            {
                if (!result.roomBoundsValid[j])
                    continue;

                LayoutRoom a = rooms[i];
                LayoutRoom b = rooms[j];

                float x0 = Mathf.Max(a.xMin, b.xMin);
                float x1 = Mathf.Min(a.xMax, b.xMax);
                float z0 = Mathf.Max(a.zMin, b.zMin);
                float z1 = Mathf.Min(a.zMax, b.zMax);

                if (x1 - x0 > LayoutConstants.Epsilon && z1 - z0 > LayoutConstants.Epsilon)
                {
                    result.errors.Add($"{RoomLabel(rooms, i)} and {RoomLabel(rooms, j)} overlap over x {Fmt(x0)}..{Fmt(x1)}, z {Fmt(z0)}..{Fmt(z1)}. " +
                                      "Rooms may share wall lines but not area.");
                }
            }
        }
    }

    private static bool CheckSide(string label, string axis, float min, float max, float minSide, LayoutValidationResult result)
    {
        float size = max - min;

        if (size < -LayoutConstants.Epsilon)
        {
            result.errors.Add($"{label}: {axis}Max ({Fmt(max)}) is less than {axis}Min ({Fmt(min)}).");
            return false;
        }

        if (size < minSide)
        {
            result.errors.Add($"{label}: {axis} size is {Fmt(size)} ft (zero-size or too small); minimum is {Fmt(minSide)} ft " +
                              $"(at least {Fmt(LayoutConstants.MinRoomSide)} ft and more than 2 x wallMatchTolerance).");
            return false;
        }

        return true;
    }

    #endregion

    #region Doorways

    private static void ValidateDoorways(BuildingLayout layout, List<LayoutRoom> rooms, List<LayoutDoorway> doorways,
                                         float tolerance, LayoutValidationResult result)
    {
        Dictionary<string, int> firstIndexByName = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        for (int j = 0; j < doorways.Count; j++)
        {
            LayoutDoorway d = doorways[j];
            ResolvedDoorway resolved = new ResolvedDoorway { doorwayIndex = j, doorway = d };
            result.doorways.Add(resolved);

            int errorsBefore = result.errors.Count;
            ValidateSingleDoorway(layout, rooms, doorways, j, tolerance, firstIndexByName, resolved, result);
            resolved.hasError = result.errors.Count > errorsBefore;
        }

        CheckOverlappingDoorways(rooms, doorways, result);
    }

    /// <summary>
    /// Name, size, wall matching, containment, height, and pairing checks for one doorway.
    /// </summary>
    private static void ValidateSingleDoorway(BuildingLayout layout, List<LayoutRoom> rooms, List<LayoutDoorway> doorways, int j,
                                              float tolerance, Dictionary<string, int> firstIndexByName,
                                              ResolvedDoorway resolved, LayoutValidationResult result)
    {
        LayoutDoorway d = doorways[j];

        if (d == null)
        {
            result.errors.Add($"Doorway #{j}: entry is null.");
            return;
        }

        string label = DoorwayLabel(doorways, j);

        // --- Name. ---
        if (string.IsNullOrWhiteSpace(d.name))
            result.errors.Add($"{label}: name is required but empty.");
        else if (firstIndexByName.TryGetValue(d.name.Trim(), out int firstIndex))
            result.errors.Add($"{label}: duplicate doorway name (first used by doorway #{firstIndex}; names are case-insensitive).");
        else
            firstIndexByName[d.name.Trim()] = j;

        // --- Numbers and size. ---
        if (!IsFinite(d.centerX) || !IsFinite(d.centerZ) || !IsFinite(d.width) || !IsFinite(d.height))
        {
            result.errors.Add($"{label}: position or size contains a non-finite number.");
            return;
        }

        bool sizeOk = true;
        if (d.width <= LayoutConstants.Epsilon)
        {
            result.errors.Add($"{label}: width ({Fmt(d.width)}) must be greater than 0. A missing field reads as 0.");
            sizeOk = false;
        }
        if (d.height <= LayoutConstants.Epsilon)
        {
            result.errors.Add($"{label}: height ({Fmt(d.height)}) must be greater than 0. A missing field reads as 0.");
            sizeOk = false;
        }
        if (!sizeOk)
            return;

        // --- Wall matching. ---
        FindMatches(rooms, result.roomBoundsValid, d, tolerance, resolved);

        if (resolved.matches.Count == 0)
        {
            result.errors.Add(NoWallMessage(rooms, result.roomBoundsValid, d, label, tolerance));
            return;
        }

        foreach (DoorwayWallMatch match in resolved.matches)
        {
            LayoutRoom room = rooms[match.roomIndex];
            string wallLabel = WallLabel(rooms, match.roomIndex, match.wall);

            if (!match.contained)
            {
                string message = $"{label} ({DoorwaySpan(d)}) extends past the end of {wallLabel} ({WallSpan(match.wall)}).";
                if (d.width > match.wall.Length + LayoutConstants.Epsilon)
                    message += $" The doorway is wider than the wall ({Fmt(d.width)} > {Fmt(match.wall.Length)} ft).";
                result.errors.Add(message);
            }

            float roomHeight = layout.GetCeilingY(room) - layout.GetFloorY(room);
            if (d.height > roomHeight + LayoutConstants.Epsilon)
            {
                result.errors.Add($"{label}: height {Fmt(d.height)} ft exceeds the height of {RoomLabel(rooms, match.roomIndex)} " +
                                  $"({Fmt(roomHeight)} ft).");
            }
        }

        ResolvePairing(layout, rooms, result.roomBoundsValid, d, label, tolerance, resolved, result);
    }

    /// <summary>
    /// Finds every wall of a valid room that runs along the doorway's axis, lies within tolerance of the
    /// doorway's line, and overlaps the opening interval by more than epsilon.
    /// </summary>
    private static void FindMatches(List<LayoutRoom> rooms, bool[] roomBoundsValid, LayoutDoorway d, float tolerance, ResolvedDoorway resolved)
    {
        for (int i = 0; i < rooms.Count; i++)
        {
            if (!roomBoundsValid[i])
                continue;

            foreach (WallSide side in LayoutMath.AllSides)
            {
                RoomWall wall = rooms[i].GetWall(side);
                if (wall.runsAlong != d.runsAlong)
                    continue;

                float distance = Mathf.Abs(d.LineCoordinate - wall.line);
                if (distance > tolerance + LayoutConstants.Epsilon)
                    continue;

                float overlap = Mathf.Min(d.AlongMax, wall.max) - Mathf.Max(d.AlongMin, wall.min);
                if (overlap <= LayoutConstants.Epsilon)
                    continue;

                bool contained = d.AlongMin >= wall.min - LayoutConstants.Epsilon
                              && d.AlongMax <= wall.max + LayoutConstants.Epsilon;

                resolved.matches.Add(new DoorwayWallMatch
                {
                    roomIndex = i,
                    wall = wall,
                    distance = distance,
                    contained = contained
                });
            }
        }
    }

    /// <summary>
    /// Checks the 1- or 2-wall case: shared floor height, opposite facing, passage gap, and single-wall warnings.
    /// </summary>
    private static void ResolvePairing(BuildingLayout layout, List<LayoutRoom> rooms, bool[] roomBoundsValid,
                                       LayoutDoorway d, string label, float tolerance,
                                       ResolvedDoorway resolved, LayoutValidationResult result)
    {
        List<DoorwayWallMatch> matches = resolved.matches;

        if (matches.Count > 2)
        {
            List<string> names = new List<string>();
            foreach (DoorwayWallMatch m in matches)
                names.Add(WallLabel(rooms, m.roomIndex, m.wall));
            result.errors.Add($"{label} lies on {matches.Count} walls ({string.Join(", ", names)}); expected 1 or 2. " +
                              "Reduce wallMatchTolerance or check for overlapping rooms.");
            return;
        }

        if (matches.Count == 1)
        {
            DoorwayWallMatch only = matches[0];
            resolved.floorY = layout.GetFloorY(rooms[only.roomIndex]);

            result.warnings.Add($"{label} lies only on {WallLabel(rooms, only.roomIndex, only.wall)}. The opening leads outside the layout; " +
                                "rays through it can hit faces of unrelated rooms.");

            WarnNearMisses(rooms, roomBoundsValid, d, label, tolerance, only, result);
            return;
        }

        // --- Exactly two walls. ---
        DoorwayWallMatch a = matches[0];
        DoorwayWallMatch b = matches[1];

        if (a.roomIndex == b.roomIndex)
        {
            result.errors.Add($"{label} lies on two walls of the same room ({RoomLabel(rooms, a.roomIndex)}). " +
                              "The room is too narrow for the current wallMatchTolerance.");
            return;
        }

        if (Vector3.Dot(a.wall.inwardNormal, b.wall.inwardNormal) > -0.5f)
        {
            result.errors.Add($"{label}: {WallLabel(rooms, a.roomIndex, a.wall)} and {WallLabel(rooms, b.roomIndex, b.wall)} " +
                              "face the same direction. Check for overlapping rooms.");
            return;
        }

        float floorA = layout.GetFloorY(rooms[a.roomIndex]);
        float floorB = layout.GetFloorY(rooms[b.roomIndex]);
        if (Mathf.Abs(floorA - floorB) > LayoutConstants.Epsilon)
        {
            result.errors.Add($"{label} connects rooms with different floor heights ({RoomLabel(rooms, a.roomIndex)} {Fmt(floorA)}, " +
                              $"{RoomLabel(rooms, b.roomIndex)} {Fmt(floorB)}). Not supported in schema v1.");
            return;
        }

        resolved.floorY = floorA;
        resolved.passageLineMin = Mathf.Min(a.wall.line, b.wall.line);
        resolved.passageLineMax = Mathf.Max(a.wall.line, b.wall.line);
        resolved.needsPassage = resolved.passageLineMax - resolved.passageLineMin > LayoutConstants.PassageMinGap;
    }

    /// <summary>
    /// For a single-wall doorway, warns about parallel walls just outside the tolerance that overlap the opening:
    /// these usually mean the tolerance is smaller than the wall-thickness gap.
    /// </summary>
    private static void WarnNearMisses(List<LayoutRoom> rooms, bool[] roomBoundsValid, LayoutDoorway d, string label,
                                       float tolerance, DoorwayWallMatch matched, LayoutValidationResult result)
    {
        float limit = Mathf.Max(3f * tolerance, 2f);

        for (int i = 0; i < rooms.Count; i++)
        {
            if (!roomBoundsValid[i] || i == matched.roomIndex)
                continue;

            foreach (WallSide side in LayoutMath.AllSides)
            {
                RoomWall wall = rooms[i].GetWall(side);
                if (wall.runsAlong != d.runsAlong)
                    continue;

                float distance = Mathf.Abs(d.LineCoordinate - wall.line);
                if (distance <= tolerance + LayoutConstants.Epsilon || distance > limit)
                    continue;

                float overlap = Mathf.Min(d.AlongMax, wall.max) - Mathf.Max(d.AlongMin, wall.min);
                if (overlap <= LayoutConstants.Epsilon)
                    continue;

                result.warnings.Add($"{label}: {WallLabel(rooms, i, wall)} is {Fmt(distance)} ft from the doorway line " +
                                    $"(tolerance {Fmt(tolerance)}). If the doorway should open into that room, increase wallMatchTolerance.");
            }
        }
    }

    private static void CheckOverlappingDoorways(List<LayoutRoom> rooms, List<LayoutDoorway> doorways, LayoutValidationResult result)
    {
        for (int j = 0; j < result.doorways.Count; j++)
        {
            ResolvedDoorway dj = result.doorways[j];
            if (dj.doorway == null || dj.matches.Count == 0)
                continue;

            for (int k = j + 1; k < result.doorways.Count; k++)
            {
                ResolvedDoorway dk = result.doorways[k];
                if (dk.doorway == null || dk.matches.Count == 0)
                    continue;

                List<string> sharedWalls = new List<string>();

                foreach (DoorwayWallMatch mj in dj.matches)
                {
                    foreach (DoorwayWallMatch mk in dk.matches)
                    {
                        if (mj.roomIndex != mk.roomIndex || mj.wall.side != mk.wall.side)
                            continue;

                        float overlap = Mathf.Min(dj.doorway.AlongMax, dk.doorway.AlongMax)
                                      - Mathf.Max(dj.doorway.AlongMin, dk.doorway.AlongMin);
                        if (overlap > LayoutConstants.Epsilon)
                            sharedWalls.Add(WallLabel(rooms, mj.roomIndex, mj.wall));
                    }
                }

                if (sharedWalls.Count > 0)
                {
                    dj.hasError = true;
                    dk.hasError = true;
                    float o0 = Mathf.Max(dj.doorway.AlongMin, dk.doorway.AlongMin);
                    float o1 = Mathf.Min(dj.doorway.AlongMax, dk.doorway.AlongMax);
                    result.errors.Add($"{DoorwayLabel(doorways, j)} and {DoorwayLabel(doorways, k)} overlap on " +
                                      $"{string.Join(" and ", sharedWalls)} ({AxisName(dj.doorway.runsAlong)} {Fmt(o0)}..{Fmt(o1)}).");
                }
            }
        }
    }

    private static string NoWallMessage(List<LayoutRoom> rooms, bool[] roomBoundsValid, LayoutDoorway d, string label, float tolerance)
    {
        string message = $"{label} lies on no room wall ({DoorwaySpan(d)}, runsAlong \"{AxisName(d.runsAlong)}\", " +
                         $"tolerance {Fmt(tolerance)} ft).";

        // Nearest wall of any orientation, by distance from the doorway center to the wall segment.
        Vector2 center = new Vector2(d.centerX, d.centerZ);
        float bestDistance = float.MaxValue;
        int bestRoom = -1;
        RoomWall bestWall = default(RoomWall);

        for (int i = 0; i < rooms.Count; i++)
        {
            if (!roomBoundsValid[i])
                continue;

            foreach (WallSide side in LayoutMath.AllSides)
            {
                RoomWall wall = rooms[i].GetWall(side);
                Vector3 p0 = LayoutMath.WallToWorld(wall.runsAlong, wall.line, wall.min, 0f);
                Vector3 p1 = LayoutMath.WallToWorld(wall.runsAlong, wall.line, wall.max, 0f);
                float distance = DistancePointToSegment(center, new Vector2(p0.x, p0.z), new Vector2(p1.x, p1.z));

                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    bestRoom = i;
                    bestWall = wall;
                }
            }
        }

        if (bestRoom < 0)
            return message + " No valid rooms to match against.";

        message += $" Nearest wall: {WallLabel(rooms, bestRoom, bestWall)} ({WallSpan(bestWall)}), {Fmt(bestDistance)} ft from the doorway center, " +
                   $"runs along \"{AxisName(bestWall.runsAlong)}\".";

        if (bestWall.runsAlong != d.runsAlong && bestDistance <= tolerance + LayoutConstants.Epsilon)
            message += $" Did you mean runsAlong \"{AxisName(bestWall.runsAlong)}\"?";

        return message;
    }

    #endregion

    #region Formatting Helpers (shared with editor tooling)

    /// <summary>Invariant-culture number formatting with up to 2 decimals.</summary>
    public static string Fmt(float value)
    {
        return value.ToString("0.##", CultureInfo.InvariantCulture);
    }

    /// <summary>"x" or "z".</summary>
    public static string AxisName(WallAxis axis)
    {
        return axis == WallAxis.X ? "x" : "z";
    }

    /// <summary>Name of the constant coordinate for a wall running along <paramref name="runsAlong"/>.</summary>
    public static string LineAxisName(WallAxis runsAlong)
    {
        return runsAlong == WallAxis.Z ? "x" : "z";
    }

    public static string RoomLabel(List<LayoutRoom> rooms, int index)
    {
        LayoutRoom room = rooms[index];
        return room == null || string.IsNullOrWhiteSpace(room.name) ? $"Room #{index}" : $"Room '{room.name}'";
    }

    public static string DoorwayLabel(List<LayoutDoorway> doorways, int index)
    {
        LayoutDoorway d = doorways[index];
        return d == null || string.IsNullOrWhiteSpace(d.name) ? $"Doorway #{index}" : $"Doorway '{d.name}'";
    }

    public static string WallLabel(List<LayoutRoom> rooms, int roomIndex, RoomWall wall)
    {
        return $"{RoomLabel(rooms, roomIndex)} {LayoutMath.SideLabel(wall.side)} wall";
    }

    /// <summary>e.g. "x=2.5, z 3..17".</summary>
    public static string WallSpan(RoomWall wall)
    {
        return $"{LineAxisName(wall.runsAlong)}={Fmt(wall.line)}, {AxisName(wall.runsAlong)} {Fmt(wall.min)}..{Fmt(wall.max)}";
    }

    /// <summary>e.g. "x=2.5, z 8.5..11.5".</summary>
    public static string DoorwaySpan(LayoutDoorway d)
    {
        return $"{LineAxisName(d.runsAlong)}={Fmt(d.LineCoordinate)}, {AxisName(d.runsAlong)} {Fmt(d.AlongMin)}..{Fmt(d.AlongMax)}";
    }

    #endregion

    #region Math Helpers

    private static bool IsFinite(float value)
    {
        return !float.IsNaN(value) && !float.IsInfinity(value);
    }

    private static float DistancePointToSegment(Vector2 p, Vector2 a, Vector2 b)
    {
        Vector2 ab = b - a;
        float lengthSqr = ab.sqrMagnitude;
        if (lengthSqr < 1e-12f)
            return Vector2.Distance(p, a);

        float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / lengthSqr);
        return Vector2.Distance(p, a + ab * t);
    }

    #endregion
}
