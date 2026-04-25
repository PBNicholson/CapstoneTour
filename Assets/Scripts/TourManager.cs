
using System;
using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
#endif

/// <summary>
/// Orchestrates tour state and coordinates between PanoramaRenderer, camera, and UI systems.
/// Handles tour loading, unloading, and node navigation.
/// </summary>
public class TourManager : MonoBehaviour
{
    #region Serialized Fields

    [Header("System References")]

    [Tooltip("Reference to the PanoramaRenderer component in the scene.")]
    [SerializeField] private PanoramaRenderer panoramaRenderer;

    [Tooltip("Reference to the camera controller for applying rotation on tour start.")]
    [SerializeField] private CameraController cameraController;

    [Header("Editor Preview")]

    [Tooltip("Tour used for drawing node gizmos in the Scene view while authoring " +
             "Has no effect at runtime - the actively loaded tour always takes precedence. " +
             "Leave unassigned in production scenes; assign only while designing a specific tour's node layout.")]
    [SerializeField] private TourData editorPreviewTour;

    #endregion

    #region State

    private TourData tour;
    private int _cachedFloor = int.MinValue;

    /// <summary>
    /// The currently active tour data.
    /// </summary>
    public TourData CurrentTour => tour;

    /// <summary>
    /// The node currently being displayed (or loading).
    /// </summary>
    public NodeData CurrentNode { get; private set; }

    /// <summary>
    /// The floor number of the current node. Falls back to last known floor if CurrentNode is null.
    /// </summary>
    public int CurrentFloor => _cachedFloor;

    public bool IsTransitioning
    {
        get
        {
            // Primary condition: panorama is loading
            if (panoramaRenderer != null && panoramaRenderer.isLoading)
                return true;

            // Future extensibility: add additional conditions here (UI transitions, camera movements, etc.)

            return false;
        }
    }

    #endregion

    #region Events

    /// <summary>
    /// Fired when the current node changes. Parameter is the new node.
    /// </summary>
    public event Action<NodeData> OnNodeChanged;

    /// <summary>
    /// Fired when the active tour changes (initial load or runtime switch). Parameter is the new tour.
    /// </summary>
    public event Action<TourData> OnTourChanged;

    /// <summary>
    /// Fired when the floor changes during navigation. Parameter is the new floor number.
    /// Does not fire if navigating to a node on the same floor.
    /// </summary>
    public event Action<int> OnFloorChanged;

    #endregion

    #region Unity Lifecycle

    private void Awake()
    {
        ValidateReferences();
    }

    #endregion

    #region Public API

    /// <summary>
    /// Navigates to the specified node within the current tour.
    /// Camera orientation is preserved during navigation.
    /// </summary>
    /// <param name="node">The node to navigate to.</param>
    public void NavigateToNode(NodeData node)
    {
        // Validate node
        if (node == null)
        {
            Debug.LogWarning("[TourManager] NavigateToNode called with null node.");
            return;
        }

        // Skip if already at this node
        if (CurrentNode == node)
        {
            return;
        }

        // Validate node belongs to current tour
        if (tour == null)
        {
            Debug.LogWarning("[TourManager] Cannot navigate: no tour is loaded.");
            return;
        }

        if (!IsNodeInTour(node))
        {
            Debug.LogWarning($"[TourManager] Node '{node.id}' does not belong to current tour '{tour.buildingName}'.");
            return;
        }

        // Update state
        CurrentNode = node;

        // Update floor and fire event if changed
        UpdateFloor(node.floor);

        // Move camera to node position
        MoveCameraToNode(node);

        // Delegate panorama loading to renderer
        panoramaRenderer.LoadPanorama(node);

        // Fire node changed event
        OnNodeChanged?.Invoke(node);

        Debug.Log($"[TourManager] Navigated to node '{node.id}' ({node.displayName}) on floor {node.floor}.");
    }

    /// <summary>
    /// Navigates to a node by its ID within the current tour.
    /// Convenience overload that resolves the ID using TourData.GetNodeById().
    /// </summary>
    /// <param name="nodeId">The unique ID of the node to navigate to.</param>
    public void NavigateToNode(string nodeId)
    {
        if (string.IsNullOrEmpty(nodeId))
        {
            Debug.LogWarning("[TourManager] NavigateToNode called with null or empty nodeId.");
            return;
        }

        if (tour == null)
        {
            Debug.LogWarning("[TourManager] Cannot navigate: no tour is loaded.");
            return;
        }

        NodeData node = tour.GetNodeById(nodeId);

        if (node == null)
        {
            Debug.LogWarning($"[TourManager] Node with ID '{nodeId}' not found in tour '{tour.buildingName}'.");
            return;
        }

        NavigateToNode(node);
    }

