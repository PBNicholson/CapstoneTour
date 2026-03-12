
using UnityEngine;

/// <summary>
/// Handles Right-click spatial navigation for the virtual tour
/// Raycasts against TourGeometry collider geometry, resolves the nearest navigable node
/// to the hit point, and delegates navigation to TourManager
/// </summary>
public class SpatialNavigator : MonoBehaviour
{
    #region Fields

    [Header("System References")]

    [Tooltip("Reference to the TourManager in the scene. Required.")]
    [SerializeField] private TourManager tourManager;

    [Header("Raycast Configuration")]

    [Tooltip("Layer mask targeting the TourGeometry layer. Raycasts will only hit colliders on this layer")]
    [SerializeField] private LayerMask tourGeometryMask;

    [Header("Debug")]

    [Tooltip("When enabled, logs hit point, resolved node, and distance on every right-click raycast.")]
    [SerializeField] private bool debugMode = false;

    // Cached camera reference to avoid Camera.main lookup every frame
    private Camera _camera;

    private bool _isReady;
    #endregion

    #region Unity Lifecycle

    private void Awake()
    {
        _camera = Camera.main;

        _isReady = ValidateReferences();

        // If the layer mask was left at default (0 / Nothing), attempt to resolve
        // the "TourGeometry" layer automatically and warn the user.
        if (tourGeometryMask.value == 0)
        {
            int layerIndex = LayerMask.NameToLayer("TourGeometry");

            if (layerIndex == -1)
            {
                Debug.LogError("[SpatialNavigator] 'TourGeometry' layer does not exist. " +
                               "Create it in Project Settings > Tags and Layers, then assign it to the InvertedBoxGenerator GameObject.");
                _isReady = false;
            }
            else
            {
                tourGeometryMask = 1 << layerIndex;
                Debug.LogWarning("[SpatialNavigator] TourGeometry layer mask was not assigned in the Inspector. " +
                                 "Resolved automatically — assign it explicitly to suppress this warning.");
            }
        }
    }

    private void Update()
    {
        if (!_isReady)
            return;

        if (!Input.GetMouseButtonDown(1))
            return;

        if (tourManager.IsTransitioning)
        {
            if (debugMode)
                Debug.Log("[SpatialNavigator] Right-click ignored: transition in progress.");
            return;
        }

        HandleNavigationClick(Input.mousePosition);
    }

    #endregion

    #region Navigation

    /// <summary>
    /// Performs the full raycast -> node resolution -> navigate pipeline for a given screen position
    /// </summary>
    /// <param name="screenPosition">Screen-space position to raycast from (typically Input.mousePosition).</param>
    private void HandleNavigationClick(Vector3 screenPosition)
    {
        Ray ray = _camera.ScreenPointToRay(screenPosition);

        if (!Physics.Raycast(ray, out RaycastHit hit, Mathf.Infinity, tourGeometryMask))
        {
            if (debugMode)
                Debug.Log("[SpatialNavigator] Raycast missed Tour Geometry - no hit.");
            return;
        }

        if (debugMode)
            Debug.Log($"[SpatialNavigator] Raycast hit at world position {hit.point}.");

        ResolveAndNavigate(hit.point);
    }

    /// <summary>
    /// Resolves the nearest node to the given world position and navigates if one is found.
    /// </summary>
    /// <param name="worldPosition">World-space hit point from raycast.</param>
    private void ResolveAndNavigate(Vector3 worldPosition)
    {
        TourData tour = tourManager.CurrentTour;

        if (tour == null)
        {
            if (debugMode)
                Debug.Log("[SpatialNavigator] No tour loaded - navigation aborted");
            return;
        }

        string excludeId = tourManager.CurrentNode != null ? tourManager.CurrentNode.id : null;

        NodeData nearest = tour.GetNearestNode(worldPosition, excludeNodeId: excludeId, floorFilter: null);

        if (nearest == null)
        {
            if (debugMode)
                Debug.Log($"[SpatialNavigator] No node found within navigation radius ({tour.navigationRadius}m) of {worldPosition}.");
            return;
        }

        if (debugMode)
        {
            float dx = nearest.position.x - worldPosition.x;
            float dz = nearest.position.z - worldPosition.z;
            float horizontalDistance = Mathf.Sqrt(dx * dx + dz * dz);
            Debug.Log($"[SpatialNavigator] Nearest node: '{nearest.id}' ({nearest.displayName}), " +
                      $"horizontal distance: {horizontalDistance:F2}m. Navigating.");
        }

        tourManager.NavigateToNode(nearest);
    }

    #endregion

    #region Validation

    private bool ValidateReferences()
    {
        bool valid = true;

        if (tourManager == null)
        {
            Debug.LogError("[SpatialNavigator] TourManager reference is not assigned.");
            valid = false;
        }

        if (_camera == null)
        {
            Debug.LogError("[SpatialNavigator] No Camera tagged 'MainCamera' found in scene. " +
                           "Ensure the tour camera has the MainCamera tag.");
            valid = false;
        }

        return valid;
    }

    #endregion

    #region ContextMenu Debug

    [ContextMenu("Simulate Navigation (Center Screen)")]
    private void DebugSimulateNavigation()
    {
        if (!Application.isPlaying)
        {
            Debug.LogWarning("[SpatialNavigator] Simulation only runs in Play Mode.");
            return;
        }

        if (!_isReady)
        {
            Debug.LogWarning("[SpatialNavigator] Navigator is not ready — check reference errors above.");
            return;
        }

        if (tourManager.IsTransitioning)
        {
            Debug.LogWarning("[SpatialNavigator] Simulation ignored: transition in progress.");
            return;
        }

        Vector3 centerScreen = new Vector3(Screen.width * 0.5f, Screen.height * 0.5f, 0f);
        Debug.Log("[SpatialNavigator] Simulating navigation from screen center.");
        HandleNavigationClick(centerScreen);
    }

    [ContextMenu("Log Navigator State")]
    private void DebugLogState()
    {
        Debug.Log("[SpatialNavigator] === Navigator State ===");
        Debug.Log($"  Ready: {_isReady}");
        Debug.Log($"  Debug Mode: {debugMode}");
        Debug.Log($"  Camera: {(_camera != null ? _camera.name : "NULL")}");
        Debug.Log($"  TourManager: {(tourManager != null ? tourManager.name : "NULL")}");
        Debug.Log($"  Layer Mask: {tourGeometryMask.value} ({LayerMask.LayerToName((int)Mathf.Log(tourGeometryMask.value, 2))})");

        if (tourManager != null)
        {
            Debug.Log($"  Current Tour: {(tourManager.CurrentTour != null ? tourManager.CurrentTour.buildingName : "None")}");
            Debug.Log($"  Current Node: {(tourManager.CurrentNode != null ? $"{tourManager.CurrentNode.id} ({tourManager.CurrentNode.displayName})" : "None")}");
            Debug.Log($"  Is Transitioning: {tourManager.IsTransitioning}");
        }
    }

    #endregion
}
