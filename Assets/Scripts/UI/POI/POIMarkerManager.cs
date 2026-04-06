
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Manages the lifecycle of POI markers in the scene.
/// Subscribes to TourManager.OnNodeChanged and spawns/despawns markers
/// when the active node changes. Each marker is positioned at its POIData's
/// worldPosition and self-configures via POIMarker.Initialize().
/// </summary>
public class POIMarkerManager : MonoBehaviour
{
    #region Serialized Fields

    [Header("System References")]

    [Tooltip("Reference to the TourManager in the scene. Required.")]
    [SerializeField] private TourManager tourManager;

    [Tooltip("Main camera used for world-space canvas rendering and billboarding. Required.")]
    [SerializeField] private Camera mainCamera;

    [Header("Prefab")]

    [Tooltip("POI marker prefab. must have a POIMarker component on the root GameObject.")]
    [SerializeField] private GameObject markerPrefab;

    #endregion

    #region Private State

    private readonly List<POIMarker> _activeMarkers = new List<POIMarker>();
    private bool _isReady;

    #endregion

    #region Unity Lifecycle

    private void Awake()
    {
        _isReady = ValidateReferences();
    }

    private void Start()
    {
        if (!_isReady)
            return;

        tourManager.OnNodeChanged += HandleNodeChanged;

        // Handle case where tour initialized before this component's Start.
        if (tourManager.CurrentNode != null)
        {
            HandleNodeChanged(tourManager.CurrentNode);
        }
    }

    private void OnDestroy()
    {
        if (tourManager != null)
        {
            tourManager.OnNodeChanged -= HandleNodeChanged;
        }

        ClearMarkers();
    }

    #endregion

    #region Private Methods

    private void HandleNodeChanged(NodeData node)
    {
        ClearMarkers();

        if (node == null || node.pointsOfInterest == null)
            return;

        for (int i = 0; i < node.pointsOfInterest.Count; i++)
        {
            POIData poiData = node.pointsOfInterest[i];

            if (poiData == null)
            {
                Debug.LogWarning($"[POIMarkerManager] Node '{node.id}' has a null entry at pointsOfInterest[{i}]. Skipping.");
                continue;
            }

            GameObject instance = Instantiate(markerPrefab, poiData.worldPosition, Quaternion.identity);
            POIMarker marker = instance.GetComponent<POIMarker>();

            if (marker == null)
            {
                Debug.LogError($"[POIMarkerManager] Marker prefab is missing a POIMarker component on the root. Destroying instance.");
                Destroy(instance);
                continue;
            }

            marker.Initialize(poiData, mainCamera, tourManager);
            _activeMarkers.Add(marker);
        }
    }

    private void ClearMarkers()
    {
        for (int i = 0; i < _activeMarkers.Count; i++)
        {
            if (_activeMarkers[i] != null)
            {
                Destroy(_activeMarkers[i].gameObject);
            }
        }

        _activeMarkers.Clear();
    }

    #endregion

    #region Validation

    private bool ValidateReferences()
    {
        bool valid = true;
        if (tourManager == null)
        {
            Debug.LogError("[POIMarkerManager] TourManager reference is not assigned.");
            valid = false;
        }
        if (mainCamera == null)
        {
            Debug.LogError("[POIMarkerManager] Main Camera reference is not assigned.");
            valid = false;
        }
        if (markerPrefab == null)
        {
            Debug.LogError("[POIMarkerManager] Marker Prefab reference is not assigned.");
            valid = false;
        }

        return valid;
    }

    #endregion
}
