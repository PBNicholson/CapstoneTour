using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Editor tool for BuildingLayout assets: imports layout JSON, validates layouts, draws the layout
/// (and optionally a tour's nodes) in the Scene view, and generates inward-facing TourGeometry colliders
/// into the active scene. Generated meshes are embedded in the scene (not saved as assets).
/// Also runs the navigation radius report for a layout against a TourData.
/// </summary>
public class TourGeometryWindow : EditorWindow
{
    #region Constants

    private const string LayoutDataFolderName = "LayoutData";
    private const string TourGeometryLayerName = "TourGeometry";
    private const float ProbeOffset = 0.137f;

    #endregion

    #region Window State

    [SerializeField] private BuildingLayout _layout;
    [SerializeField] private string _jsonPath = string.Empty;

    [SerializeField] private bool _showLayout = true;
    [SerializeField] private TourData _tour;
    [SerializeField] private bool _showNodes = true;
    [SerializeField] private bool _showNodeLabels;
    [SerializeField] private bool _showNodeRadius;

    // Last layout hash seen by OnInspectorUpdate; used to repaint the Scene view when the layout is edited.
    private string _lastLayoutHash;

    // Status of generated geometry for the current layout in the active scene; refreshed by OnInspectorUpdate.
    private string _generatedStatus = string.Empty;

    [SerializeField] private float _reportGridSpacing = 1f;
    [SerializeField] private bool _showReportMarkers = true;

    // Last radius report (not serialized; cleared by recompiles).
    private RadiusReport _report;

    private readonly StringBuilder _log = new StringBuilder();
    private Vector2 _logScroll;

    #endregion

    #region Window Lifecycle

    [MenuItem("Tools/Tour Geometry Generator")]
    private static void ShowWindow()
    {
        TourGeometryWindow window = GetWindow<TourGeometryWindow>();
        window.titleContent = new GUIContent("Tour Geometry");
        window.minSize = new Vector2(520f, 420f);
    }

    private void OnEnable()
    {
        SceneView.duringSceneGui += OnSceneGUI;
        UpdateGizmoCoordination();
    }

    private void OnDisable()
    {
        SceneView.duringSceneGui -= OnSceneGUI;
        TourGeometryGizmos.WindowLayout = null;
        SceneView.RepaintAll();
    }

    /// <summary>
    /// Repaints the Scene view when the layout asset changes (inspector edits, undo, import).
    /// Called by Unity about 10 times per second while the window is open.
    /// </summary>
    private void OnInspectorUpdate()
    {
        string status = ComputeGeneratedStatus();
        if (status != _generatedStatus)
        {
            _generatedStatus = status;
            Repaint();
        }

        if (!_showLayout || _layout == null)
            return;

        string hash = _layout.ComputeContentHash();
        if (hash != _lastLayoutHash)
        {
            _lastLayoutHash = hash;
            SceneView.RepaintAll();
        }
    }

