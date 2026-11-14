using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Builds a tour's navigation colliders at runtime from TourData.layout.
/// Subscribes to TourManager.OnTourChanged: destroys the previous tour's colliders, then builds the new
/// tour's layout with TourGeometryBuilder and places the result on the TourGeometry layer.
///
/// Tours without a layout (or whose layout fails validation) use the fallback geometry instead, which is
/// the scene's LobbyGeometry / InvertedBoxGenerator object. The fallback is deactivated while layout
/// geometry is loaded and reactivated otherwise.
///
/// Nothing built here is saved: the hierarchy and its meshes exist only in Play mode / the player.
/// The Tour Geometry Generator window is still the authoring tool (import, validate, preview, radius report).
/// </summary>
public class TourGeometryLoader : MonoBehaviour
{
    #region Serialized Fields

    [Header("System References")]

    [Tooltip("Reference to the TourManager in the scene. Required.")]
    [SerializeField] private TourManager tourManager;

    [Tooltip("Geometry used by tours that have no layout (the LobbyGeometry object). " +
             "Deactivated while layout geometry is loaded. Optional.")]
    [SerializeField] private GameObject fallbackGeometry;

    #endregion

    #region Private State

    private const string TourGeometryLayerName = "TourGeometry";

    private readonly List<Mesh> _meshes = new List<Mesh>();
    private GameObject _root;
    private BuildingLayout _loadedLayout;
    private int _layer = -1;
    private bool _isReady;

    #endregion

    #region Unity Lifecycle

    private void Awake()
    {
        _isReady = ValidateSetup();

        if (_isReady)
            DisableSavedGeneratedRoots();
    }

    private void OnEnable()
    {
        if (_isReady)
            tourManager.OnTourChanged += HandleTourChanged;
    }

    private void Start()
    {
        // Covers a tour that was loaded before this component subscribed.
        if (_isReady)
            Apply(tourManager.CurrentTour);
    }

    private void OnDisable()
    {
        if (_isReady && tourManager != null)
            tourManager.OnTourChanged -= HandleTourChanged;
    }

    private void OnDestroy()
    {
        Clear();
    }

    #endregion

    #region Tour Changes

    private void HandleTourChanged(TourData tour)
    {
        Apply(tour);
    }

    /// <summary>
    /// Makes the loaded geometry match the given tour. Never throws: this runs inside
    /// TourManager.LoadTour's event, and an exception here would abort the tour load.
    /// </summary>
    private void Apply(TourData tour)
    {
        BuildingLayout layout = tour != null ? tour.layout : null;

        // Already showing this layout.
        if (layout != null && layout == _loadedLayout && _root != null)
            return;

        Clear();

        if (layout == null)
        {
            SetFallbackActive(true);
            return;
        }

        try
        {
            BuildFrom(layout);
        }
        catch (Exception ex)
        {
            Debug.LogError($"[TourGeometryLoader] Building geometry for layout '{layout.name}' failed. " +
                           "Using fallback geometry.");
            Debug.LogException(ex);
            Clear();
            SetFallbackActive(true);
        }
    }

    private void BuildFrom(BuildingLayout layout)
    {
        double startTime = Time.realtimeSinceStartupAsDouble;

        TourGeometryBuildResult build = TourGeometryBuilder.Build(layout, out LayoutValidationResult validation);

        if (build == null)
        {
            Debug.LogError($"[TourGeometryLoader] Layout '{layout.name}' failed validation with " +
                           $"{validation.errors.Count} error(s). Using fallback geometry. " +
                           "Open Tools > Tour Geometry Generator to see and fix them.\n  " +
                           string.Join("\n  ", validation.errors));
            SetFallbackActive(true);
            return;
        }

        // Track every mesh before creating objects, so Clear() destroys them even if a later step throws.
        foreach (GeneratedMeshInfo info in build.All)
        {
            if (info.mesh != null)
                _meshes.Add(info.mesh);
        }

        // Positions in the meshes are world-space, so the root stays unparented at the origin.
        _root = new GameObject($"{TourGeometryBuilder.RootName(layout)} (runtime)") { layer = _layer };

        Transform roomsGroup = CreateChild(_root.transform, "Rooms").transform;
        foreach (GeneratedMeshInfo info in build.rooms)
            CreateColliderObject(roomsGroup, info);

        if (build.passages.Count > 0)
        {
            Transform doorwaysGroup = CreateChild(_root.transform, "Doorways").transform;
            foreach (GeneratedMeshInfo info in build.passages)
                CreateColliderObject(doorwaysGroup, info);
        }

        _loadedLayout = layout;
        SetFallbackActive(false);
        Physics.SyncTransforms();

        double elapsedMs = (Time.realtimeSinceStartupAsDouble - startTime) * 1000.0;
        Debug.Log($"[TourGeometryLoader] Built {build.rooms.Count} room and {build.passages.Count} passage " +
                  $"collider(s) from layout '{layout.name}' in {elapsedMs:F1} ms.");
    }

