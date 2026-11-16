using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Owns the campus map view's UI lifecycle. Shows TourCatalog.campusMap, builds one
/// MapHotspot on the map for every TourCatalog entry that has a map position, manages
/// single-selection state, drives the detail card, and raises CloseRequested (handled by
/// MainMenuController) when the panel's close button is clicked.
///
/// Selection: clicking a hotspot selects it and shows the detail card. The selection is
/// cleared, and the card hidden, by clicking the selected hotspot again, clicking empty map,
/// or clicking the card's close button. Selection state is reset on every Open() call.
///
/// Hotspot positions come from TourCatalogEntry.mapHotspotPosition, normalised to the map
/// image ((0,0) bottom-left, (1,1) top-right). An entry left at (0,0), the field's default,
/// has not been placed and gets no hotspot; its tour is still listed in the tour list.
/// Hotspots are children of the map Image, whose
/// AspectRatioFitter (Envelope Parent) makes it cover the map area, so they stay on their
/// buildings when the map is cropped at other aspect ratios.
///
/// Draw order: hotspots are siblings under the map Image. The hovered hotspot is moved to
/// the front so its name pill is not covered by neighbours; when the pointer leaves, the
/// selected hotspot is moved back to the front. The detail card is a later sibling of the
/// map Image, so it always draws over every hotspot.
///
/// Placement: positions are set in Edit mode with the "Placement" commands in this
/// component's context menu (CampusMapHotspotPlacement, editor only). Hotspot objects left
/// under the map Image by those commands are removed here when the hotspots are built, so
/// they can never appear twice at runtime.
///
/// This is a MenuSubPanel: MainMenuController opens and closes it and hides it when the
/// scene starts. The panel does not deactivate itself in Awake, so its GameObject may be
/// saved either active or inactive in the scene.
/// </summary>
public class CampusMapPanel : MenuSubPanel
{
    #region Serialized Fields

    [Header("System References")]

    [Tooltip("Tour catalog asset providing the campus map image, the tours and their hotspot positions.")]
    [SerializeField] private TourCatalog tourCatalog;

    [Tooltip("Reference to the TourManager in the scene. Used to start the selected tour.")]
    [SerializeField] private TourManager tourManager;

    [Header("Map")]

    [Tooltip("Image that displays the campus map. Needs an AspectRatioFitter set to Envelope Parent. " +
             "Hotspots are spawned as its children. Its sprite is set from TourCatalog.campusMap at runtime.")]
    [SerializeField] private Image mapImage;

    [Tooltip("Button on the map Image (Transition None). Clicking empty map clears the selection.")]
    [SerializeField] private Button mapBackgroundButton;

    [Tooltip("Prefab for one map hotspot. Must have a MapHotspot component on the root.")]
    [SerializeField] private MapHotspot hotspotPrefab;

    [Header("Detail Card")]

    [Tooltip("Root GameObject of the detail card. Active only while a hotspot is selected.")]
    [SerializeField] private GameObject detailCard;

    [Tooltip("Photo Image displaying the selected tour's thumbnail. Disabled when the tour has no thumbnail, " +
             "so the placeholder frame behind it shows. See TourThumbnail.")]
    [SerializeField] private Image detailThumbnailImage;

    [Tooltip("Text displaying the selected tour's name.")]
    [SerializeField] private TextMeshProUGUI detailNameText;

    [Tooltip("Text displaying the selected tour's description.")]
    [SerializeField] private TextMeshProUGUI detailDescriptionText;

    [Tooltip("ScrollRect that scrolls the description. Reset to the top whenever a different tour is selected.")]
    [SerializeField] private ScrollRect detailDescriptionScrollRect;

    [Tooltip("Button on the card that starts the selected tour.")]
    [SerializeField] private Button startTourButton;

    [Tooltip("Close button on the card. Clears the selection and hides the card; the panel stays open.")]
    [SerializeField] private Button cardCloseButton;

    [Header("Panel Controls")]

    [Tooltip("Button that closes the panel. Raises CloseRequested, which MainMenuController handles.")]
    [SerializeField] private Button closeButton;

    #endregion

    private readonly List<MapHotspot> _hotspots = new List<MapHotspot>();
    private MapHotspot _selectedHotspot;
    private bool _hotspotsBuilt;