    private void OnGUI()
    {
        // --- Layout. ---
        EditorGUILayout.LabelField("Layout", EditorStyles.boldLabel);

        EditorGUI.BeginChangeCheck();
        _layout = (BuildingLayout)EditorGUILayout.ObjectField("Building Layout", _layout, typeof(BuildingLayout), false);
        if (EditorGUI.EndChangeCheck())
        {
            UpdateGizmoCoordination();
            SceneView.RepaintAll();
        }

        EditorGUILayout.Space();

        // --- Import. ---
        EditorGUILayout.LabelField("Import", EditorStyles.boldLabel);
        DrawJsonPathField();

        string importLabel = _layout != null ? "Import JSON into Layout" : "Import JSON (create new Layout asset)";
        using (new EditorGUI.DisabledScope(string.IsNullOrWhiteSpace(_jsonPath)))
        {
            if (GUILayout.Button(importLabel, GUILayout.Height(26f)))
            {
                RunImport();
                // RunImport may open a save dialog; exit this GUI pass to avoid layout-group mismatch errors.
                GUIUtility.ExitGUI();
            }
        }

        EditorGUILayout.Space();

        // --- Validate. ---
        EditorGUILayout.LabelField("Validate", EditorStyles.boldLabel);
        using (new EditorGUI.DisabledScope(_layout == null))
        {
            if (GUILayout.Button("Validate Layout", GUILayout.Height(26f)))
                RunValidate();
        }

        EditorGUILayout.Space();

        // --- Generate. ---
        EditorGUILayout.LabelField("Generate", EditorStyles.boldLabel);
        EditorGUILayout.LabelField("Status", string.IsNullOrEmpty(_generatedStatus) ? "-" : _generatedStatus, EditorStyles.wordWrappedLabel);

        bool inPlayMode = EditorApplication.isPlayingOrWillChangePlaymode;
        using (new EditorGUI.DisabledScope(_layout == null || inPlayMode))
        {
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Generate in Active Scene", GUILayout.Height(26f)))
                RunGenerate();
            if (GUILayout.Button("Remove Generated", GUILayout.Height(26f), GUILayout.Width(150f)))
                RunRemove();
            EditorGUILayout.EndHorizontal();
        }

        if (inPlayMode)
            EditorGUILayout.HelpBox("Exit Play Mode to generate or remove geometry.", MessageType.Info);

        EditorGUILayout.Space();

        // --- Scene view. ---
        // (Change checks are kept out of the Import section: its ExitGUI call would skip EndChangeCheck.)
        EditorGUILayout.LabelField("Scene View", EditorStyles.boldLabel);
        EditorGUI.BeginChangeCheck();
        _showLayout = EditorGUILayout.Toggle("Show Layout", _showLayout);
        _tour = (TourData)EditorGUILayout.ObjectField(
            new GUIContent("Tour", "Draws this tour's nodes in the Scene view. Required for the radius report."),
            _tour, typeof(TourData), false);

        using (new EditorGUI.DisabledScope(_tour == null))
        {
            EditorGUI.indentLevel++;
            _showNodes = EditorGUILayout.Toggle("Show Nodes", _showNodes);
            using (new EditorGUI.DisabledScope(!_showNodes))
            {
                _showNodeLabels = EditorGUILayout.Toggle("Node Labels", _showNodeLabels);
                _showNodeRadius = EditorGUILayout.Toggle(
                    new GUIContent("Navigation Radius", "Circle of TourData.navigationRadius around each node."),
                    _showNodeRadius);
            }
            EditorGUI.indentLevel--;
        }

        using (new EditorGUI.DisabledScope(_layout == null))
        {
            if (GUILayout.Button("Frame Layout in Scene View"))
                FrameLayout();
        }

        if (EditorGUI.EndChangeCheck())
        {
            UpdateGizmoCoordination();
            SceneView.RepaintAll();
        }

        EditorGUILayout.Space();

        // --- Radius report. ---
        EditorGUILayout.LabelField("Navigation Radius Report", EditorStyles.boldLabel);
        _reportGridSpacing = Mathf.Max(0.1f, EditorGUILayout.FloatField(
            new GUIContent("Grid Spacing (ft)", "Floor sample spacing. Room edges and corners are always sampled."),
            _reportGridSpacing));

        using (new EditorGUI.DisabledScope(_layout == null || _tour == null))
        {
            if (GUILayout.Button("Run Report (uses the Tour field above)", GUILayout.Height(26f)))
                RunReport();
        }

        EditorGUI.BeginChangeCheck();
        EditorGUILayout.BeginHorizontal();
        _showReportMarkers = EditorGUILayout.Toggle("Show Worst Points", _showReportMarkers);
        using (new EditorGUI.DisabledScope(_report == null))
        {
            if (GUILayout.Button("Clear Report", GUILayout.Width(100f)))
                _report = null;
        }
        EditorGUILayout.EndHorizontal();
        if (EditorGUI.EndChangeCheck())
            SceneView.RepaintAll();

        EditorGUILayout.Space();

        // --- Log. ---
        EditorGUILayout.BeginHorizontal();
        EditorGUILayout.LabelField("Log", EditorStyles.boldLabel);
        if (GUILayout.Button("Clear", GUILayout.Width(60f)))
            _log.Clear();
        EditorGUILayout.EndHorizontal();

        _logScroll = EditorGUILayout.BeginScrollView(_logScroll, GUILayout.ExpandHeight(true));
        EditorGUILayout.SelectableLabel(
            _log.Length > 0 ? _log.ToString() : "(no output yet)",
            EditorStyles.textArea,
            GUILayout.ExpandHeight(true));
        EditorGUILayout.EndScrollView();
    }

