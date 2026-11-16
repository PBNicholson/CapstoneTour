using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Owns the tour list view's UI lifecycle. Builds TourListEntry instances from
/// TourCatalog, manages single-selection state, drives the detail region, and
/// raises CloseRequested (handled by MainMenuController) when the close button
/// is clicked.
///
/// Selection state is reset on every Open() call.
///
/// This is a MenuSubPanel: MainMenuController opens and closes it and hides it when
/// the scene starts. The panel does not deactivate itself in Awake, so its GameObject
/// may be saved either active or inactive in the scene.
/// </summary>
public class ListViewPanel : MenuSubPanel
{
    #region Serialized Fields

    [Header("System References")]

    [Tooltip("Tour catalog asset providing the list of tours to display.")]
    [SerializeField] private TourCatalog tourCatalog;

    [Tooltip("Reference to the TourManager in the scene. Used to start the selected tour.")]
    [SerializeField] private TourManager tourManager;

    [Header("List Region")]

    [Tooltip("Transform under which TourListEntry instances are spawned. Typically the ScrollRect's content.")]
    [SerializeField] private Transform listRegionContent;

    [Tooltip("Prefab for one tour list entry. Must have a TourListEntry component on the root.")]
    [SerializeField] private TourListEntry entryPrefab;

    [Header("Detail Region")]

    [Tooltip("GameObject shown when no entry is selected. Hidden when an entry is selected.")]
    [SerializeField] private GameObject noSelectionPlaceholder;

    [Tooltip("GameObject containing the detail region content (thumbnail, name, description, Start Tour button). " +
             "Active only when an entry is selected.")]
    [SerializeField] private GameObject selectionContainer;

    [Tooltip("Photo Image displaying the selected tour's thumbnail. Disabled when the tour has no thumbnail, " +
             "so the placeholder frame behind it shows. See TourThumbnail.")]
    [SerializeField] private Image detailThumbnailImage;

    [Tooltip("Text displaying the selected tour's name.")]
    [SerializeField] private TextMeshProUGUI detailNameText;

    [Tooltip("Text displaying the selected tour's description.")]
    [SerializeField] private TextMeshProUGUI detailDescriptionText;

    [Tooltip("ScrollRect that scrolls the description. Reset to the top whenever a different tour is selected.")]
    [SerializeField] private ScrollRect detailDescriptionScrollRect;

    [Tooltip("Button that starts the selected tour. Should live inside selectionContainer " +
             "so it's only interactable when an entry is selected.")]
    [SerializeField] private Button startTourButton;

    [Header("Panel Controls")]

    [Tooltip("Button that closes the panel. Raises CloseRequested, which MainMenuController handles.")]
    [SerializeField] private Button closeButton;

    #endregion

    private readonly List<TourListEntry> _entries = new List<TourListEntry>();
    private TourListEntry _selectedEntry;
    private bool _entriesBuilt;

    private void Awake()
    {
        ValidateReferences();

        if (startTourButton != null)
            startTourButton.onClick.AddListener(HandleStartTourClicked);

        if (closeButton != null)
            closeButton.onClick.AddListener(HandleCloseClicked);

        // No SetActive(false) here: MainMenuController hides all sub-panels in Start.
    }

    #region MenuSubPanel

    /// <summary>
    /// Runs on every Open(), just before the panel is activated. Builds list entries
    /// on the first call and clears selection state on every call.
    /// </summary>
    protected override void OnOpening()
    {
        if (!_entriesBuilt)
            BuildEntries();

        ClearSelection();
    }

    #endregion

    #region Private Methods

    private void BuildEntries()
    {
        if (tourCatalog == null || entryPrefab == null || listRegionContent == null)
        {
            _entriesBuilt = true;
            return;
        }

        for (int i = 0; i < tourCatalog.entries.Count; i++)
        {
            TourCatalogEntry catalogEntry = tourCatalog.entries[i];

            if (catalogEntry == null || catalogEntry.tourData == null)
            {
                Debug.LogWarning($"[ListViewPanel] TourCatalog entry at index {i} is null or has no TourData. Skipping.");
                continue;
            }

            TourListEntry instance = Instantiate(entryPrefab, listRegionContent);
            instance.Initialize(catalogEntry.tourData, HandleEntrySelected);
            _entries.Add(instance);
        }

        _entriesBuilt = true;
    }

    private void HandleEntrySelected(TourListEntry entry)
    {
        if (entry == null || entry == _selectedEntry)
            return;

        // Deselect previous, select new.
        if (_selectedEntry != null)
            _selectedEntry.SetSelected(false);

        _selectedEntry = entry;
        _selectedEntry.SetSelected(true);

        PopulateDetailRegion(entry.TourData);
    }

    private void PopulateDetailRegion(TourData data)
    {
        if (data == null)
        {
            ShowPlaceholder();
            return;
        }

        if (noSelectionPlaceholder != null)
            noSelectionPlaceholder.SetActive(false);

        if (selectionContainer != null)
            selectionContainer.SetActive(true);

        // Thumbnail, name, description and the description scroll reset (each tour starts at the top
        // rather than at the previous tour's scroll offset) are shared with the campus map's detail card.
        TourDetail.Apply(data, detailThumbnailImage, detailNameText, detailDescriptionText, detailDescriptionScrollRect);
    }

    private void ClearSelection()
    {
        if (_selectedEntry != null)
        {
            _selectedEntry.SetSelected(false);
            _selectedEntry = null;
        }

        ShowPlaceholder();
    }

    private void ShowPlaceholder()
    {
        if (selectionContainer != null)
            selectionContainer.SetActive(false);

        if (noSelectionPlaceholder != null)
            noSelectionPlaceholder.SetActive(true);
    }

    private void HandleStartTourClicked()
    {
        if (_selectedEntry == null || _selectedEntry.TourData == null)
        {
            // This means you did something wrong
            Debug.LogWarning("[ListViewPanel] StartTour clicked but no entry is selected.");
            return;
        }

        if (tourManager == null)
            return;

        tourManager.LoadTour(_selectedEntry.TourData);
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
            Debug.LogError("[ListViewPanel] TourCatalog reference is not assigned.");

        if (tourManager == null)
            Debug.LogError("[ListViewPanel] TourManager reference is not assigned.");

        if (listRegionContent == null)
            Debug.LogError("[ListViewPanel] ListRegionContent reference is not assigned.");

        if (entryPrefab == null)
            Debug.LogError("[ListViewPanel] EntryPrefab reference is not assigned.");

        if (noSelectionPlaceholder == null)
            Debug.LogError("[ListViewPanel] NoSelectionPlaceholder reference is not assigned.");

        if (selectionContainer == null)
            Debug.LogError("[ListViewPanel] SelectionContainer reference is not assigned.");

        if (detailThumbnailImage == null)
            Debug.LogError("[ListViewPanel] DetailThumbnailImage reference is not assigned.");

        if (detailNameText == null)
            Debug.LogError("[ListViewPanel] DetailNameText reference is not assigned.");

        if (detailDescriptionText == null)
            Debug.LogError("[ListViewPanel] DetailDescriptionText reference is not assigned.");

        if (detailDescriptionScrollRect == null)
            Debug.LogError("[ListViewPanel] DetailDescriptionScrollRect reference is not assigned.");

        if (startTourButton == null)
            Debug.LogError("[ListViewPanel] StartTourButton reference is not assigned.");

        if (closeButton == null)
            Debug.LogError("[ListViewPanel] CloseButton reference is not assigned.");
    }

    #endregion
}