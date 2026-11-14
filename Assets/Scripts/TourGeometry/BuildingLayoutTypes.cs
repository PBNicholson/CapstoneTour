using System;
using UnityEngine;

/// <summary>
/// Axis a wall line runs along. A wall at constant x runs along Z; a wall at constant z runs along X.
/// </summary>
public enum WallAxis
{
    X = 0,
    Z = 1
}

/// <summary>
/// The four walls of an axis-aligned room, named by the side of the room they sit on.
/// NegX = wall at xMin, PosX = wall at xMax, NegZ = wall at zMin, PosZ = wall at zMax.
/// Axis names are used instead of compass names because TourData.northOffset decouples +Z from true north.
/// </summary>
public enum WallSide
{
    NegX = 0,
    PosX = 1,
    NegZ = 2,
    PosZ = 3
}

/// <summary>
/// Shared constants for layout validation and geometry generation. All values are in feet (1 Unity unit = 1 ft).
/// </summary>
public static class LayoutConstants
{
    /// <summary>Supported JSON schema version.</summary>
    public const int SchemaVersion = 1;

    /// <summary>Comparison tolerance for coordinates (ft).</summary>
    public const float Epsilon = 0.001f;

    /// <summary>Minimum room side length (ft). Rooms must also be wider than 2 x wallMatchTolerance.</summary>
    public const float MinRoomSide = 1f;

    /// <summary>Rooms shorter than this (ft) produce a warning; usually indicates a missing height field.</summary>
    public const float SuspiciousRoomHeight = 6f;

    /// <summary>Quads are subdivided so no edge exceeds this length (ft). Keeps triangles far below the PhysX 500-unit warning.</summary>
    public const float MaxQuadEdge = 64f;

    /// <summary>Wall pieces shorter than this (ft) along either dimension are not generated.</summary>
    public const float MinPieceLength = 0.01f;

    /// <summary>Two matched walls farther apart than this (ft) get a passage (threshold, head, jambs) between them.</summary>
    public const float PassageMinGap = 0.01f;
}

/// <summary>
/// One wall of a room, resolved to a line and an extent.
/// </summary>
public struct RoomWall
{
    /// <summary>Which side of the room this wall is on.</summary>
    public WallSide side;

    /// <summary>Axis the wall runs along.</summary>
    public WallAxis runsAlong;

    /// <summary>Constant coordinate of the wall line: x for walls running along Z, z for walls running along X.</summary>
    public float line;

    /// <summary>Start of the wall's extent along <see cref="runsAlong"/>.</summary>
    public float min;

    /// <summary>End of the wall's extent along <see cref="runsAlong"/>.</summary>
    public float max;

    /// <summary>Unit normal pointing into the room.</summary>
    public Vector3 inwardNormal;

    public float Length => max - min;
}

/// <summary>
/// An axis-aligned rectangular room in world space (feet).
/// </summary>
[Serializable]
public class LayoutRoom
{
    [Tooltip("Unique room name (case-insensitive). Used for generated object names, gizmo labels, and reports.")]
    public string name = "Room";

    [Header("Bounds (world space, feet)")]

    [Tooltip("Edge of the room on the -X side.")]
    public float xMin;

    [Tooltip("Edge of the room on the +X side.")]
    public float xMax;

    [Tooltip("Edge of the room on the -Z side.")]
    public float zMin;

    [Tooltip("Edge of the room on the +Z side.")]
    public float zMax;

    [Header("Heights (world Y, feet)")]

    [Tooltip("When false, the layout's defaultFloorY / defaultCeilingY are used and the two fields below are ignored.")]
    public bool overrideHeights;

    [Tooltip("Floor plane Y. Used only when overrideHeights is true.")]
    public float floorY = -5.1f;

    [Tooltip("Ceiling plane Y. Used only when overrideHeights is true.")]
    public float ceilingY = 3.9f;

    public float Width => xMax - xMin;
    public float Depth => zMax - zMin;
    public Vector2 CenterXZ => new Vector2((xMin + xMax) * 0.5f, (zMin + zMax) * 0.5f);