    private void DrawJsonPathField()
    {
        EditorGUILayout.BeginHorizontal();
        _jsonPath = EditorGUILayout.TextField("Layout JSON", _jsonPath);

        if (GUILayout.Button("Browse", GUILayout.Width(70f)))
        {
            string startFolder = !string.IsNullOrWhiteSpace(_jsonPath) && File.Exists(_jsonPath)
                ? Path.GetDirectoryName(_jsonPath)
                : DefaultLayoutDataFolder();

            string picked = EditorUtility.OpenFilePanel("Select Layout JSON", startFolder, "json");
            if (!string.IsNullOrEmpty(picked))
            {
                _jsonPath = picked;
                GUI.FocusControl(null); // Refresh the text field from the new value.
            }
        }
        EditorGUILayout.EndHorizontal();
    }

    /// <summary>[ProjectRoot]/LayoutData if it exists, otherwise the project root.</summary>
    private static string DefaultLayoutDataFolder()
    {
        string projectRoot = Directory.GetParent(Application.dataPath).FullName;
        string folder = Path.Combine(projectRoot, LayoutDataFolderName);
        return Directory.Exists(folder) ? folder : projectRoot;
    }

    #endregion

    #region Scene View

    private void OnSceneGUI(SceneView sceneView)
    {
        if (_showLayout && _layout != null)
            TourGeometryGizmos.DrawLayout(_layout);

        if (_showNodes && _tour != null)
            TourGeometryGizmos.DrawNodes(_tour, _showNodeLabels, _showNodeRadius);

        if (_showReportMarkers && _report != null)
            NavigationRadiusReport.DrawMarkers(_report);
    }

    /// <summary>Tells the [DrawGizmo] path which layout this window is already drawing.</summary>
    private void UpdateGizmoCoordination()
    {
        TourGeometryGizmos.WindowLayout = _showLayout ? _layout : null;
    }

    /// <summary>Frames the bounds of all rooms (floor to ceiling) in the last active Scene view.</summary>
    private void FrameLayout()
    {
        SceneView sceneView = SceneView.lastActiveSceneView;
        if (sceneView == null || _layout.rooms == null || _layout.rooms.Count == 0)
            return;

        bool hasBounds = false;
        Bounds bounds = new Bounds();

        foreach (LayoutRoom room in _layout.rooms)
        {
            if (room == null)
                continue;

            Vector3 min = new Vector3(room.xMin, _layout.GetFloorY(room), room.zMin);
            Vector3 max = new Vector3(room.xMax, _layout.GetCeilingY(room), room.zMax);
            Bounds roomBounds = new Bounds((min + max) * 0.5f, max - min);

            if (hasBounds)
                bounds.Encapsulate(roomBounds);
            else
                bounds = roomBounds;
            hasBounds = true;
        }

        if (hasBounds)
            sceneView.Frame(bounds, false);
    }

    #endregion

    #region Import

