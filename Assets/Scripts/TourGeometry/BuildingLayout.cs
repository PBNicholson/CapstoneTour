using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Data-driven building layout: axis-aligned rooms and doorways in world space (feet).
/// Consumed by the Tour Geometry Generator to build inward-facing TourGeometry colliders.
/// Usually populated by importing JSON (Tools > Tour Geometry Generator); editable in the inspector.
/// </summary>
[CreateAssetMenu(fileName = "New Building Layout", menuName = "Tour/Building Layout", order = 10)]
public class BuildingLayout : ScriptableObject
{
    [Header("Identification")]

    [Tooltip("Required. Name of the generated root object (TourGeometry_<layoutName>).")]
    public string layoutName = string.Empty;

    [Header("Height Defaults (world Y, feet)")]

    [Tooltip("Floor plane Y for rooms that do not override heights. Nodes sit at y = 0 at camera height, " +
             "so the floor is below zero (about -5.1 for a 1.55 m camera).")]
    public float defaultFloorY = -5.1f;

    [Tooltip("Ceiling plane Y for rooms that do not override heights.")]
    public float defaultCeilingY = 3.9f;

    [Header("Doorway Matching")]

    [Tooltip("A doorway cuts every parallel room wall whose line lies within this distance (feet) of the doorway center. " +
             "0 = exact shared wall lines only. Must be larger than the wall-thickness gap between adjacent rooms.")]
    [Min(0f)]
    public float wallMatchTolerance = 1f;

    [Header("Layout")]

    [Tooltip("Axis-aligned rooms. Rooms may share wall lines but must not overlap.")]
    public List<LayoutRoom> rooms = new List<LayoutRoom>();

    [Tooltip("Doorways, each defined once in world space.")]
    public List<LayoutDoorway> doorways = new List<LayoutDoorway>();

    /// <summary>Resolved floor Y for a room (override or layout default).</summary>
    public float GetFloorY(LayoutRoom room)
    {
        return room.overrideHeights ? room.floorY : defaultFloorY;
    }

    /// <summary>Resolved ceiling Y for a room (override or layout default).</summary>
    public float GetCeilingY(LayoutRoom room)
    {
        return room.overrideHeights ? room.ceilingY : defaultCeilingY;
    }

    /// <summary>
    /// Hash of all serialized layout fields. Stored on generated geometry to detect when it is stale.
    /// </summary>
    public string ComputeContentHash()
    {
        return Hash128.Compute(JsonUtility.ToJson(this)).ToString();
    }
}