using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Per-instance POI marker behavior. Handles facing the camera (the canvas is kept parallel
/// to the camera's view plane), constant on-screen sizing, click routing based on POIInteractionType, the marker's
/// Open state while its info panel is showing, and ownership of the info panel.
/// Configured at runtime via Initialize() - no Inspector data authoring needed per instance.
/// </summary>
[RequireComponent(typeof(Canvas))]
public class POIMarker : MonoBehaviour
{
    #region Serialized Fields

    [Header("UI References")]

    [Tooltip("Button on the Marker object. Its target graphic is the marker's Fill image; " +
             "its ColorBlock holds the Normal state colours.")]
    [SerializeField] private Button markerButton;

    [Tooltip("The info glyph Image on the marker. Recoloured while the info panel is open.")]
    [SerializeField] private Image markerGlyph;

    [Tooltip("The marker's outer ring Image (bottom keyline layer), stretch-anchored to the Marker object. " +
             "Its colour and outset change while the info panel is open.")]
    [SerializeField] private Image markerOuterRing;

    [Tooltip("Root GameObject of the label pill. Shown for every interaction type; hidden while the info panel is open.")]
    [SerializeField] private GameObject persistentLabelRoot;

    [Tooltip("Text inside the label pill.")]
    [SerializeField] private TextMeshProUGUI persistentLabel;

    [Tooltip("Reference to the POIInfoPanel component on the InfoPanel child.")]
    [SerializeField] private POIInfoPanel infoPanel;

    [Header("Open State (info panel showing)")]

    [Tooltip("Fill colour while the panel is open. Spec: Gold #CFAB7A.")]
    [SerializeField] private Color openNormalColor = new Color32(0xCF, 0xAB, 0x7A, 0xFF);

    [Tooltip("Fill colour on hover while the panel is open. Gold Hover #DDBF96 (not in the spec's marker table).")]
    [SerializeField] private Color openHighlightedColor = new Color32(0xDD, 0xBF, 0x96, 0xFF);

    [Tooltip("Fill colour when pressed while the panel is open. Gold Pressed #BA9A6E (not in the spec's marker table).")]
    [SerializeField] private Color openPressedColor = new Color32(0xBA, 0x9A, 0x6E, 0xFF);

    [Tooltip("Glyph colour while the panel is open. Spec: Ink #323232.")]
    [SerializeField] private Color openGlyphColor = new Color32(0x32, 0x32, 0x32, 0xFF);

    [Tooltip("Outer ring colour while the panel is open. Spec: Blue #004B8D.")]
    [SerializeField] private Color openRingColor = new Color32(0x00, 0x4B, 0x8D, 0xFF);

    [Tooltip("How far the outer ring extends past the White keyline while the panel is open, in canvas units. Spec: 3.")]
    [SerializeField] private float openRingOutset = 3f;

    [Tooltip("Sorting order of this marker's canvas while its panel is open, so the panel draws over neighbouring markers.")]
    [SerializeField] private int openSortingOrder = 1;

    #endregion

    #region Private State

    // Markers closer than this along the view direction (or behind the camera) are sized as if at this depth.
    private const float MinViewDepth = 0.01f;

    private POIData _data;
    private Camera _camera;
    private TourManager _tourManager;
    private Canvas _sizingCanvas;
    private Canvas _canvas;
    private bool _isInitialized;
    private bool _isDisplayOnly;

    // Normal-state values, captured from the prefab at Initialize so the Inspector stays authoritative.
    private ColorBlock _normalColors;
    private Color _normalGlyphColor;
    private Color _normalRingColor;
    private float _normalRingOutset;
    private int _normalSortingOrder;

    #endregion

    #region Public API

    /// <summary>
    /// Configures the marker with its POI data and scene dependencies.
    /// Called by POIMarkerManager immediately after instantiation.
    /// </summary>
    /// <param name="data">The POIData this marker represents.</param>
    /// <param name="camera">Main camera for billboarding, sizing and canvas event handling.</param>
    /// <param name="tourManager">TourManager reference for NavigationTrigger interaction.</param>
    /// <param name="sizingCanvas">Overlay canvas whose scaleFactor (set by its Canvas Scaler) sizes this marker.</param>
    public void Initialize(POIData data, Camera camera, TourManager tourManager, Canvas sizingCanvas)
    {
        _data = data;
        _camera = camera;
        _tourManager = tourManager;
        _sizingCanvas = sizingCanvas;
        _isDisplayOnly = data.interactionType == POIInteractionType.DisplayOnly;

        _canvas = GetComponent<Canvas>();
        _canvas.worldCamera = camera;
        _normalSortingOrder = _canvas.sortingOrder;

        CaptureNormalState();
        ConfigureDisplay();

        if (markerButton != null)
        {
            markerButton.onClick.RemoveListener(HandleMarkerClicked);
            markerButton.onClick.AddListener(HandleMarkerClicked);
        }

        if (infoPanel != null)
        {
            infoPanel.Initialize(data);
            infoPanel.OpenStateChanged -= HandlePanelOpenStateChanged;
            infoPanel.OpenStateChanged += HandlePanelOpenStateChanged;
            infoPanel.Close();
        }

        ApplyOpenState(false);

        _isInitialized = true;

        // Size and face the camera immediately so the first rendered frame is correct.
        Billboard();
        UpdateScreenSize();
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

    private void Awake()
    {
        ValidateReferences();
    }

    private void LateUpdate()
    {
        if (!_isInitialized)
            return;

        Billboard();
        UpdateScreenSize();
    }

    private void OnDestroy()
    {
        if (infoPanel != null)
            infoPanel.OpenStateChanged -= HandlePanelOpenStateChanged;
    }

    #endregion

    #region Interaction

    private void HandleMarkerClicked()
    {
        if (!_isInitialized || _data == null)
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

    private void HandlePanelOpenStateChanged(bool isOpen)
    {
        ApplyOpenState(isOpen);
    }

    #endregion

    #region Private Methods

    private void ConfigureDisplay()
    {
        // The label is shown for every interaction type. It is hidden only while the info panel is open.
        if (persistentLabel != null)
            persistentLabel.text = _data.label;

        if (persistentLabelRoot != null)
            persistentLabelRoot.SetActive(true);
    }

    private void CaptureNormalState()
    {
        if (markerButton != null)
            _normalColors = markerButton.colors;

        if (markerGlyph != null)
            _normalGlyphColor = markerGlyph.color;

        if (markerOuterRing != null)
        {
            _normalRingColor = markerOuterRing.color;
            _normalRingOutset = -markerOuterRing.rectTransform.offsetMin.x;
        }
    }

    /// <summary>
    /// Switches the marker between its Normal colours and its Open colours, hides the label
    /// while the panel is showing, and raises the canvas so the panel draws over other markers.
    /// </summary>
    private void ApplyOpenState(bool isOpen)
    {
        if (markerButton != null)
        {
            ColorBlock colors = _normalColors;

            if (isOpen)
            {
                colors.normalColor = openNormalColor;
                colors.highlightedColor = openHighlightedColor;
                colors.pressedColor = openPressedColor;
                colors.selectedColor = openNormalColor;
            }
            else if (_isDisplayOnly)
            {
                // No click response, so no hover or pressed change either.
                colors.highlightedColor = colors.normalColor;
                colors.pressedColor = colors.normalColor;
                colors.selectedColor = colors.normalColor;
            }

            // Assigning colors re-applies the current state transition.
            markerButton.colors = colors;
        }

        if (markerGlyph != null)
            markerGlyph.color = isOpen ? openGlyphColor : _normalGlyphColor;

        if (markerOuterRing != null)
        {
            markerOuterRing.color = isOpen ? openRingColor : _normalRingColor;

            float outset = isOpen ? openRingOutset : _normalRingOutset;
            RectTransform ring = markerOuterRing.rectTransform;
            ring.offsetMin = new Vector2(-outset, -outset);
            ring.offsetMax = new Vector2(outset, outset);
        }

        if (persistentLabelRoot != null)
            persistentLabelRoot.SetActive(!isOpen);

        if (_canvas != null)
            _canvas.sortingOrder = isOpen ? openSortingOrder : _normalSortingOrder;
    }

    /// <summary>
    /// Keeps the canvas parallel to the camera's view plane, so the marker, label and panel
    /// are never sheared or keystoned when the camera pitches or the marker is off-centre.
    /// The canvas's forward then points away from the camera, which is its visible side.
    /// </summary>
    private void Billboard()
    {
        transform.rotation = _camera.transform.rotation;
    }

    /// <summary>
    /// Scales the root so one canvas unit covers sizingCanvas.scaleFactor screen pixels, the
    /// same rule the overlay canvases use. The visible world height at view depth d is
    /// 2 * d * tan(FOV / 2), so one screen pixel at that depth is that height divided by the
    /// camera's pixel height. View depth (not straight-line distance) keeps the size constant
    /// as the marker moves across the screen.
    /// </summary>
    private void UpdateScreenSize()
    {
        if (_sizingCanvas == null)
            return;

        float pixelsPerCanvasUnit = _sizingCanvas.scaleFactor;
        int cameraPixelHeight = _camera.pixelHeight;

        if (pixelsPerCanvasUnit <= 0f || cameraPixelHeight <= 0)
            return;

        Transform cameraTransform = _camera.transform;

        float viewDepth = Vector3.Dot(transform.position - cameraTransform.position, cameraTransform.forward);

        if (viewDepth < MinViewDepth)
            viewDepth = MinViewDepth;

        float halfFovRadians = _camera.fieldOfView * 0.5f * Mathf.Deg2Rad;
        float worldUnitsPerScreenPixel = 2f * viewDepth * Mathf.Tan(halfFovRadians) / cameraPixelHeight;
        float scale = worldUnitsPerScreenPixel * pixelsPerCanvasUnit;

        transform.localScale = new Vector3(scale, scale, scale);
    }

    #endregion

    #region Validation

    private void ValidateReferences()
    {
        if (markerButton == null)
            Debug.LogError("[POIMarker] MarkerButton reference is not assigned.");

        if (markerGlyph == null)
            Debug.LogError("[POIMarker] MarkerGlyph reference is not assigned.");

        if (markerOuterRing == null)
            Debug.LogError("[POIMarker] MarkerOuterRing reference is not assigned.");

        if (persistentLabelRoot == null)
            Debug.LogError("[POIMarker] PersistentLabelRoot reference is not assigned.");

        if (persistentLabel == null)
            Debug.LogError("[POIMarker] PersistentLabel reference is not assigned.");

        if (infoPanel == null)
            Debug.LogError("[POIMarker] InfoPanel reference is not assigned.");
    }

    #endregion
}