    private void Awake()
    {
        ValidateReferences();

        if (startTourButton != null)
            startTourButton.onClick.AddListener(HandleStartTourClicked);

        if (cardCloseButton != null)
            cardCloseButton.onClick.AddListener(ClearSelection);

        if (mapBackgroundButton != null)
            mapBackgroundButton.onClick.AddListener(ClearSelection);

        if (closeButton != null)
            closeButton.onClick.AddListener(HandleCloseClicked);

        // No SetActive(false) here: MainMenuController hides all sub-panels in Start.
    }

    #region MenuSubPanel

    /// <summary>
    /// Runs on every Open(), just before the panel is activated. Builds the hotspots on the
    /// first call and clears selection state on every call. On the first open of a panel
    /// saved inactive this runs before Awake, so it uses serialized references only.
    /// </summary>
    protected override void OnOpening()
    {
        if (!_hotspotsBuilt)
            BuildHotspots();

        ClearSelection();
    }

    #endregion

    #region Private Methods

    private void BuildHotspots()
    {
        _hotspotsBuilt = true;

        if (tourCatalog == null || hotspotPrefab == null || mapImage == null)
            return;

        ApplyMapSprite();
        RemoveSavedHotspots();

        RectTransform mapRect = mapImage.rectTransform;
        List<string> unplaced = new List<string>();

        for (int i = 0; i < tourCatalog.entries.Count; i++)
        {
            TourCatalogEntry catalogEntry = tourCatalog.entries[i];

            if (catalogEntry == null || catalogEntry.tourData == null)
            {
                Debug.LogWarning($"[CampusMapPanel] TourCatalog entry at index {i} is null or has no TourData. Skipping.");
                continue;
            }

            // (0,0) is the field's default and means the tour has not been placed on the map.
            // It gets no hotspot.
            if (catalogEntry.mapHotspotPosition == Vector2.zero)
            {
                unplaced.Add(catalogEntry.tourData.name);
                continue;
            }

            MapHotspot instance = Instantiate(hotspotPrefab, mapRect);
            instance.Initialize(
                catalogEntry.tourData,
                catalogEntry.mapHotspotPosition,
                HandleHotspotClicked,
                HandleHotspotHoverChanged);
            _hotspots.Add(instance);
        }

        if (unplaced.Count > 0)
        {
            Debug.Log("[CampusMapPanel] These tours have no map hotspot position (0,0) and are not shown on the map: " +
                      string.Join(", ", unplaced.ToArray()) +
                      ". To add them, use the Placement commands in the CampusMapPanel context menu.");
        }
    }

    /// <summary>
    /// Shows TourCatalog.campusMap in the map Image and sets the fitter's aspect ratio from it.
    /// If the catalog has no map, the Image is left as it was saved in the scene.
    /// </summary>
    private void ApplyMapSprite()
    {
        if (tourCatalog.campusMap == null)
        {
            Debug.LogWarning("[CampusMapPanel] TourCatalog.campusMap is not assigned. The map Image is left as saved in the scene.");
            return;
        }

        // Same rule as tour thumbnails: assign the sprite and set the AspectRatioFitter's
        // ratio from it, so the image covers its parent and the parent's RectMask2D crops it.
        TourThumbnail.Apply(mapImage, tourCatalog.campusMap);
    }

    /// <summary>
    /// Destroys MapHotspot objects that were saved under the map Image, which happens when the
    /// editor placement hotspots are not removed before the scene is saved.
    /// </summary>
    private void RemoveSavedHotspots()
    {
        MapHotspot[] saved = mapImage.GetComponentsInChildren<MapHotspot>(true);

        if (saved.Length == 0)
            return;

        for (int i = 0; i < saved.Length; i++)
        {
            // Hidden at once; Destroy itself only takes effect at the end of the frame.
            saved[i].gameObject.SetActive(false);
            Destroy(saved[i].gameObject);
        }

        Debug.LogWarning($"[CampusMapPanel] Removed {saved.Length} hotspot object(s) saved under the map Image. " +
                         "They are placement leftovers: run 'Placement 3 - Remove placement hotspots' " +
                         "from the CampusMapPanel context menu and save the scene.");
    }

