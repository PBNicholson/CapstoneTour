using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// One tour hotspot on the campus map. Spawned by CampusMapPanel, one per TourCatalog entry,
/// as a child of the map Image.
///
/// Configured at runtime via Initialize() - no Inspector data authoring per instance.
/// Has no knowledge of sibling hotspots: clicks and hover changes are reported through
/// callbacks supplied by CampusMapPanel, which owns selection and draw order and drives this
/// hotspot's selected visual via SetSelected().
///
/// Position: the hotspot's anchors are set to a position normalised to the map image
/// ((0,0) bottom-left, (1,1) top-right), so it stays on its building however the map is
/// cropped. The prefab root's pivot must be (0.5, 0.5).
///
/// Expected prefab (keyline recipe): the root holds the Button and this component and is the
/// size of the White layer. Children: OuterRing (stretched, 2 px outside), Keyline (White,
/// the only raycast target), Fill (the Button's Target Graphic, Image colour White, colours in
/// the ColorBlock), Dot, and NamePill (inactive by default).
///
/// Selected visual: the Button's colour tint cannot change the size, the ring or the dot, so
/// those are set here. The normal-state values are read from the prefab the first time they
/// are needed, so the prefab's Inspector values stay authoritative for the normal state.
///
/// The name pill is shown while the hotspot is hovered or selected.
/// </summary>
public class MapHotspot : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
{
    #region Serialized Fields

    [Header("UI References")]

    [Tooltip("Button that reports clicks. On the prefab root, with Target Graphic set to Fill.")]
    [SerializeField] private Button selectButton;

    [Tooltip("Outer ring Image behind the White keyline. Stretched to the root; its Size Delta sets how far it extends outside.")]
    [SerializeField] private Image outerRing;

    [Tooltip("Centre dot Image.")]
    [SerializeField] private Image centreDot;

    [Tooltip("Root object of the name pill. Shown while the hotspot is hovered or selected.")]
    [SerializeField] private GameObject namePillRoot;

    [Tooltip("Text inside the name pill. Receives the tour's building name.")]
    [SerializeField] private TextMeshProUGUI nameText;

    [Header("Selected State")]

    [Tooltip("Width and height of the hotspot while selected. Style spec: 56.")]
    [SerializeField] private float selectedSize = 56f;

    [Tooltip("Fill colour while selected. Style spec: Gold #CFAB7A.")]
    [SerializeField] private Color selectedFillColor = new Color32(0xCF, 0xAB, 0x7A, 0xFF);

    [Tooltip("Fill colour while selected and hovered. Gold Hover #DDBF96.")]
    [SerializeField] private Color selectedFillHighlightedColor = new Color32(0xDD, 0xBF, 0x96, 0xFF);

    [Tooltip("Fill colour while selected and pressed. Gold Pressed #BA9A6E.")]
    [SerializeField] private Color selectedFillPressedColor = new Color32(0xBA, 0x9A, 0x6E, 0xFF);

    [Tooltip("Outer ring colour while selected. Style spec: Blue #004B8D.")]
    [SerializeField] private Color selectedRingColor = new Color32(0x00, 0x4B, 0x8D, 0xFF);

    [Tooltip("How far the outer ring extends outside the White keyline while selected, in canvas units. Style spec: 3.")]
    [SerializeField] private float selectedRingOutset = 3f;

    [Tooltip("Centre dot colour while selected. Style spec: Ink #323232.")]
    [SerializeField] private Color selectedDotColor = new Color32(0x32, 0x32, 0x32, 0xFF);

    [Tooltip("Centre dot width and height while selected. Style spec: 16.")]
    [SerializeField] private float selectedDotSize = 16f;

    #endregion

    private TourData _data;
    private Action<MapHotspot> _onClicked;
    private Action<MapHotspot, bool> _onHoverChanged;
    private bool _isInitialized;
    private bool _isSelected;
    private bool _isHovered;

    // Normal-state values, read from the prefab by CacheNormalState().
    private bool _normalStateCached;
    private Vector2 _normalSize;
    private ColorBlock _normalColors;
    private Color _normalRingColor;
    private Vector2 _normalRingSizeDelta;
    private Color _normalDotColor;
    private Vector2 _normalDotSize;