    /// <summary>
    /// Loads a new tour at runtime. replacing the current tour.
    /// Replaces any currently loaded tour - no explicit unload is required
    /// </summary>
    /// <param name="newTour">The tour to load. Must have a valid defaultStartNodeId.</param>
    public void LoadTour(TourData newTour)
    {
        if (newTour == null)
        {
            Debug.LogWarning("[TourManager] LoadTour called with null TourData.");
            return;
        }

        // Validate the new tour has a valid start node
        NodeData startNode = newTour.GetStartNode();
        if (startNode == null)
        {
            Debug.LogError($"[TourManager] Tour '{newTour.buildingName}' has invalid defaultStartNodeId '{newTour.defaultStartNodeId}'.");
            return;
        }

        if (tour == newTour)
        {
            return;
        }

        // Update tour reference
        tour = newTour;

        // Fire tour changed event
        OnTourChanged?.Invoke(newTour);

        NavigateToStartNode(startNode);

        Debug.Log($"[TourManager] Loaded tour '{newTour.buildingName}' at node '{startNode.id}'.");
    }

    /// <summary>
    /// Unloads the current tour, clearing the skybox and resetting node/floor state.
    /// </summary>
    public void UnloadTour()
    {
        if (tour == null)
            return;

        string unloadedName = tour.buildingName;

        panoramaRenderer.ClearSkybox();

        CurrentNode = null;
        tour = null;
        _cachedFloor = int.MinValue;

        OnTourChanged?.Invoke(null);
        OnNodeChanged?.Invoke(null);

        Debug.Log($"[TourManager] Unloaded tour '{unloadedName}'.");
    }

    #endregion

    #region Private Methods

    private void ValidateReferences()
    {
        if (panoramaRenderer == null)
        {
            Debug.LogError("[TourManager] PanoramaRenderer reference is not assigned.");
        }

        if (cameraController == null)
        {
            Debug.LogError("[TourManager] Camera controller reference is not assigned.");
        }
    }

    /// <summary>
    /// Navigates to a start node. Used by LoadTour during tour initialization.
    /// </summary>
    private void NavigateToStartNode(NodeData startNode)
    {
        // Update state
        CurrentNode = startNode;

        // Update floor (always set on start, fire event)
        int previousFloor = _cachedFloor;
        _cachedFloor = startNode.floor;

        // Fire floor changed if this isn't the first load or floor differs
        if (previousFloor != _cachedFloor || previousFloor == int.MinValue)
        {
            OnFloorChanged?.Invoke(_cachedFloor);
        }

        MoveCameraToNode(startNode);

        // Load panorama
        panoramaRenderer.LoadPanorama(startNode);

        // Fire node changed event
        OnNodeChanged?.Invoke(startNode);
    }

    private void UpdateFloor(int newFloor)
    {
        if (_cachedFloor != newFloor)
        {
            _cachedFloor = newFloor;
            OnFloorChanged?.Invoke(newFloor);
        }
    }

    private bool IsNodeInTour(NodeData node)
    {
        if (tour == null || tour.nodes == null)
            return false;

        for (int i = 0; i < tour.nodes.Count; i++)
        {
            if (tour.nodes[i] == node)
                return true;
        }

        return false;
    }

    /// <summary>
    /// Moves the camera to the world position of the given node.
    /// Called on every navigation to keep the camera origin consistent with
    /// the panorama capture point, which ensures raycasts originate from the
    /// correct position and node gizmos remain spatially accurate.
    /// </summary>
    private void MoveCameraToNode(NodeData node)
    {
        if (cameraController == null)
            return;

        cameraController.transform.position = node.position;
        Debug.Log($"[TourManager] Camera moved to node position {node.position}.");
    }

    #endregion

    #region Debug / Testing

    [ContextMenu("Navigate to Next Node")]
    private void DebugNavigateToNextNode()
    {
        if (tour == null || tour.nodes == null || tour.nodes.Count == 0)
        {
            Debug.LogWarning("[TourManager] Cannot navigate: no tour loaded or tour has no nodes.");
            return;
        }

        int currentIndex = GetCurrentNodeIndex();
        int nextIndex = (currentIndex + 1) % tour.nodes.Count;

        NavigateToNode(tour.nodes[nextIndex]);
    }

    [ContextMenu("Navigate to Previous Node")]
    private void DebugNavigateToPreviousNode()
    {
        if (tour == null || tour.nodes == null || tour.nodes.Count == 0)
        {
            Debug.LogWarning("[TourManager] Cannot navigate: no tour loaded or tour has no nodes.");
            return;
        }

        int currentIndex = GetCurrentNodeIndex();
        int prevIndex = (currentIndex - 1 + tour.nodes.Count) % tour.nodes.Count;

        NavigateToNode(tour.nodes[prevIndex]);
    }

