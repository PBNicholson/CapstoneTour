
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Per-instance POI marker behavior. Handles Y-axis billboarding toward the camera,
/// click interaction routing based on POIInteractionType, and ownership of the info panel.
/// Configured at runtime vie Initialize() - no Inspector data authoring needed per instance.
/// </summary>
[RequireComponent(typeof(Canvas))]
public class POIMarker : MonoBehaviour, IPointerClickHandler
{
    #region Serialized Fields

    [Header("UI References")]

    [Tooltip("The marker icon Image. Click target and visual placeholder.")]
    [SerializeField] private Image markerIcon;

    [Tooltip("Label shown persistently for DisplayOnly POIs. Hidden for all other interaction types.")]
    [SerializeField] private TextMeshProUGUI persistentLabel;

    [Tooltip("Reference to the POIInfoPanel component on the InfoPanel child.")]
    [SerializeField] private POIInfoPanel infoPanel;

    #endregion

    #region Private State

    private POIData _data;
    private Camera _camera;
    private TourManager _tourManager;
    private Canvas _canvas;
    private bool _isInitialized;

    #endregion

    #region Public API

    /// <summary>
    /// Configures the marker with its POI data and scene dependencies.
    /// Called by POIMarkerManager immediately after instantiation.
    /// </summary>
    /// <param name="data">The POIData this marker represents.</param>
    /// <param name="camera">Main camera for billboarding the canvas rendering.</param>
    /// <param name="tourManager">TourManager reference for NavigationTrigger interaction.</param>
    public void Initialize(POIData data, Camera camera, TourManager tourManager)
    {
        _data = data;
        _camera = camera;
        _tourManager = tourManager;

        _canvas = GetComponent<Canvas>();
        _canvas.worldCamera = camera;

        ConfigureDisplay();

        if (infoPanel != null)
        {
            infoPanel.Initialize(data);
            infoPanel.Close();
        }
        _isInitialized = true;
    }

    /// <summary>
    /// Closes the info panel and releases any held resources.
    /// Called by POIMarkerManager before destroying this marker.
    /// </summary>
    public void Cleanup()
    {
        if (infoPanel != null)
            infoPanel.Close();

        _isInitialized = false;
    }

    #endregion

    #region Unity Lifecycle

    private void LateUpdate()
    {
        if (!_isInitialized)
            return;

        Billboard();
    }

    #endregion

    #region Interaction

    public void OnPointerClick(PointerEventData eventData)
    {
        if (!_isInitialized || _data == null) 
            return;

        // Only respond to left-click
        if (eventData.button != PointerEventData.InputButton.Left)
            return;

        switch (_data.interactionType)
        {
            case POIInteractionType.DisplayOnly:
                // No click response
                break;

            case POIInteractionType.Expandable:
                if (infoPanel != null)
                    infoPanel.Toggle();
                break;

            case POIInteractionType.ExternalLink:
                if (!string.IsNullOrEmpty(_data.externalUrl))
                    Application.OpenURL(_data.externalUrl);
                else
                    Debug.LogWarning($"[POIMarker] POI '{_data.label}' has ExternalLink type but no URL assigned.");
                break;

            case POIInteractionType.NavigationTrigger:
                if (!string.IsNullOrEmpty(_data.targetNodeId))
                    _tourManager.NavigateToNode(_data.targetNodeId);
                else
                    Debug.LogWarning($"[POIMarker] POI '{_data.label}' has NavigationTrigger type but no target NodeId assigned.");
                break;
        }
    }

    #endregion

    #region Private Methods

    private void ConfigureDisplay()
    {
        if (persistentLabel != null)
        {
            persistentLabel.gameObject.SetActive(true);
            persistentLabel.text = _data.label;
        }
    }

    private void Billboard()
    {
        Vector3 directionToCamera = _camera.transform.position - transform.position;
        directionToCamera.y = 0f;

        if (directionToCamera.sqrMagnitude < 0.001f)
            return;

        transform.rotation = Quaternion.LookRotation(-directionToCamera);
    }

    #endregion
}