    /// <summary>
    /// Parses and validates the JSON into a temporary layout. Only if there are no errors is the target
    /// asset created or overwritten (lists are replaced, not merged). Overwriting an existing asset is undoable.
    /// </summary>
    private void RunImport()
    {
        _log.Clear();
        BuildingLayout temp = null;

        try
        {
            if (!File.Exists(_jsonPath))
            {
                LogError($"JSON file does not exist: {_jsonPath}");
                return;
            }

            Log($"Importing {_jsonPath}");

            string json = File.ReadAllText(_jsonPath);
            List<string> parseErrors = new List<string>();
            temp = BuildingLayoutJson.ParseToTemporaryLayout(json, parseErrors);

            if (temp == null)
            {
                LogErrorBlock(parseErrors);
                Log("Import aborted while parsing JSON. No assets were modified.");
                return;
            }

            LayoutValidationResult result = BuildingLayoutValidator.Validate(temp);
            LogValidation(temp, result);

            if (result.HasErrors)
            {
                Log("Import aborted: validation failed. No assets were modified.");
                return;
            }

            BuildingLayout target = _layout;
            bool isNew = target == null;

            if (isNew)
            {
                string defaultName = Path.GetFileNameWithoutExtension(_jsonPath);
                string assetPath = EditorUtility.SaveFilePanelInProject(
                    "Create Building Layout", defaultName, "asset",
                    "Choose where to save the new BuildingLayout asset.", "Assets/Tours");

                if (string.IsNullOrEmpty(assetPath))
                {
                    Log("Import cancelled. No assets were modified.");
                    return;
                }

                target = CreateInstance<BuildingLayout>();
                BuildingLayoutJson.CopyLayout(temp, target);
                AssetDatabase.CreateAsset(target, assetPath);
                _layout = target;
            }
            else
            {
                Undo.RecordObject(target, "Import Building Layout JSON");
                BuildingLayoutJson.CopyLayout(temp, target);
                EditorUtility.SetDirty(target);
            }

            AssetDatabase.SaveAssetIfDirty(target);
            UpdateGizmoCoordination();
            SceneView.RepaintAll();
            Log($"{(isNew ? "Created" : "Updated")} {AssetDatabase.GetAssetPath(target)} " +
                $"({target.rooms.Count} room(s), {target.doorways.Count} doorway(s)).");
        }
        catch (System.Exception ex)
        {
            LogError($"Unhandled exception during import: {ex.GetType().Name}: {ex.Message}");
            LogError("See the Console for the full stack trace.");
            Debug.LogException(ex);
        }
        finally
        {
            if (temp != null)
                DestroyImmediate(temp);
            Repaint();
        }
    }

    #endregion

    #region Validate

    private void RunValidate()
    {
        _log.Clear();
        Log($"Validating {AssetDatabase.GetAssetPath(_layout)}");

        LayoutValidationResult result = BuildingLayoutValidator.Validate(_layout);
        LogValidation(_layout, result);

        if (Physics.queriesHitBackfaces)
        {
            LogWarning("Physics.queriesHitBackfaces is ON. Generated geometry relies on it being off " +
                       "(Project Settings > Physics > Queries Hit Backfaces).");
        }

        Repaint();
    }

    #endregion

    #region Generate

    /// <summary>
    /// Validates and builds all meshes first; only then removes this layout's previous roots from the active scene
    /// and creates the new hierarchy. A validation failure leaves the scene untouched. Not undoable: regenerate instead.
    /// </summary>
    private void RunGenerate()
    {
        _log.Clear();
        TourGeometryBuildResult build = null;
        bool attached = false;

        try
        {
            if (PrefabStageUtility.GetCurrentPrefabStage() != null)
            {
                LogError("Exit Prefab Mode before generating.");
                return;
            }

            Scene scene = SceneManager.GetActiveScene();
            if (!scene.IsValid() || !scene.isLoaded)
            {
                LogError("No valid, loaded active scene.");
                return;
            }

            int layer = LayerMask.NameToLayer(TourGeometryLayerName);
            if (layer < 0)
            {
                LogError($"Layer '{TourGeometryLayerName}' does not exist. Create it in Project Settings > Tags and Layers.");
                return;
            }

            Log($"Generating from {AssetDatabase.GetAssetPath(_layout)} into scene '{scene.name}'.");

            build = TourGeometryBuilder.Build(_layout, out LayoutValidationResult validation);
            LogValidation(_layout, validation);

            if (build == null)
            {
                Log("Generation aborted: validation failed. The scene was not modified.");
                return;
            }

            if (Physics.queriesHitBackfaces)
            {
                LogWarning("Physics.queriesHitBackfaces is ON. Generated geometry relies on it being off " +
                           "(Project Settings > Physics > Queries Hit Backfaces).");
            }

            // --- Replace previous output. ---
            int removedRoots = RemoveGeneratedRoots(scene, _layout, out int destroyedMeshes);
            if (removedRoots > 0)
                Log($"Removed {removedRoots} previous root(s) for this layout and destroyed {destroyedMeshes} mesh(es).");

            // --- Create. ---
            Dictionary<GeneratedMeshInfo, Collider> colliders = new Dictionary<GeneratedMeshInfo, Collider>();
            GameObject root = CreateHierarchy(build, layer, colliders);
            attached = true;

            Physics.SyncTransforms();

            LogGeneration(build, root, layer);
            RunProbes(root, build, colliders, layer);
            WarnForeignColliders(scene, root, layer);
            LogLeakCheck(build);

            EditorSceneManager.MarkSceneDirty(scene);
            Log("Done. Save the scene to keep the generated geometry.");
        }
        catch (Exception ex)
        {
            LogError($"Unhandled exception during generation: {ex.GetType().Name}: {ex.Message}");
            LogError("See the Console for the full stack trace.");
            Debug.LogException(ex);
        }
        finally
        {
            // Meshes that were built but never attached to the scene would otherwise leak.
            if (!attached && build != null)
            {
                foreach (GeneratedMeshInfo info in build.All)
                {
                    if (info.mesh != null)
                        DestroyImmediate(info.mesh);
                }
            }

            _generatedStatus = ComputeGeneratedStatus();
            SceneView.RepaintAll();
            Repaint();
        }
    }