    [ContextMenu("Log Current State")]
    private void DebugLogCurrentState()
    {
        Debug.Log($"[TourManager] === Current State ===");
        Debug.Log($"  Tour: {(tour != null ? tour.buildingName : "None")}");
        Debug.Log($"  Node: {(CurrentNode != null ? $"{CurrentNode.id} ({CurrentNode.displayName})" : "None")}");
        Debug.Log($"  Floor: {CurrentFloor}");

        if (cameraController != null)
        {
            cameraController.GetRotation(out float yaw, out float pitch);
            Debug.Log($"  Camera Rotation: yaw={yaw:F1}, pitch={pitch:F1}");
        }
    }

    private int GetCurrentNodeIndex()
    {
        if (CurrentNode == null || tour == null)
            return 0;

        for (int i = 0; i < tour.nodes.Count; i++)
        {
            if (tour.nodes[i] == CurrentNode)
                return i;
        }

        return 0;
    }

    #endregion

    #region Gizmos

#if UNITY_EDITOR

    private void OnDrawGizmosSelected()
    {
        TourData gizmoTour = tour != null ? tour : editorPreviewTour;

        // Only draw if we have tour data with nodes
        if (gizmoTour == null || gizmoTour.nodes == null || gizmoTour.nodes.Count == 0)
            return;

        for (int i = 0; i < gizmoTour.nodes.Count; i++)
        {
            NodeData node = gizmoTour.nodes[i];
            if (node == null)
                continue;

            bool isCurrent = (CurrentNode != null && CurrentNode == node);
            Gizmos.color = isCurrent ? Color.yellow : Color.cyan;
            Gizmos.DrawSphere(node.position, 0.15f);
            Gizmos.color = new Color(Gizmos.color.r, Gizmos.color.g, Gizmos.color.b, 0.3f);
            Gizmos.DrawWireSphere(node.position, gizmoTour.navigationRadius);

            GUIStyle labelStyle = new GUIStyle();
            labelStyle.normal.textColor = isCurrent ? Color.yellow : Color.white;
            labelStyle.fontSize = 11;
            labelStyle.fontStyle = isCurrent ? FontStyle.Bold : FontStyle.Normal;

            string label = string.IsNullOrEmpty(node.displayName)
                ? node.id : $"{node.id}\n{node.displayName}";

            Vector3 labelPos = node.position + Vector3.up * 0.35f;
            Handles.Label(labelPos, label, labelStyle);
        }

        // --- POI gizmos ---
        // Orange: normal POI marker
        // Red: degenerate (worldPosition at or within 0.001m of the node, likely unset)
        Color poiOrange = new Color(1f, 0.5f, 0f, 1f);
        Color poiOrangeFade = new Color(1f, 0.5f, 0f, 0.35f);
        Color poiRed = new Color(1f, 0.15f, 0.15f, 1f);
        Color poiRedFade = new Color(1f, 0.15f, 0.15f, 0.35f);

        for (int i = 0; i < gizmoTour.nodes.Count; i++)
        {
            NodeData node = gizmoTour.nodes[i];
            if (node == null || node.pointsOfInterest == null)
                continue;

            for (int j = 0; j < node.pointsOfInterest.Count; j++)
            {
                POIData poi = node.pointsOfInterest[j];
                if (poi == null)
                    continue;

                if (!poi.showGizmos)
                    continue;

                float dist = (poi.worldPosition - node.position).magnitude;
                bool isDegenerate = dist < 0.001f;
                bool isUnset = poi.worldPosition == Vector3.zero;

                Color lineColor = isDegenerate ? poiRed : poiOrange;
                Color sphereColor = isDegenerate ? poiRedFade : poiOrangeFade;

                // Line from node to POI
                Handles.color = lineColor;
                if (isUnset)
                    Handles.DrawDottedLine(node.position, poi.worldPosition, 4f);
                else
                    Handles.DrawLine(node.position, poi.worldPosition, 2f);

                // Sphere at POI world position
                Gizmos.color = sphereColor;
                Gizmos.DrawSphere(poi.worldPosition, 0.1f);
                Gizmos.color = lineColor;
                Gizmos.DrawWireSphere(poi.worldPosition, 0.1f);

                // Label
                string poiLabel = string.IsNullOrEmpty(poi.label) ? poi.name : poi.label;
                if (isDegenerate) poiLabel += "\nWarning: too close to node";
                else if (isUnset) poiLabel += "\nWarning: worldPosition not set";

                GUIStyle poiStyle = new GUIStyle();
                poiStyle.normal.textColor = lineColor;
                poiStyle.fontSize = 10;
                poiStyle.fontStyle = FontStyle.Italic;

                Handles.Label(poi.worldPosition + Vector3.up * 0.25f, poiLabel, poiStyle);
            }
        }
    }

#endif

    #endregion
}
