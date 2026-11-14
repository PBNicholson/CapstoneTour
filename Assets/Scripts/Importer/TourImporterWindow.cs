using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using UnityEngine;

/// <summary>
/// Editor tool that assembles a tour from a CSV manifest: imports panorama
/// texturs with cube settings, creates/updates NodeData assets, and populates a
/// TourData asset. All operations are synchronous editor operations.
/// </summary>
public class TourImporterWindow : EditorWindow
{
    #region Constants

    private const string ToursRoot = "Assets/Tours";

    // CSV header, in required column order.
    private static readonly string[] ExpectedHeaders =
    {
        "node_id", "display_name", "floor",
        "position_x", "position_y", "position_z",
        "panorama_rotation", "panorama_filename", "is_start_node"
    };

    #endregion

    #region Window State

    private string _csvPath = string.Empty;
    private string _buildingName = string.Empty;
    private string _sourceFolder = string.Empty;

    private readonly StringBuilder _log = new StringBuilder();
    private Vector2 _logScroll;

    #endregion

    #region Window Lifecycle

    [MenuItem("Tools/Tour Importer")]
    private static void ShowWindow()
    {
        TourImporterWindow window = GetWindow<TourImporterWindow>();
        window.titleContent = new GUIContent("Tour Importer");
        window.minSize = new Vector2(480f, 360f);
    }

    private void OnGUI()
    {
        EditorGUILayout.LabelField("Inputs", EditorStyles.boldLabel);

        DrawPathField("CSV File", ref _csvPath, isFolder: false);
        _buildingName = EditorGUILayout.TextField("Building Name", _buildingName);
        DrawPathField("Panorama Source Folder", ref _sourceFolder, isFolder: true);

        EditorGUILayout.Space();

        using (new EditorGUI.DisabledScope(!HasRequiredInputs()))
        {
            if (GUILayout.Button("Run Import", GUILayout.Height(28f)))
            {
                RunImport();
            }
        }

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Log", EditorStyles.boldLabel);

        _logScroll = EditorGUILayout.BeginScrollView(_logScroll, GUILayout.ExpandHeight(true));
        EditorGUILayout.SelectableLabel(
            _log.Length > 0 ? _log.ToString() : "(no output yet)",
            EditorStyles.textArea,
            GUILayout.ExpandHeight(true));
        EditorGUILayout.EndScrollView();
    }

    /// <summary>
    /// Draws a labeled text field paired with a Browse button. The browse target
    /// is a file picker (CSV) or a folder picker depending on <paramref name="isFolder"/>.
    /// </summary>
    private void DrawPathField(string label, ref string value, bool isFolder)
    {
        EditorGUILayout.BeginHorizontal();
        value = EditorGUILayout.TextField(label, value);

        if (GUILayout.Button("Browse", GUILayout.Width(70f)))
        {
            string picked = isFolder
                ? EditorUtility.OpenFolderPanel("Select Panorama Source Folder", value, string.Empty)
                : EditorUtility.OpenFilePanel("Select CSV Manifest", value, "csv");

            if (!string.IsNullOrEmpty(picked))
            {
                value = picked;
                GUI.FocusControl(null); // Force the text field to refresh from the new value.
            }
        }
        EditorGUILayout.EndHorizontal();
    }

    private bool HasRequiredInputs()
    {
        return !string.IsNullOrWhiteSpace(_csvPath)
            && !string.IsNullOrWhiteSpace(_buildingName)
            && !string.IsNullOrWhiteSpace(_sourceFolder);
    }

    #endregion

    #region Run Cycle