    /// <summary>
    /// True if (x, z) lies inside the rectangle, edges included (expanded by <paramref name="epsilon"/>).
    /// </summary>
    public bool ContainsXZ(float x, float z, float epsilon = 0f)
    {
        return x >= xMin - epsilon && x <= xMax + epsilon
            && z >= zMin - epsilon && z <= zMax + epsilon;
    }

    /// <summary>
    /// Resolves one wall of this room to its line, extent, and inward normal.
    /// </summary>
    public RoomWall GetWall(WallSide side)
    {
        switch (side)
        {
            case WallSide.NegX:
                return new RoomWall { side = side, runsAlong = WallAxis.Z, line = xMin, min = zMin, max = zMax, inwardNormal = Vector3.right };
            case WallSide.PosX:
                return new RoomWall { side = side, runsAlong = WallAxis.Z, line = xMax, min = zMin, max = zMax, inwardNormal = Vector3.left };
            case WallSide.NegZ:
                return new RoomWall { side = side, runsAlong = WallAxis.X, line = zMin, min = xMin, max = xMax, inwardNormal = Vector3.forward };
            case WallSide.PosZ:
                return new RoomWall { side = side, runsAlong = WallAxis.X, line = zMax, min = xMin, max = xMax, inwardNormal = Vector3.back };
            default:
                throw new ArgumentOutOfRangeException(nameof(side), side, null);
        }
    }
}

/// <summary>
/// A doorway, defined once in world space. The opening is the segment
/// [center - width/2, center + width/2] along <see cref="runsAlong"/>, on the wall line through the center,
/// from the room floor up to floor + height. The generator cuts it in every room wall it lies on.
/// </summary>
[Serializable]
public class LayoutDoorway
{
    [Tooltip("Unique doorway name (case-insensitive).")]
    public string name = "Doorway";

    [Header("Position (world space, feet)")]

    [Tooltip("X of the opening's midpoint.")]
    public float centerX;

    [Tooltip("Z of the opening's midpoint.")]
    public float centerZ;

    [Tooltip("Axis the wall line runs along. A doorway in a wall at constant x runs along Z.")]
    public WallAxis runsAlong = WallAxis.Z;

    [Header("Size (feet)")]

    [Tooltip("Opening width along the wall.")]
    public float width = 3f;

    [Tooltip("Opening height, measured up from the room floor.")]
    public float height = 7f;

    /// <summary>Constant coordinate of the wall line the doorway sits on (x if running along Z, z if running along X).</summary>
    public float LineCoordinate => runsAlong == WallAxis.Z ? centerX : centerZ;

    /// <summary>Center of the opening along <see cref="runsAlong"/>.</summary>
    public float AlongCenter => runsAlong == WallAxis.Z ? centerZ : centerX;

    public float AlongMin => AlongCenter - width * 0.5f;
    public float AlongMax => AlongCenter + width * 0.5f;
}

/// <summary>
/// Coordinate helpers shared by the validator, builder, gizmos, and report.
/// </summary>
public static class LayoutMath
{
    public static readonly WallSide[] AllSides = { WallSide.NegX, WallSide.PosX, WallSide.NegZ, WallSide.PosZ };

    /// <summary>
    /// Converts wall-local coordinates (line, along, y) to a world position.
    /// For walls running along Z: line = x, along = z. For walls running along X: line = z, along = x.
    /// </summary>
    public static Vector3 WallToWorld(WallAxis runsAlong, float line, float along, float y)
    {
        return runsAlong == WallAxis.Z
            ? new Vector3(line, y, along)
            : new Vector3(along, y, line);
    }

    /// <summary>Short ASCII label for a wall side, e.g. "+X".</summary>
    public static string SideLabel(WallSide side)
    {
        switch (side)
        {
            case WallSide.NegX: return "-X";
            case WallSide.PosX: return "+X";
            case WallSide.NegZ: return "-Z";
            case WallSide.PosZ: return "+Z";
            default: return side.ToString();
        }
    }
}