    private void RunRemove()
    {
        _log.Clear();

        Scene scene = SceneManager.GetActiveScene();
        int removedRoots = RemoveGeneratedRoots(scene, _layout, out int destroyedMeshes);

        if (removedRoots > 0)
        {
            EditorSceneManager.MarkSceneDirty(scene);
            Log($"Removed {removedRoots} root(s) for '{_layout.name}' from scene '{scene.name}' and destroyed {destroyedMeshes} mesh(es).");
        }
        else
        {
            Log($"No generated roots for '{_layout.name}' in scene '{scene.name}'.");
        }

        LogLeakCountOnly();

        _generatedStatus = ComputeGeneratedStatus();
        SceneView.RepaintAll();
        Repaint();
    }

    /// <summary>Every GeneratedTourGeometry in <paramref name="scene"/> whose source is <paramref name="layout"/>, inactive included.</summary>
    private static List<GeneratedTourGeometry> FindGeneratedRoots(Scene scene, BuildingLayout layout)
    {
        List<GeneratedTourGeometry> found = new List<GeneratedTourGeometry>();
        if (!scene.IsValid() || !scene.isLoaded || layout == null)
            return found;

        foreach (GameObject go in scene.GetRootGameObjects())
        {
            foreach (GeneratedTourGeometry marker in go.GetComponentsInChildren<GeneratedTourGeometry>(true))
            {
                if (marker.SourceLayout == layout)
                    found.Add(marker);
            }
        }

        return found;
    }

    /// <summary>
    /// Destroys every generated root for <paramref name="layout"/> in <paramref name="scene"/>, and the non-asset meshes
    /// referenced by their MeshColliders. Meshes that are assets are never destroyed.
    /// </summary>
    private static int RemoveGeneratedRoots(Scene scene, BuildingLayout layout, out int destroyedMeshes)
    {
        destroyedMeshes = 0;
        List<GeneratedTourGeometry> roots = FindGeneratedRoots(scene, layout);

        foreach (GeneratedTourGeometry marker in roots)
        {
            if (marker == null)
                continue; // Already destroyed as a child of an earlier root.

            foreach (MeshCollider mc in marker.GetComponentsInChildren<MeshCollider>(true))
            {
                Mesh mesh = mc.sharedMesh;
                if (mesh != null && !EditorUtility.IsPersistent(mesh))
                {
                    mc.sharedMesh = null;
                    DestroyImmediate(mesh);
                    destroyedMeshes++;
                }
            }

            DestroyImmediate(marker.gameObject);
        }

        return roots.Count;
    }

    private GameObject CreateHierarchy(TourGeometryBuildResult build, int layer, Dictionary<GeneratedMeshInfo, Collider> colliders)
    {
        // New GameObjects are created in the active scene.
        GameObject root = new GameObject(TourGeometryBuilder.RootName(_layout)) { layer = layer };

        GeneratedTourGeometry marker = root.AddComponent<GeneratedTourGeometry>();
        marker.SetGenerationInfo(_layout, _layout.ComputeContentHash(),
                                 DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture));

        Transform roomsGroup = CreateChild(root.transform, "Rooms", layer).transform;
        foreach (GeneratedMeshInfo info in build.rooms)
            colliders[info] = CreateColliderObject(roomsGroup, info, layer);

