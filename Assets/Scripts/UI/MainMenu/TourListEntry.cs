using System;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Per-instance tour list entry behavior. Renders one TourData's collapsed
/// representation (thumbnail + name + optional caption) and reports clicks via a
/// selection callback supplied by ListViewPanel during initialization
///
/// Configured at runtime via Initialize() - no Inspector data authoring per instance.
/// Has no knowledge of sibling entries; selection state is owned upstream by ListViewPanel,
/// which drives this entry's selected visual via SetSelected().
///
/// Selected visual: the selectedIndicator object is shown (full-row fill and bar, placed
/// under the text) and the name and caption switch to their selected colours. The Button's
/// colour tint cannot recolour the labels, so that swap is done here.
/// </summary>
public class TourListEntry : MonoBehaviour
{
    #region Serialized Fields

    [Header("UI References")]

    [Tooltip("Photo Image displaying the tour's building thumbnail. Disabled when the tour has no thumbnail, " +
             "so the placeholder frame behind it shows. See TourThumbnail.")]
    [SerializeField] private Image thumbnailImage;

    [Tooltip("Text displaying the tour's building name.")]
    [SerializeField] private TextMeshProUGUI nameText;

    [Tooltip("Text displaying the tour's caption under the name. Its GameObject is hidden when the tour has no caption.")]
    [SerializeField] private TextMeshProUGUI captionText;

    [Tooltip("Button that triggers selection. Typically on the entry root.")]
    [SerializeField] private Button selectButton;

    [Tooltip("GameObject toggled to indicate the selected visual state." +
             " Active when this entry is selected, inactive otherwise.")]
    [SerializeField] private GameObject selectedIndicator;

    [Header("Text Colours")]

    [Tooltip("Name colour when the entry is not selected. Style spec: Ink #323232.")]
    [SerializeField] private Color nameColor = new Color32(0x32, 0x32, 0x32, 0xFF);

    [Tooltip("Caption colour when the entry is not selected. Style spec: Muted #55616B.")]
    [SerializeField] private Color captionColor = new Color32(0x55, 0x61, 0x6B, 0xFF);

    [Tooltip("Name colour when the entry is selected. Style spec: White #FFFFFF.")]
    [SerializeField] private Color selectedNameColor = new Color32(0xFF, 0xFF, 0xFF, 0xFF);

    [Tooltip("Caption colour when the entry is selected. Style spec: Gray #CFD4D8.")]
    [SerializeField] private Color selectedCaptionColor = new Color32(0xCF, 0xD4, 0xD8, 0xFF);

    #endregion

    private TourData _data;
    private Action<TourListEntry> _onSelected;
    private bool _isInitialized;

    #region Public API

    /// <summary>
    /// The TourData this entry represents. Null until Initialize has been called.
    /// </summary>
    public TourData TourData => _data;

    /// <summary>
    /// Configures the entry with its tour data and selection callback.
    /// Called by ListViewPanel immediately after instantiation.
    /// </summary>
    /// <param name="data">The TourData this entry represents</param>
    /// <param name="onSelected">
    /// Callback invoked when the user clicks this entry. Receives this entry
    /// instance as the argument so the panel can resolve which entry was clicked.
    /// </param>
    public void Initialize(TourData data, Action<TourListEntry> onSelected)
    {
        _data = data;
        _onSelected = onSelected;

        if (nameText != null)
            nameText.text = data != null ? data.buildingName : string.Empty;

        // Caption line is hidden when empty so the layout can centre the name on its own.
        string caption = data != null ? data.caption : null;
        bool hasCaption = !string.IsNullOrWhiteSpace(caption);

        if (captionText != null)
        {
            captionText.text = hasCaption ? caption : string.Empty;
            captionText.gameObject.SetActive(hasCaption);
        }

        TourThumbnail.Apply(thumbnailImage, data != null ? data.buildingThumbnail : null);

        // Default to unselected on initialization
        SetSelected(false);

        _isInitialized = true;
    }

    /// <summary>
    /// Toggles the visual selected state of this entry.
    /// Driven externally by ListViewPanel.
    /// </summary>
    /// <param name="selected">True to show the selected indicator and selected text colours, false for the normal state.</param>
    public void SetSelected(bool selected)
    {
        if (selectedIndicator != null)
            selectedIndicator.SetActive(selected);

        if (nameText != null)
            nameText.color = selected ? selectedNameColor : nameColor;

        if (captionText != null)
            captionText.color = selected ? selectedCaptionColor : captionColor;
    }

    #endregion

    private void Awake()
    {
        ValidateReferences();

        // Wire click handler
        if (selectButton != null)
            selectButton.onClick.AddListener(HandleClicked);
    }

    private void HandleClicked()
    {
        if (!_isInitialized)
            return;

        _onSelected?.Invoke(this);
    }

    private void ValidateReferences()
    {
        if (thumbnailImage == null)
            Debug.LogError("[TourListEntry] ThumbnailImage reference is not assigned.");
        if (nameText == null)
            Debug.LogError("[TourListEntry] nameText reference is not assigned.");
        if (captionText == null)
            Debug.LogError("[TourListEntry] captionText reference is not assigned.");
        if (selectButton == null)
            Debug.LogError("[TourListEntry] selectButton reference is not assigned.");
        if (selectedIndicator == null)
            Debug.LogError("[TourListEntry] selectedIndicator reference is not assigned.");
    }
}