using System;
using System.Collections.Generic;
using UnityEngine;

// JSON transfer types for BuildingLayout, schema version 1. JsonUtility-compatible: public fields only,
// no properties, no nullables, root is an object.
//
// These types deliberately have NO field initializers. Whether or not JsonUtility applies initializers to
// fields missing from the JSON, a missing field here always reads as 0 / false / null, which validation
// then reports. See the schema in the session notes for which fields are required.

[Serializable]
public class BuildingLayoutFile
{
    public int schemaVersion;
    public string layoutName;
    public float defaultFloorY;
    public float defaultCeilingY;
    public float wallMatchTolerance;
    public RoomJson[] rooms;
    public DoorwayJson[] doorways;
}

[Serializable]
public class RoomJson
{
    public string name;
    public float xMin;
    public float xMax;
    public float zMin;
    public float zMax;
    public bool overrideHeights;
    public float floorY;
    public float ceilingY;
}

[Serializable]
public class DoorwayJson
{
    public string name;
    public float centerX;
    public float centerZ;
    public string runsAlong;
    public float width;
    public float height;
}

/// <summary>
/// Parses layout JSON into a temporary (unsaved) BuildingLayout instance. The caller validates it,
/// copies it into the target asset, and destroys the temporary instance.
/// </summary>
public static class BuildingLayoutJson
{
    /// <summary>
    /// Returns a temporary BuildingLayout (HideFlags.DontSave) or null if the JSON cannot be mapped.
    /// Structural problems (unparseable JSON, wrong schema version, bad runsAlong) go to <paramref name="errors"/>.
    /// Content problems are left for BuildingLayoutValidator.
    /// </summary>
    public static BuildingLayout ParseToTemporaryLayout(string json, List<string> errors)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            errors.Add("JSON file is empty.");
            return null;
        }

        BuildingLayoutFile file;
        try
        {
            file = JsonUtility.FromJson<BuildingLayoutFile>(json);
        }
        catch (Exception ex)
        {
            errors.Add($"JSON could not be parsed: {ex.Message}");
            return null;
        }

        if (file == null)
        {
            errors.Add("JSON parsed to nothing. The root must be a JSON object.");
            return null;
        }

        if (file.schemaVersion != LayoutConstants.SchemaVersion)
        {
            errors.Add($"schemaVersion is {file.schemaVersion}; expected {LayoutConstants.SchemaVersion}. " +
                       "0 usually means the field is missing.");
        }

        if (file.rooms == null)
            errors.Add("'rooms' array is missing.");

        BuildingLayout layout = ScriptableObject.CreateInstance<BuildingLayout>();
        layout.hideFlags = HideFlags.DontSave;

        layout.layoutName = file.layoutName ?? string.Empty;
        layout.defaultFloorY = file.defaultFloorY;
        layout.defaultCeilingY = file.defaultCeilingY;
        layout.wallMatchTolerance = file.wallMatchTolerance;
        layout.rooms = new List<LayoutRoom>();
        layout.doorways = new List<LayoutDoorway>();

        if (file.rooms != null)
        {
            for (int i = 0; i < file.rooms.Length; i++)
            {
                RoomJson rj = file.rooms[i];
                if (rj == null)
                {
                    errors.Add($"rooms[{i}] is null.");
                    continue;
                }

                layout.rooms.Add(new LayoutRoom
                {
                    name = rj.name ?? string.Empty,
                    xMin = rj.xMin,
                    xMax = rj.xMax,
                    zMin = rj.zMin,
                    zMax = rj.zMax,
                    overrideHeights = rj.overrideHeights,
                    floorY = rj.floorY,
                    ceilingY = rj.ceilingY
                });
            }
        }

        if (file.doorways != null)
        {
            for (int j = 0; j < file.doorways.Length; j++)
            {
                DoorwayJson dj = file.doorways[j];
                if (dj == null)
                {
                    errors.Add($"doorways[{j}] is null.");
                    continue;
                }

                if (!TryParseAxis(dj.runsAlong, out WallAxis axis))
                {
                    string shown = string.IsNullOrEmpty(dj.runsAlong) ? "(missing)" : $"'{dj.runsAlong}'";
                    errors.Add($"doorways[{j}] ('{dj.name}'): runsAlong {shown} must be \"x\" or \"z\".");
                    continue;
                }

                layout.doorways.Add(new LayoutDoorway
                {
                    name = dj.name ?? string.Empty,
                    centerX = dj.centerX,
                    centerZ = dj.centerZ,
                    runsAlong = axis,
                    width = dj.width,
                    height = dj.height
                });
            }
        }

        if (errors.Count > 0)
        {
            UnityEngine.Object.DestroyImmediate(layout);
            return null;
        }

        return layout;
    }

    /// <summary>
    /// Copies every layout field from <paramref name="source"/> to <paramref name="target"/>, replacing the lists.
    /// </summary>
    public static void CopyLayout(BuildingLayout source, BuildingLayout target)
    {
        target.layoutName = source.layoutName;
        target.defaultFloorY = source.defaultFloorY;
        target.defaultCeilingY = source.defaultCeilingY;
        target.wallMatchTolerance = source.wallMatchTolerance;
        target.rooms = new List<LayoutRoom>(source.rooms);
        target.doorways = new List<LayoutDoorway>(source.doorways);
    }

    private static bool TryParseAxis(string value, out WallAxis axis)
    {
        string v = value?.Trim();

        if (string.Equals(v, "x", StringComparison.OrdinalIgnoreCase))
        {
            axis = WallAxis.X;
            return true;
        }

        if (string.Equals(v, "z", StringComparison.OrdinalIgnoreCase))
        {
            axis = WallAxis.Z;
            return true;
        }

        axis = WallAxis.Z;
        return false;
    }
}