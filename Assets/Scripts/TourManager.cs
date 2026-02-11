
using System;
using UnityEngine;

/// <summary>
/// Orchestrates tour state and coordinates between PanoramaRenderer, camera, and UI systems.
/// Handles tour initialization, node navigation, and runtime tour switching.
/// </summary>
public class TourManager : MonoBehaviour
{
    #region Serialized Fields

    [Header("Tour Configuration")]

    [Tooltip("The active tour data. Can be assigned in Inspector or switched at runtime via LoadTour().")]
    [SerializeField] private TourData tour;

    [Header("System References")]

    [Tooltip("Reference to the PanoramaRenderer component in the scene.")]
    [SerializeField] private PanoramaRenderer panoramaRenderer;

    [Tooltip("Reference to the camera controller for applying rotation on tour start.")]
    [SerializeField] private advCameraController cameraController;

    #endregion

    #region State

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

    private int _cachedFloor;

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

    private void Start()
    {
        if (tour != null)
        {
            InitializeTour();
        }
        else
        {
            Debug.LogWarning("[TourManager] No TourData assigned. Call LoadTour() to start a tour.");
        }
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
    /// Navigates to the new tour's default start node and applies its initial rotation.
    /// </summary>
    /// <param name="newTour">The tour to load.</param>
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

        // Update tour reference
        tour = newTour;

        // Fire tour changed event
        OnTourChanged?.Invoke(newTour);

        Debug.Log($"[TourManager] Loaded tour '{newTour.buildingName}'.");

        // Navigate tot start node with rotation reset
        NavigateToStartNode(startNode, applyRotation: true);
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

    private void InitializeTour()
    {
        NodeData startNode = tour.GetStartNode();

        if (startNode == null)
        {
            Debug.LogError($"[TourManager] Tour ' {tour.buildingName}' has invalid defaultStartNodeId '{tour.defaultStartNodeId}'.");
            return;
        }

        // Fire tour changed event for initial load
        OnTourChanged?.Invoke(tour);

        Debug.Log($"[TourManager] Initializing tour '{tour.buildingName}' at node '{startNode.id}'.");

        // Navigate to start node with rotation applied
        NavigateToStartNode(startNode, applyRotation: true);
    }

    /// <summary>
    /// Navigates to a start node, optionally applying its initial rotation.
    /// Used for tour initialization and tour switching.
    /// </summary>
    private void NavigateToStartNode(NodeData startNode, bool applyRotation)
    {
        // Update state
        CurrentNode = startNode;

        // Update floor (always set on start, fire event)
        int previousFloor = _cachedFloor;
        _cachedFloor = startNode.floor;

        // Fire floor changed if this isn't the first load or floor differs
        if (previousFloor != _cachedFloor || previousFloor == 0)
        {
            OnFloorChanged?.Invoke(_cachedFloor);
        }

        // Apply initial rotation if requested
        if (applyRotation && cameraController != null)
        {
            // Set yaw to node's initial rotation, pitch to 0 (level horizon)
            cameraController.SetRotation(startNode.initialRotation, 0f);
            Debug.Log($"[TourManager] Camera rotation set to yaw: {startNode.initialRotation}, pitch:0.");
        }

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
        Debug.Log($"  IsTransitioning: {IsTransitioning}");

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
}