    private void HandleHotspotClicked(MapHotspot hotspot)
    {
        if (hotspot == null)
            return;

        // Clicking the selected hotspot again clears the selection and hides the card.
        if (hotspot == _selectedHotspot)
        {
            ClearSelection();
            return;
        }

        // Deselect previous, select new.
        if (_selectedHotspot != null)
            _selectedHotspot.SetSelected(false);

        _selectedHotspot = hotspot;
        _selectedHotspot.SetSelected(true);
        _selectedHotspot.transform.SetAsLastSibling();

        ShowCard(hotspot.TourData);
    }

    private void HandleHotspotHoverChanged(MapHotspot hotspot, bool hovered)
    {
        if (hovered)
        {
            // Bring the hovered hotspot to the front so its name pill is not covered.
            if (hotspot != null)
                hotspot.transform.SetAsLastSibling();
        }
        else if (_selectedHotspot != null)
        {
            // Hover ended: the selected hotspot and its pill go back on top.
            _selectedHotspot.transform.SetAsLastSibling();
        }
    }

    private void ShowCard(TourData data)
    {
        if (detailCard != null)
            detailCard.SetActive(true);

        // Thumbnail, name, description and scroll reset are shared with the tour list's detail region.
        TourDetail.Apply(data, detailThumbnailImage, detailNameText, detailDescriptionText, detailDescriptionScrollRect);
    }

    /// <summary>
    /// Deselects the selected hotspot, if any, and hides the detail card.
    /// Also the handler for the card's close button and for clicks on empty map.
    /// </summary>
    private void ClearSelection()
    {
        if (_selectedHotspot != null)
        {
            _selectedHotspot.SetSelected(false);
            _selectedHotspot = null;
        }

        if (detailCard != null)
            detailCard.SetActive(false);
    }

    private void HandleStartTourClicked()
    {
        if (_selectedHotspot == null || _selectedHotspot.TourData == null)
        {
            // The card is only visible while a hotspot is selected, so this should not happen.
            Debug.LogWarning("[CampusMapPanel] StartTour clicked but no hotspot is selected.");
            return;
        }

        if (tourManager == null)
            return;

        tourManager.LoadTour(_selectedHotspot.TourData);
    }

    private void HandleCloseClicked()
    {
        RequestClose();
    }

    #endregion

    #region Validation

    private void ValidateReferences()
    {
        if (tourCatalog == null)
            Debug.LogError("[CampusMapPanel] TourCatalog reference is not assigned.");

        if (tourManager == null)
            Debug.LogError("[CampusMapPanel] TourManager reference is not assigned.");

        if (mapImage == null)
            Debug.LogError("[CampusMapPanel] MapImage reference is not assigned.");
        else if (mapImage.GetComponent<AspectRatioFitter>() == null)
            Debug.LogError("[CampusMapPanel] MapImage has no AspectRatioFitter. Add one set to Envelope Parent.");

        if (mapBackgroundButton == null)
            Debug.LogError("[CampusMapPanel] MapBackgroundButton reference is not assigned.");

        if (hotspotPrefab == null)
            Debug.LogError("[CampusMapPanel] HotspotPrefab reference is not assigned.");

        if (detailCard == null)
            Debug.LogError("[CampusMapPanel] DetailCard reference is not assigned.");

        if (detailThumbnailImage == null)
            Debug.LogError("[CampusMapPanel] DetailThumbnailImage reference is not assigned.");

        if (detailNameText == null)
            Debug.LogError("[CampusMapPanel] DetailNameText reference is not assigned.");

        if (detailDescriptionText == null)
            Debug.LogError("[CampusMapPanel] DetailDescriptionText reference is not assigned.");

        if (detailDescriptionScrollRect == null)
            Debug.LogError("[CampusMapPanel] DetailDescriptionScrollRect reference is not assigned.");

        if (startTourButton == null)
            Debug.LogError("[CampusMapPanel] StartTourButton reference is not assigned.");

        if (cardCloseButton == null)
            Debug.LogError("[CampusMapPanel] CardCloseButton reference is not assigned.");

        if (closeButton == null)
            Debug.LogError("[CampusMapPanel] CloseButton reference is not assigned.");
    }

    #endregion
}