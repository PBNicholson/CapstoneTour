
using System;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Per-instance tour list entry behavior. Renders one TourData's collapsed
/// representation (thumbnail + name) and reports clicks via a selection
/// callback supplied by ListViewPanel during initialization
/// 
/// Configured at runtime via Initialize() - no Inspector data authoring per instance.
/// Has no knowledge of sibling entries; selection state is owned upstream by ListViewPanel,
/// which drives this entry's selected visual via SetSelected().
/// </summary>
public class TourListEntry : MonoBehaviour
{
    #region Serialized Fields

    [Header("UI References")]

    [Tooltip("Image displaying the tour's building thumbnail.")]
    [SerializeField] private Image thumbnailImage;

    [Tooltip("Text displaying the tour's building name.")]
    [SerializeField] private TextMeshProUGUI nameText;

    [Tooltip("Button that triggers selection. Typically on the entry root.")]
    [SerializeField] private Button selectButton;

    [Tooltip("GameObject toggled to indicate the selected visual state." +
             " Active when this entry is selected, inactive otherwise.")]
    [SerializeField] private GameObject selectedIndicator;

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
        if (thumbnailImage != null)
            thumbnailImage.sprite = data != null ? data.buildingThumbnail : null;
        // Default to unselected on initialization
        SetSelected(false);

        _isInitialized = true;
    }

    /// <summary>
    /// Toggles the visual selected state of this entry.
    /// Driven externally by ListViewPanel.
    /// </summary>
    /// <param name="selected">True to show the selected indicator, false to hide it.</param>
    public void SetSelected(bool selected)
    {
        if (selectedIndicator != null)
            selectedIndicator.SetActive(selected);
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
        if (selectButton == null)
            Debug.LogError("[TourListEntry] selectButton reference is not assigned.");
        if (selectedIndicator == null)
            Debug.LogError("[TourListEntry] selectedIndicator reference is not assigned.");
    }
}