    #region Public API

    /// <summary>
    /// The TourData this hotspot represents. Null until Initialize has been called.
    /// </summary>
    public TourData TourData => _data;

    /// <summary>
    /// True while the hotspot shows its selected visual.
    /// </summary>
    public bool IsSelected => _isSelected;

    /// <summary>
    /// Configures the hotspot with its tour, map position and callbacks.
    /// Called by CampusMapPanel immediately after instantiation. This can run before Awake
    /// (the panel builds its hotspots while it is still inactive), so it does not depend on
    /// anything Awake sets up.
    /// </summary>
    /// <param name="data">The TourData this hotspot represents.</param>
    /// <param name="normalizedPosition">Position on the map image, (0,0) bottom-left to (1,1) top-right.</param>
    /// <param name="onClicked">Invoked with this hotspot when the user clicks it. May be null.</param>
    /// <param name="onHoverChanged">
    /// Invoked with this hotspot and true when the pointer enters it, false when it leaves. May be null.
    /// </param>
    public void Initialize(
        TourData data,
        Vector2 normalizedPosition,
        Action<MapHotspot> onClicked,
        Action<MapHotspot, bool> onHoverChanged)
    {
        _data = data;
        _onClicked = onClicked;
        _onHoverChanged = onHoverChanged;

        if (nameText != null)
            nameText.text = data != null ? data.buildingName : string.Empty;

        SetNormalizedPosition(normalizedPosition);

        // Default to unselected and not hovered on initialization.
        _isHovered = false;
        SetSelected(false);

        _isInitialized = true;
    }

    /// <summary>
    /// Toggles the selected visual: size, fill colours, outer ring, centre dot and name pill.
    /// Driven externally by CampusMapPanel.
    /// </summary>
    /// <param name="selected">True for the selected visual, false for the normal state.</param>
    public void SetSelected(bool selected)
    {
        CacheNormalState();

        _isSelected = selected;

        RectTransform root = (RectTransform)transform;
        root.sizeDelta = selected ? new Vector2(selectedSize, selectedSize) : _normalSize;

        if (selectButton != null)
        {
            // Start from the prefab's ColorBlock so multiplier and fade duration are kept.
            ColorBlock colors = _normalColors;

            if (selected)
            {
                colors.normalColor = selectedFillColor;
                colors.highlightedColor = selectedFillHighlightedColor;
                colors.pressedColor = selectedFillPressedColor;
                colors.selectedColor = selectedFillColor;
            }

            selectButton.colors = colors;
        }

        if (outerRing != null)
        {
            outerRing.color = selected ? selectedRingColor : _normalRingColor;

            // The ring is stretched to the root, so Size Delta is twice the outset.
            outerRing.rectTransform.sizeDelta = selected
                ? new Vector2(selectedRingOutset * 2f, selectedRingOutset * 2f)
                : _normalRingSizeDelta;
        }

        if (centreDot != null)
        {
            centreDot.color = selected ? selectedDotColor : _normalDotColor;
            centreDot.rectTransform.sizeDelta = selected
                ? new Vector2(selectedDotSize, selectedDotSize)
                : _normalDotSize;
        }

        RefreshNamePill();
    }

    /// <summary>
    /// Places the hotspot at a position normalised to its parent (the map image):
    /// (0,0) is the parent's bottom-left corner and (1,1) its top-right. Values are clamped
    /// to that range. Both anchors are set to the position, so the hotspot follows the map
    /// when the map is resized or cropped.
    /// </summary>
    public void SetNormalizedPosition(Vector2 normalizedPosition)
    {
        Vector2 clamped = new Vector2(
            Mathf.Clamp01(normalizedPosition.x),
            Mathf.Clamp01(normalizedPosition.y));

        RectTransform root = (RectTransform)transform;
        root.anchorMin = clamped;
        root.anchorMax = clamped;
        root.anchoredPosition = Vector2.zero;
    }