    /// <summary>
    /// Executes import sequence.
    /// </summary>
    private void RunImport()
    {
        _log.Clear();

        try
        {
            // --- Confirm inputs resolve to real locations. ---
            if (!File.Exists(_csvPath))
            {
                LogError($"CSV file does not exist: {_csvPath}");
                return;
            }

            if (!Directory.Exists(_sourceFolder))
            {
                LogError($"Panorama source folder does not exist: {_sourceFolder}");
                return;
            }

            string buildingName = _buildingName.Trim();
            if (buildingName.Length == 0 || buildingName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            {
                LogError($"Building name is empty or contains characters invalid for a folder name: '{_buildingName}'.");
                return;
            }

            TourImporterData data = new TourImporterData();

            // --- Parse CSV. ---
            if (!ParseCsv(_csvPath, data))
            {
                LogErrorBlock(data.errors);
                Log("Import aborted while parsing CSV. No assets were modified.");
                return;
            }

            // --- Validate data. ---
            Validate(data);

            if (data.HasErrors)
            {
                LogErrorBlock(data.errors);
                Log($"Import aborted while validating with {data.errors.Count} error(s). No assets were modified.");
                return;
            }

            Log($"Validation passed: {data.rows.Count} node(s).");

            // --- Folder scaffolding. ---
            EnsureFolders(buildingName);

            // --- Panorama import. ---
            if (!ImportPanoramas(data, buildingName))
            {
                Log("Import aborted while importing panoramas. Partial asset state may remain.");
                return;
            }

            // --- NodeData asset creation. ---
            if (!CreateNodeAssets(data, buildingName, out List<NodeData> nodes))
            {
                Log("Import aborted while creating NodeData assets. Partial asset state may remain.");
                return;
            }

            // --- TourData asset creation. ---
            if (!PopulateTourData(data, buildingName, nodes))
            {
                Log("Import aborted while creating TourData asset. Partial asset state may remain.");
                return;
            }

            // --- Save and report. ---
            FinalizeImport();
        }
        catch (System.Exception ex)
        {
            LogError($"Unhandled exception during import: {ex.GetType().Name}: {ex.Message}");
            LogError("Partial asset state may remain. See the Console for the full stack trace.");
            Debug.LogException(ex);
        }
        finally
        { 
            Repaint();
        }
    }

    #endregion

    #region Parse

    private bool ParseCsv(string path, TourImporterData data)
    {
        Log("Parsing CSV...");

        string[] lines;
        try
        {
            lines = File.ReadAllLines(path);

        }
        catch (System.Exception ex)
        {
            data.AddError($"Failed to read CSV file: {ex.Message}");
            return false;
        }

        if (lines.Length == 0)
        {
            data.AddError("CSV file is empty.");
            return false;
        }

        // Header: strip a leading BOM character if present, then validate.
        string headerLine = lines[0].TrimStart('\uFEFF');
        List<string> headers = SplitCsvLine(headerLine);

        if (!HeadersMatch(headers, out string headerError))
        {
            data.AddError(headerError);
            return false;
        }

        // Data rows. Physical line numbers are 1-based; header is line .
        for (int i = 1; i < lines.Length; i++)
        {
            string raw = lines[i];
            int physicalLine = i + 1;

            if (string.IsNullOrWhiteSpace(raw))
                continue; // Skip blank lines.
            List<string> fields = SplitCsvLine(raw);

            if (fields.Count != ExpectedHeaders.Length)
            {
                data.AddError($"Line {physicalLine}: Expected {ExpectedHeaders.Length} fields but found {fields.Count}.");
                continue;
            }

            NodeImportRow row = new NodeImportRow
            {
                sourceLine = physicalLine,
                rawNodeId = fields[0].Trim(),
                rawDisplayName = fields[1].Trim(),
                rawFloor = fields[2].Trim(),
                rawPositionX = fields[3].Trim(),
                rawPositionY = fields[4].Trim(),
                rawPositionZ = fields[5].Trim(),
                rawPanoramaRotation = fields[6].Trim(),
                rawPanoramaFilename = fields[7].Trim(),
                rawIsStartNode = fields[8].Trim(),
            };

            data.rows.Add(row);
        }

        Log($" Parsed {data.rows.Count} data row(s).");
        return true;
    }

    private bool HeadersMatch(List<string> headers, out string error)
    {
        if (headers.Count != ExpectedHeaders.Length)
        {
            error = $"CSV header has {headers.Count} columns but expected {ExpectedHeaders.Length}: " +
                    $"[{string.Join(", ", ExpectedHeaders)}].";
            return false;
        }

        for (int i = 0; i < ExpectedHeaders.Length; i++)
        {
            string actual = headers[i].Trim();
            if (!string.Equals(actual, ExpectedHeaders[i], System.StringComparison.OrdinalIgnoreCase))
            {
                error = $"CSV header column {i + 1} is '{actual}' but expected '{ExpectedHeaders[i]}'.";
                return false;
            }
        }

        error = null;
        return true;
    }

    /// <summary>
    /// Splits a single CSV line into fields. Handles Excel-style double-quoted
    /// fields and escaped quotes inside a quoted field. Does NOT handle quoted
    /// fields that span multiple physical lines.
    /// </summary>
    private static List<string> SplitCsvLine(string line)
    {
        List<string> fields = new List<string>();
        StringBuilder current = new StringBuilder();
        bool inQuotes = false;

        for (int i = 0; i < line.Length; i++)
        {
            char c = line[i];

            if (inQuotes)
            {
                if (c == '"')
                {
                    if (i + 1 < line.Length && line[i + 1] == '"')
                    {
                        current.Append('"');
                        i++;
                    }
                    else
                    {
                        inQuotes = false;
                    }
                }
                else
                {
                    current.Append(c);
                }
            }
            else
            {
                if (c == '"')
                {
                    inQuotes = true;
                }
                else if (c == ',')
                {
                    fields.Add(current.ToString());
                    current.Clear();

                }
                else
                {
                    current.Append(c);
                }
            }
        }

        fields.Add(current.ToString());
        return fields;
    }

    #endregion

    #region Validation

    /// <summary>
    /// Types every raw row into its NodeImportRow typed fields and collects every error before returning.
    /// Checks:
    /// required fields (node_id, panorama_filename),
    /// unpparseable int/float fields,
    /// duplicate node_id values.
    /// Resolves the start node (exactly one TRUE; none -> first row; multiple -> Warning)
    /// into <see cref="TourImporterData.startNodeRowIndex"/>.
    /// </summary>
    private void Validate(TourImporterData data)
    {
        Log("Validating...");

        if (data.rows.Count == 0)
        {
            data.AddError("CSV contains no data rows.");
            return;
        }

        // Track node_id occurences for duplicate detection.
        Dictionary<string, int> idFirstSeenLine = new Dictionary<string, int>();
        List<int> startNodeRowIndices = new List<int>();

        for (int idx = 0; idx < data.rows.Count; idx++)
        {
            NodeImportRow row = data.rows[idx];

            // --- Required: node id ---
            if (string.IsNullOrEmpty(row.rawNodeId))
                data.AddError($"Line {row.sourceLine}: node_id is required but empty.");
            else
            {
                row.nodeId = row.rawNodeId;

                if (idFirstSeenLine.TryGetValue(row.nodeId, out int firstLine))
                    data.AddError($"Line {row.sourceLine}: duplicate node_id '{row.nodeId}' (first seen on line {firstLine})");
                else
                    idFirstSeenLine[row.nodeId] = row.sourceLine;
            }

            // --- Required: panorama filename ---
            if (string.IsNullOrEmpty(row.rawPanoramaFilename))
                data.AddError($"Line {row.sourceLine}: panorama_filename is required but empty.");
            else
                row.panoramaFilename = row.rawPanoramaFilename;

            // --- Optional free text ---
            row.displayName = row.rawDisplayName;

            // ---Typed: floor (int) ---
            if (!int.TryParse(row.rawFloor, NumberStyles.Integer, CultureInfo.InvariantCulture, out int floor))
                data.AddError($"Line {row.sourceLine}: floor '{row.rawFloor}' is not a valid integer.");
            else
                row.floor = floor;

            // --- Typed: position (3x float) ---
            float px = 0f, py = 0f, pz = 0f;
            bool posOk = true;

            if (!TryParseFloat(row.rawPositionX, out px))
            {
                data.AddError($"Line {row.sourceLine}: position_x '{row.rawPositionX}' is not a valid float.");
                posOk = false;
            }
            if (!TryParseFloat(row.rawPositionY, out py))
            {
                data.AddError($"Line {row.sourceLine}: position_y '{row.rawPositionY}' is not a valid float.");
                posOk = false;
            }
            if (!TryParseFloat(row.rawPositionZ, out pz))
            {
                data.AddError($"Line {row.sourceLine}: position_z '{row.rawPositionZ}' is not a valid float.");
                posOk = false;
            }
            if (posOk)
                row.position = new Vector3(px, py, pz);

            // --- Typed: panorama rotation (float) ---
            if (!TryParseFloat(row.rawPanoramaRotation, out float rotation))
                data.AddError($"Line {row.sourceLine}: panorama_rotation '{row.rawPanoramaRotation}' is not a valid float.");
            else
                row.panoramaRotation = rotation;

            // --- Typed: is_start_node (bool, lenient) ---
            row.isStartNode = ParseStartNodeFlag(row.rawIsStartNode, row.sourceLine);
            if (row.isStartNode)
                startNodeRowIndices.Add(idx);
        }

        ResolveStartNode(data, startNodeRowIndices);
    }

    /// <summary>
    /// Applies the start-node resolution rule and logs the outcome.
    /// </summary>
    private void ResolveStartNode(TourImporterData data, List<int> startNodeRowIndices)
    {
        if (startNodeRowIndices.Count == 0)
        {
            data.startNodeRowIndex = 0;
            Log($" No is_start_node=TRUE row foundl defaultin to first row ('{data.rows[0].rawNodeId}').");
        }
        else if (startNodeRowIndices.Count == 1)
        {
            data.startNodeRowIndex = startNodeRowIndices[0];
        }
        else
        {
            data.startNodeRowIndex = startNodeRowIndices[0];
            string chosen = data.rows[startNodeRowIndices[0]].rawNodeId;
            LogWarning($" {startNodeRowIndices.Count} rows have is_start_node=TRUE; defaulting to first: '{chosen}'.");
        }
    }

    private bool ParseStartNodeFlag(string raw, int sourceLine)
    {
        if (string.Equals(raw, "TRUE", System.StringComparison.OrdinalIgnoreCase))
            return true;
        if (string.Equals(raw, "FALSE", System.StringComparison.OrdinalIgnoreCase))
            return false;

        LogWarning($" Line {sourceLine}: is_start_node value '{raw}' is not a valid boolean; treating as FALSE.");
        return false;
    }

    private static bool TryParseFloat(string s, out float value)
    {
        return float.TryParse(
            s,
            NumberStyles.Float | NumberStyles.AllowThousands,
            CultureInfo.InvariantCulture,
            out value);
    }

    #endregion

    #region Folder Scaffolding

    private void EnsureFolders(string buildingName)
    {
        string buildingRoot = $"{ToursRoot}/{buildingName}";
        string nodesFolder = $"{buildingRoot}/Nodes";
        string panoramasFolder = $"{buildingRoot}/Panoramas";

        if (!AssetDatabase.IsValidFolder(ToursRoot))
            AssetDatabase.CreateFolder("Assets", "Tours");

        if (!AssetDatabase.IsValidFolder(buildingRoot))
            AssetDatabase.CreateFolder(ToursRoot, buildingName);

        if (!AssetDatabase.IsValidFolder(nodesFolder))
            AssetDatabase.CreateFolder(buildingRoot, "Nodes");

        if (!AssetDatabase.IsValidFolder(panoramasFolder))
            AssetDatabase.CreateFolder(buildingRoot, "Panoramas");
    }

    #endregion

    #region Panorama Import

    /// <summary>
    /// For each row: locates the source panorama, copies it into the building's Panoramas/ folder
    /// with cube settings, and asserts its addressable address and group.
    /// 
    /// Import settings are re-applied on every run even when the file copy is skipped due to an existing file with the same name
    /// so re-import repairs wrong settings or dropped Addressable entries.
    /// </summary>
    private bool ImportPanoramas(TourImporterData data, string buildingName)
    {
        Log("Importing panoramas...");

        string panoramasFolder = $"{ToursRoot}/{buildingName}/Panoramas";
        string projectRoot = Directory.GetParent(Application.dataPath).FullName;

        AddressableAssetSettings settings = AddressableAssetSettingsDefaultObject.Settings;
        if (settings == null)
        {
            LogError(" Addressable settings not found. Open Window > Asset Management > Addressables > Groups once to initialize them.");
            return false;
        }

        AddressableAssetGroup group = settings.FindGroup("Panoramas") ?? settings.DefaultGroup;
        if (group == null)
        {
            LogError("No 'Panoramas' Addressable groups and no default groups is set");
            return false;
        }

        foreach (NodeImportRow row in data.rows)
        {
            string sourcePath = Path.Combine(_sourceFolder, row.panoramaFilename);
            if (!File.Exists(sourcePath))
            {
                LogError($" Line {row.sourceLine}: source panorama ' {row.panoramaFilename}' not found at {sourcePath}.");
                return false;
            }

            string ext = Path.GetExtension(row.panoramaFilename);
            string destAssetPath = $"{panoramasFolder}/{row.nodeId}_pano{ext}";
            string destAbsolute = Path.Combine(projectRoot, destAssetPath);

            // Copy only if absent (decision); settings are enforced regardless below.
            if (!File.Exists(destAbsolute))
            {
                try
                {
                    File.Copy(sourcePath, destAbsolute);
                }
                catch (System.Exception ex)
                {
                    LogError($" Line {row.sourceLine}: failed to copy panorama from '{sourcePath}' to '{destAbsolute}': {ex.Message}");
                    return false;
                }
            }

            // Register/refresh the asset so an importer is available.
            AssetDatabase.ImportAsset(destAssetPath, ImportAssetOptions.ForceUpdate);

            TextureImporter importer = AssetImporter.GetAtPath(destAssetPath) as TextureImporter;
            if (importer == null)
            {
                LogError($" Line {row.sourceLine}: no TextureImporter at {destAssetPath} (not recognized as a texture?).");
                return false;
            }

            ApplyPanoramaImportSettings(importer);
            importer.SaveAndReimport();

            string guid = AssetDatabase.AssetPathToGUID(destAssetPath);
            if (string.IsNullOrEmpty(guid))
            {
                LogError($" Line {row.sourceLine}: failed to get GUID for imported panorama at '{destAssetPath}'.");
                return false;
            }

            string address = $"{row.nodeId}_pano";
            AddressableAssetEntry entry = settings.CreateOrMoveEntry(guid, group);
            entry.SetAddress(address);

            row.importedPanoramaGuid = guid;
            Log($" {row.nodeId}: imported '{row.panoramaFilename}' -> {destAssetPath} (address '{address}', group '{group.Name}').");
        }

        EditorUtility.SetDirty(settings);
        Log($" Imported {data.rows.Count} panorama(s).");
        return true;
    }

    /// <summary>
    /// Import settings for equirectangular colour photographs used as skybox cubemaps.
    /// </summary>
    private void ApplyPanoramaImportSettings(TextureImporter importer)
    {
        importer.textureType = TextureImporterType.Default;
        importer.textureShape = TextureImporterShape.TextureCube;
        importer.sRGBTexture = true;
        
        // Equirectangular (2:1) source -> lat-long cubemap, no lighting convolution.
        TextureImporterSettings s = new TextureImporterSettings();
        importer.ReadTextureSettings(s);
        s.generateCubemap = TextureImporterGenerateCubemap.Cylindrical;
        s.cubemapConvolution = TextureImporterCubemapConvolution.None;
        importer.SetTextureSettings(s);

        // Skybox is viewed from the center, clamp avoids seams.
        importer.wrapMode = TextureWrapMode.Clamp;
        importer.filterMode = FilterMode.Bilinear;
        importer.mipmapEnabled = false;
        importer.alphaSource = TextureImporterAlphaSource.None;
    }

    #endregion

    #region NodeData Creation

    /// <summary>
    /// Creates or updates a NodeData asset per row at
    /// Asset/Tours/[BuildingName]/Nodes/[node_id].asset. On an existing asset,
    /// overwrites id/displayName.floor/position/panoramaRotation/panoramaTexture but
    /// preserves pointsOfInterest, so re-importing does not destroy manual POI wiring.
    /// </summary>
    private bool CreateNodeAssets(TourImporterData data, string buildingName,
         out List<NodeData> nodes)
    {
        Log("Creating NodeData assets...");
        nodes = new List<NodeData>(data.rows.Count);

        string nodesFolder = $"{ToursRoot}/{buildingName}/Nodes";

        foreach (NodeImportRow row in data.rows)
        {
            string guid = row.importedPanoramaGuid;
            if (string.IsNullOrEmpty(guid))
            {
                LogError($" {row.nodeId}: no imported panorama GUID.");
                return false;
            }

            string nodePath = $"{nodesFolder}/{row.nodeId}.asset";
            NodeData node = AssetDatabase.LoadAssetAtPath<NodeData>(nodePath);
            bool isNew = node == null;

            if (isNew)
                node = ScriptableObject.CreateInstance<NodeData>();

            node.id = row.nodeId;
            node.displayName = row.displayName;
            node.floor = row.floor;
            node.position = row.position;
            node.panoramaRotation = row.panoramaRotation;
            node.panoramaTexture = new AssetReferenceCubemap(guid);

            if (isNew)
                AssetDatabase.CreateAsset(node, nodePath);

            EditorUtility.SetDirty(node);
            nodes.Add(node);

            Log($" {row.nodeId}: {(isNew ? "created" : "updated")} {nodePath}.");
        }

        // Persist node assets
        AssetDatabase.SaveAssets();
        Log($" Wrote {nodes.Count} NodeData assets(s).");
        return true;
    }

    #endregion

    #region TourData Population

    private bool PopulateTourData(TourImporterData data, string buildingName, List<NodeData> nodes)
    {
        Log("Populating TourData...");

        string tourAssetPath = $"{ToursRoot}/{buildingName}/{buildingName}Tour.asset";
        TourData tour = AssetDatabase.LoadAssetAtPath<TourData>(tourAssetPath);
        bool isNew = tour == null;

        if (isNew)
        {
            tour = ScriptableObject.CreateInstance<TourData>();
            tour.buildingName = buildingName;
            AssetDatabase.CreateAsset(tour, tourAssetPath);
        }

        tour.nodes = new List<NodeData>(nodes);
        tour.defaultStartNodeId = data.rows[data.startNodeRowIndex].nodeId;
        tour.floors = nodes.Select(n => n.floor).Distinct().OrderBy(f => f).ToList();

        EditorUtility.SetDirty(tour);

        Log($" {(isNew ? "Created" : "Updated")} {tourAssetPath}: {tour.nodes.Count} node(s), " +
            $"start '{tour.defaultStartNodeId}', floors [{string.Join(", ", tour.floors)}].");
        return true;
    }

    #endregion

    #region Finalize

    private void FinalizeImport()
    {
        Log("Saving...");
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Log("Import complete");
    }

    #endregion

    #region Logging

    private void Log(string message)
    {
        _log.AppendLine(message);
    }

    private void LogWarning(string message)
    {
        _log.AppendLine($"[WARN] {message}");
    }

    private void LogError(string message)
    {
        _log.AppendLine($"[ERROR] {message}");
    }

    private void LogErrorBlock(List<string> errors)
    {
        for (int i = 0; i < errors.Count; i++)
        {
            _log.AppendLine($"[ERROR] {errors[i]}");
        }
    }
    #endregion
}
