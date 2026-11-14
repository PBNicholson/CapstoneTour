using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Plain data containers for the tour import pipeline.
/// </summary>
public class TourImporterData
{
    /// <summary> One entry per non-blank data row in the CSV, in file order.</summary>
    public List<NodeImportRow> rows = new List<NodeImportRow>();

    /// <summary>
    /// All errors accumulated across the parse and validate phases. When not empty,
    /// the import must abort befpre any assets are touched.
    /// </summary>
    public List<string> errors = new List<string>();

    /// <summary>
    /// Index into <see cref="rows"/> of the resolved start node, or -1 if unresolved.
    /// </summary>
    public int startNodeRowIndex = -1;

    public bool HasErrors => errors.Count > 0;

    public void AddError(string message)
    {
        errors.Add(message);
    }
}

/// <summary>
/// One parsed CSV data row.
/// </summary>
public class NodeImportRow
{
    /// <summary>
    /// 1-based physical line number in the source CSV file.
    /// Used for error reporting.
    /// </summary>
    public int sourceLine;

    // --- Raw cell values. ---
    public string rawNodeId;
    public string rawDisplayName;
    public string rawFloor;
    public string rawPositionX;
    public string rawPositionY;
    public string rawPositionZ;
    public string rawPanoramaRotation;
    public string rawPanoramaFilename;
    public string rawIsStartNode;

    // --- Typed values ---
    public string nodeId;
    public string displayName;
    public int floor;
    public Vector3 position;
    public float panoramaRotation;
    public string panoramaFilename;
    public bool isStartNode;
    public string importedPanoramaGuid;
}