    /// <summary>
    /// Returns where the hotspot's centre currently sits in its parent, normalised to the
    /// parent's rect ((0,0) bottom-left, (1,1) top-right, clamped). Works whatever the
    /// hotspot's anchors are, so it can be read after the hotspot has been dragged by hand.
    /// Used by the editor placement commands. Returns (0,0) if there is no RectTransform parent.
    /// </summary>
    public Vector2 ReadNormalizedPosition()
    {
        RectTransform root = (RectTransform)transform;
        RectTransform parent = root.parent as RectTransform;

        if (parent == null)
            return Vector2.zero;

        Rect parentRect = parent.rect;

        if (parentRect.width <= 0f || parentRect.height <= 0f)
            return Vector2.zero;

        // Centre of this rect in the parent's local space (pivot position plus the offset
        // from the pivot to the rect's centre).
        Vector2 centreOffset = Vector2.Scale(root.rect.center, root.localScale);
        float centreX = root.localPosition.x + centreOffset.x;
        float centreY = root.localPosition.y + centreOffset.y;

        return new Vector2(
            Mathf.Clamp01((centreX - parentRect.xMin) / parentRect.width),
            Mathf.Clamp01((centreY - parentRect.yMin) / parentRect.height));
    }

    #endregion

    private void Awake()
    {
        ValidateReferences();

        // Wire click handler
        if (selectButton != null)
            selectButton.onClick.AddListener(HandleClicked);
    }

    private void OnDisable()
    {
        // The pointer-exit event is not sent when the panel is hidden under the pointer,
        // so clear hover here. Otherwise the name pill would still be showing on reopen.
        if (_isHovered)
        {
            _isHovered = false;
            RefreshNamePill();
        }
    }

    #region Pointer Events

    /// <summary>
    /// Raised by the EventSystem when the pointer enters the hotspot's raycast target.
    /// </summary>
    public void OnPointerEnter(PointerEventData eventData)
    {
        if (!_isInitialized || _isHovered)
            return;

        _isHovered = true;
        RefreshNamePill();

        _onHoverChanged?.Invoke(this, true);
    }

    /// <summary>
    /// Raised by the EventSystem when the pointer leaves the hotspot's raycast target.
    /// </summary>
    public void OnPointerExit(PointerEventData eventData)
    {
        if (!_isInitialized || !_isHovered)
            return;

        _isHovered = false;
        RefreshNamePill();

        _onHoverChanged?.Invoke(this, false);
    }

    #endregion

    #region Private Methods

    private void HandleClicked()
    {
        if (!_isInitialized)
            return;

        _onClicked?.Invoke(this);
    }

    private void RefreshNamePill()
    {
        if (namePillRoot != null)
            namePillRoot.SetActive(_isSelected || _isHovered);
    }

    /// <summary>
    /// Reads the normal-state size and colours from the prefab values. Runs once, before the
    /// first change to any of them.
    /// </summary>
    private void CacheNormalState()
    {
        if (_normalStateCached)
            return;

        _normalSize = ((RectTransform)transform).sizeDelta;

        if (selectButton != null)
            _normalColors = selectButton.colors;

        if (outerRing != null)
        {
            _normalRingColor = outerRing.color;
            _normalRingSizeDelta = outerRing.rectTransform.sizeDelta;
        }

        if (centreDot != null)
        {
            _normalDotColor = centreDot.color;
            _normalDotSize = centreDot.rectTransform.sizeDelta;
        }

        _normalStateCached = true;
    }

    private void ValidateReferences()
    {
        if (selectButton == null)
            Debug.LogError("[MapHotspot] selectButton reference is not assigned.");
        if (outerRing == null)
            Debug.LogError("[MapHotspot] outerRing reference is not assigned.");
        if (centreDot == null)
            Debug.LogError("[MapHotspot] centreDot reference is not assigned.");
        if (namePillRoot == null)
            Debug.LogError("[MapHotspot] namePillRoot reference is not assigned.");
        if (nameText == null)
            Debug.LogError("[MapHotspot] nameText reference is not assigned.");

        Vector2 pivot = ((RectTransform)transform).pivot;

        if (!Mathf.Approximately(pivot.x, 0.5f) || !Mathf.Approximately(pivot.y, 0.5f))
            Debug.LogError("[MapHotspot] The prefab root's pivot must be (0.5, 0.5) so the hotspot is centred on its map position.");
    }

    #endregion
}