    private GameObject CreateChild(Transform parent, string childName)
    {
        GameObject go = new GameObject(childName) { layer = _layer };
        go.transform.SetParent(parent, false);
        return go;
    }

    private void CreateColliderObject(Transform parent, GeneratedMeshInfo info)
    {
        GameObject go = CreateChild(parent, info.objectName);
        MeshCollider meshCollider = go.AddComponent<MeshCollider>();
        meshCollider.sharedMesh = info.mesh;
    }

    /// <summary>
    /// Destroys the loaded hierarchy and its meshes. The root is deactivated first because Destroy is
    /// deferred to the end of the frame, and the old colliders must not be hit in the meantime.
    /// </summary>
    private void Clear()
    {
        if (_root != null)
        {
            _root.SetActive(false);
            Destroy(_root);
            _root = null;
        }

        for (int i = 0; i < _meshes.Count; i++)
        {
            if (_meshes[i] != null)
                Destroy(_meshes[i]);
        }

        _meshes.Clear();
        _loadedLayout = null;
    }

    private void SetFallbackActive(bool active)
    {
        if (fallbackGeometry != null && fallbackGeometry.activeSelf != active)
            fallbackGeometry.SetActive(active);
    }

    #endregion

    #region Validation

    private bool ValidateSetup()
    {
        bool valid = true;

        if (tourManager == null)
        {
            Debug.LogError("[TourGeometryLoader] TourManager reference is not assigned. " +
                           "Layout geometry will not be built.");
            valid = false;
        }

        _layer = LayerMask.NameToLayer(TourGeometryLayerName);
        if (_layer < 0)
        {
            Debug.LogError($"[TourGeometryLoader] Layer '{TourGeometryLayerName}' does not exist. " +
                           "Create it in Project Settings > Tags and Layers. Layout geometry will not be built.");
            valid = false;
        }

        if (fallbackGeometry == null)
        {
            Debug.LogWarning("[TourGeometryLoader] Fallback Geometry is not assigned. " +
                             "Tours without a layout will only work if other TourGeometry colliders are active.");
        }

        return valid;
    }

    /// <summary>
    /// Geometry generated by the editor window and saved into the scene would sit on top of the colliders
    /// built here. Deactivates any such root that is active and says so.
    /// </summary>
    private void DisableSavedGeneratedRoots()
    {
        GameObject[] sceneRoots = gameObject.scene.GetRootGameObjects();

        for (int i = 0; i < sceneRoots.Length; i++)
        {
            GeneratedTourGeometry[] saved = sceneRoots[i].GetComponentsInChildren<GeneratedTourGeometry>(false);

            for (int j = 0; j < saved.Length; j++)
            {
                saved[j].gameObject.SetActive(false);
                Debug.LogWarning($"[TourGeometryLoader] Deactivated '{saved[j].gameObject.name}': generator output " +
                                 "saved in the scene. Runtime geometry is built from TourData.layout; " +
                                 "remove this object from the scene (Tools > Tour Geometry Generator > Remove Generated).",
                                 saved[j].gameObject);
            }
        }
    }

    #endregion
}