        if (build.passages.Count > 0)
        {
            Transform doorwaysGroup = CreateChild(root.transform, "Doorways", layer).transform;
            foreach (GeneratedMeshInfo info in build.passages)
                colliders[info] = CreateColliderObject(doorwaysGroup, info, layer);
        }

        return root;
    }

    private static GameObject CreateChild(Transform parent, string name, int layer)
    {
        GameObject go = new GameObject(name) { layer = layer };
        go.transform.SetParent(parent, false);
        return go;
    }

    private static Collider CreateColliderObject(Transform parent, GeneratedMeshInfo info, int layer)
    {
        GameObject go = CreateChild(parent, info.objectName, layer);
        MeshCollider mc = go.AddComponent<MeshCollider>();
        mc.sharedMesh = info.mesh;
        return mc;
    }

    #endregion

    #region Generation Checks

    private void LogGeneration(TourGeometryBuildResult build, GameObject root, int layer)
    {
        Log($"Created '{root.name}' on layer '{TourGeometryLayerName}' ({layer}): " +
            $"{build.rooms.Count} room collider(s), {build.passages.Count} passage collider(s).");

        foreach (GeneratedMeshInfo info in build.All)
        {
            Log($"  {info.objectName}: {info.quadCount} quad(s), {info.triangleCount} triangle(s), " +
                $"longest triangle edge {F(info.maxEdgeLength)} ft");
        }

        Log($"Total: {build.TotalTriangles} triangle(s); longest triangle edge {F(build.MaxEdgeLength)} ft " +
            "(PhysX warns above 500).");

        // Hierarchy self-check for gate G1.
        Transform[] all = root.GetComponentsInChildren<Transform>(true);
        int wrongLayer = 0;
        foreach (Transform t in all)
        {
            if (t.gameObject.layer != layer)
                wrongLayer++;
        }

        int renderers = root.GetComponentsInChildren<Renderer>(true).Length;
        int meshColliders = root.GetComponentsInChildren<MeshCollider>(true).Length;
        int otherColliders = root.GetComponentsInChildren<Collider>(true).Length - meshColliders;

        string check = $"Hierarchy check: {all.Length} object(s), {wrongLayer} not on {TourGeometryLayerName}, " +
                       $"{meshColliders} MeshCollider(s), {otherColliders} other collider(s), {renderers} renderer(s).";

        if (wrongLayer == 0 && renderers == 0 && otherColliders == 0)
            Log(check);
        else
            LogWarning(check);
    }

    /// <summary>
    /// Edit-mode raycasts from each room's center at mid-height: down and up must hit the room's own collider;
    /// horizontal rays must hit generated geometry facing the ray (another room is fine: the ray went through a doorway).
    /// </summary>
    private void RunProbes(GameObject root, TourGeometryBuildResult build, Dictionary<GeneratedMeshInfo, Collider> colliders, int layer)
    {
        Log("Probes (edit-mode raycasts from each room center at mid-height, offset 0.137 ft):");

        Vector3[] directions = { Vector3.down, Vector3.up, Vector3.right, Vector3.left, Vector3.forward, Vector3.back };
        string[] labels = { "down", "up", "+X", "-X", "+Z", "-Z" };
        int mask = 1 << layer;

        int totalHits = 0;
        int failures = 0;
        int misses = 0;

        foreach (GeneratedMeshInfo info in build.rooms)
        {
            LayoutRoom room = _layout.rooms[info.roomIndex];
            // Offset slightly from the exact center so a probe never runs exactly along a doorway edge or jamb plane
            // (layout coordinates are usually whole or half feet).
            Vector2 c = room.CenterXZ;
            Vector3 origin = new Vector3(c.x + ProbeOffset, (_layout.GetFloorY(room) + _layout.GetCeilingY(room)) * 0.5f, c.y + ProbeOffset);
            Collider own = colliders[info];

            List<string> notes = new List<string>();
            int ok = 0;

            for (int k = 0; k < directions.Length; k++)
            {
                Vector3 dir = directions[k];
                bool vertical = k < 2;

                if (!Physics.Raycast(origin, dir, out RaycastHit hit, Mathf.Infinity, mask, QueryTriggerInteraction.Ignore))
                {
                    misses++;
                    notes.Add($"{labels[k]}: no hit");
                    continue;
                }

                totalHits++;
                bool facesRay = Vector3.Dot(hit.normal, dir) < -0.99f;
                bool isOwn = hit.collider == own;
                bool isGenerated = hit.transform.IsChildOf(root.transform);

                if (!isGenerated)
                {
                    failures++;
                    notes.Add($"{labels[k]}: FAIL hit '{hit.collider.name}', which is not generated geometry");
                }
                else if (!facesRay)
                {
                    failures++;
                    notes.Add($"{labels[k]}: FAIL normal ({F(hit.normal.x)}, {F(hit.normal.y)}, {F(hit.normal.z)}) does not face the ray");
                }
                else if (vertical && !isOwn)
                {
                    failures++;
                    notes.Add($"{labels[k]}: FAIL hit '{hit.collider.name}' instead of this room");
                }
                else if (!isOwn)
                {
                    ok++;
                    notes.Add($"{labels[k]}: ok, through a doorway into '{hit.collider.name}'");
                }
                else
                {
                    ok++;
                }
            }

            string detail = notes.Count > 0 ? " (" + string.Join("; ", notes) + ")" : string.Empty;
            Log($"  {info.objectName}: {ok}/{directions.Length} ok{detail}");
        }

        if (totalHits == 0)
        {
            LogWarning("Edit-mode raycasts returned no hits at all. Edit-mode physics queries may be unavailable here; " +
                       "rely on the Play Mode checks instead of these probes.");
        }
        else if (failures > 0)
        {
            LogWarning($"Probe summary: {failures} failure(s), {misses} miss(es). See the lines above.");
        }
        else
        {
            Log($"Probe summary: 0 failures, {misses} miss(es).");
        }
    }

    /// <summary>Warns about active colliders on the TourGeometry layer that are not part of the new root (e.g. LobbyGeometry).</summary>
    private void WarnForeignColliders(Scene scene, GameObject root, int layer)
    {
        List<string> names = new List<string>();

        foreach (GameObject go in scene.GetRootGameObjects())
        {
            foreach (Collider c in go.GetComponentsInChildren<Collider>(true))
            {
                if (c.gameObject.layer != layer || !c.enabled || !c.gameObject.activeInHierarchy)
                    continue;
                if (c.transform.IsChildOf(root.transform))
                    continue;
                names.Add(c.gameObject.name);
            }
        }

        if (names.Count > 0)
        {
            LogWarning($"{names.Count} other active collider(s) on {TourGeometryLayerName} in this scene: {string.Join(", ", names)}. " +
                       "Navigation raycasts will also hit these.");
        }
    }

    /// <summary>Counts this layout's generated (non-asset) meshes in memory and compares with what was just built.</summary>
    private void LogLeakCheck(TourGeometryBuildResult build)
    {
        int expected = build.rooms.Count + build.passages.Count;
        int found = CountGeneratedMeshesInMemory();
        string message = $"Leak check: {found} mesh(es) named '{TourGeometryBuilder.MeshNamePrefix(_layout)}*' in memory " +
                         $"(all loaded scenes); expected {expected}.";

        if (found == expected)
            Log(message);
        else
            LogWarning(message);
    }

    private void LogLeakCountOnly()
    {
        Log($"Leak check: {CountGeneratedMeshesInMemory()} mesh(es) named '{TourGeometryBuilder.MeshNamePrefix(_layout)}*' " +
            "in memory (all loaded scenes).");
    }

    private int CountGeneratedMeshesInMemory()
    {
        string prefix = TourGeometryBuilder.MeshNamePrefix(_layout);
        int count = 0;

        foreach (Mesh mesh in Resources.FindObjectsOfTypeAll<Mesh>())
        {
            if (mesh != null && !EditorUtility.IsPersistent(mesh) && mesh.name.StartsWith(prefix, StringComparison.Ordinal))
                count++;
        }

        return count;
    }

    /// <summary>One-line status of this layout's generated geometry in the active scene.</summary>
    private string ComputeGeneratedStatus()
    {
        if (_layout == null)
            return string.Empty;

        Scene scene = SceneManager.GetActiveScene();
        List<GeneratedTourGeometry> roots = FindGeneratedRoots(scene, _layout);

        if (roots.Count == 0)
            return $"Not generated in '{scene.name}'.";

        int upToDate = 0;
        foreach (GeneratedTourGeometry root in roots)
        {
            if (root.IsUpToDate)
                upToDate++;
        }

        string state = upToDate == roots.Count
            ? "up to date"
            : "STALE: the layout has changed since generation";

        return roots.Count == 1
            ? $"Generated in '{scene.name}', {state}."
            : $"{roots.Count} roots in '{scene.name}' ({upToDate} up to date). Generate to replace them with one.";
    }

    #endregion

    #region Radius Report

    private void RunReport()
    {
        _log.Clear();
        _report = null;

        try
        {
            RadiusReport report = NavigationRadiusReport.Run(_layout, _tour, _reportGridSpacing, out string error);
            if (report == null)
            {
                LogError(error);
                return;
            }

            _report = report;
            foreach (string line in NavigationRadiusReport.Format(report))
                Log(line);
        }
        catch (Exception ex)
        {
            LogError($"Unhandled exception during report: {ex.GetType().Name}: {ex.Message}");
            Debug.LogException(ex);
        }
        finally
        {
            SceneView.RepaintAll();
            Repaint();
        }
    }

    #endregion

    #region Validation Output

    private void LogValidation(BuildingLayout layout, LayoutValidationResult result)
    {
        int roomCount = layout.rooms != null ? layout.rooms.Count : 0;
        int doorwayCount = layout.doorways != null ? layout.doorways.Count : 0;

        Log($"Validation: {roomCount} room(s), {doorwayCount} doorway(s): " +
            $"{result.errors.Count} error(s), {result.warnings.Count} warning(s).");

        LogErrorBlock(result.errors);
        foreach (string warning in result.warnings)
            LogWarning(warning);

        if (!result.HasErrors)
            LogLayoutSummary(layout, result);
    }

    private void LogLayoutSummary(BuildingLayout layout, LayoutValidationResult result)
    {
        Log($"Layout '{layout.layoutName}': default floor {F(layout.defaultFloorY)}, default ceiling {F(layout.defaultCeilingY)}, " +
            $"wallMatchTolerance {F(layout.wallMatchTolerance)} ft.");

        Log("Rooms:");
        for (int i = 0; i < layout.rooms.Count; i++)
        {
            LayoutRoom room = layout.rooms[i];
            Log($"  {room.name}: x {F(room.xMin)}..{F(room.xMax)}, z {F(room.zMin)}..{F(room.zMax)} " +
                $"({F(room.Width)} x {F(room.Depth)} ft), floor {F(layout.GetFloorY(room))}, ceiling {F(layout.GetCeilingY(room))}" +
                (room.overrideHeights ? " (override)" : string.Empty));
        }

        if (result.doorways.Count == 0)
        {
            Log("Doorways: none.");
            return;
        }

        Log("Doorways:");
        foreach (ResolvedDoorway rd in result.doorways)
        {
            LayoutDoorway d = rd.doorway;
            Log($"  {d.name}: {BuildingLayoutValidator.DoorwaySpan(d)}, {F(d.width)} x {F(d.height)} ft, " +
                $"opening y {F(rd.floorY)}..{F(rd.floorY + d.height)}");

            foreach (DoorwayWallMatch m in rd.matches)
                Log($"    cuts {BuildingLayoutValidator.WallLabel(layout.rooms, m.roomIndex, m.wall)} (distance {F(m.distance)} ft)");

            if (rd.matches.Count == 2)
            {
                string axis = BuildingLayoutValidator.LineAxisName(d.runsAlong);
                Log(rd.needsPassage
                    ? $"    gap {F(rd.passageLineMax - rd.passageLineMin)} ft ({axis} {F(rd.passageLineMin)}..{F(rd.passageLineMax)}): passage will be generated"
                    : "    shared wall line: no passage needed");
            }
        }
    }

    private static string F(float value)
    {
        return BuildingLayoutValidator.Fmt(value);
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
            _log.AppendLine($"[ERROR] {errors[i]}");
    }

    #endregion
}
