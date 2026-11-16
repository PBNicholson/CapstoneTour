using System;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;
using TMPro;

/// <summary>
/// Manages the POI info panel's content population, open/close state, the close button
/// in its header, and Addressable image loading and release. Owned by a POIMarker instance.
/// The panel GameObject is hidden by Initialize and activates on demand.
///
/// Image: the POIImage sits inside a frame (Gray Image plus RectMask2D) and has an
/// AspectRatioFitter set to Envelope Parent, so the loaded sprite covers the frame and is
/// cropped (TourThumbnail.Apply). The frame (ImageContainer) is shown only once a sprite
/// has loaded.
/// </summary>
public class POIInfoPanel : MonoBehaviour
{
    #region Serialized Fields

    [Header("Content References")]

    [Tooltip("Text element displaying the POI label in the header.")]
    [SerializeField] private TextMeshProUGUI labelText;

    [Tooltip("Text element displaying the POI description body.")]
    [SerializeField] private TextMeshProUGUI descriptionText;

    [Tooltip("Photo Image that receives the Addressable sprite at runtime. " +
             "Needs an AspectRatioFitter set to Envelope Parent.")]
    [SerializeField] private Image poiImage;

    [Tooltip("Frame GameObject around the POI image. Disabled when no image is assigned or loaded.")]
    [SerializeField] private GameObject imageContainer;

    [Header("Controls")]

    [Tooltip("Close button in the panel header. Closes the panel.")]
    [SerializeField] private Button closeButton;

    #endregion

    #region Events

    /// <summary>
    /// Raised when the panel opens (true) or closes (false). Not raised when Open or Close
    /// is called on a panel that is already in that state.
    /// </summary>
    public event Action<bool> OpenStateChanged;

    #endregion

    #region Private State

    private POIData _data;
    private AsyncOperationHandle<Sprite> _imageHandle;
    private bool _hasImageHandle;
    private bool _isOpen;
    private bool _isInitialized;

    #endregion

    #region Public API

    /// <summary>
    /// True while the panel is showing.
    /// </summary>
    public bool IsOpen => _isOpen;

    public void Initialize(POIData data)
    {
        _data = data;

        if (labelText != null)
            labelText.text = data.label;

        if (descriptionText != null)
            descriptionText.text = data.description;

        // Image container starts hidden - enabled only after successful load
        if (imageContainer != null)
            imageContainer.SetActive(false);

        if (closeButton != null)
        {
            closeButton.onClick.RemoveListener(Close);
            closeButton.onClick.AddListener(Close);
        }

        _isInitialized = true;
    }

    /// <summary>
    /// Toggles the panel between open and closed states.
    /// </summary>
    public void Toggle()
    {
        if (!_isInitialized)
            return;

        if (_isOpen)
            Close();
        else
            Open();
    }

    /// <summary>
    /// Opens the panel and begins loading the image if one is assigned.
    /// </summary>
    public void Open()
    {
        if (!_isInitialized)
            return;

        bool wasOpen = _isOpen;

        gameObject.SetActive(true);
        _isOpen = true;

        LoadImage();

        if (!wasOpen)
            OpenStateChanged?.Invoke(true);
    }

    /// <summary>
    /// Closes the panel and releases any held Addressable image handle.
    /// Safe to call when already closed or not yet initialized.
    /// </summary>
    public void Close()
    {
        bool wasOpen = _isOpen;

        _isOpen = false;
        gameObject.SetActive(false);

        ReleaseImage();

        if (wasOpen)
            OpenStateChanged?.Invoke(false);
    }

    #endregion

    #region Private Methods

    private void LoadImage()
    {
        // Nothing to load if no image reference on the data
        if (_data == null || _data.image == null || !_data.image.RuntimeKeyIsValid())
        {
            if (imageContainer != null)
                imageContainer.SetActive(false);
            return;
        }

        // Already loaded from a previous open - just show it
        if (_hasImageHandle && _imageHandle.IsValid() && _imageHandle.Status == AsyncOperationStatus.Succeeded)
        {
            if (imageContainer != null)
                imageContainer.SetActive(true);
            return;
        }

        // Release any stale handle before starting a new load
        ReleaseImage();

        _imageHandle = _data.image.LoadAssetAsync<Sprite>();
        _hasImageHandle = true;
        _imageHandle.Completed += OnImageLoadCompleted;
    }

    private void OnImageLoadCompleted(AsyncOperationHandle<Sprite> handle)
    {
        // Panel was closed or marker destroyed before load finished
        if (!_hasImageHandle || !_isOpen)
        {
            if (_hasImageHandle)
                ReleaseImage();
            return;
        }

        if (handle.Status != AsyncOperationStatus.Succeeded)
        {
            string label = _data != null ? _data.label : "unknown";
            Debug.LogWarning($"[POIInfoPanel] Failed to load image for POI '{label}': {handle.OperationException?.Message}");
            ReleaseImage();
            return;
        }

        // Assigns the sprite and sets the fitter's aspect ratio so the photo covers the frame.
        TourThumbnail.Apply(poiImage, handle.Result);

        if (imageContainer != null)
            imageContainer.SetActive(true);
    }

    private void ReleaseImage()
    {
        if (!_hasImageHandle)
            return;

        // Clear the sprite reference before releasing the handle
        TourThumbnail.Apply(poiImage, null);

        if (imageContainer != null)
            imageContainer.SetActive(false);

        Addressables.Release(_imageHandle);
        _hasImageHandle = false;
    }

    #endregion

    #region Validation

    private void Awake()
    {
        ValidateReferences();
    }

    private void ValidateReferences()
    {
        if (labelText == null)
            Debug.LogError("[POIInfoPanel] LabelText reference is not assigned.");

        if (descriptionText == null)
            Debug.LogError("[POIInfoPanel] DescriptionText reference is not assigned.");

        if (poiImage == null)
            Debug.LogError("[POIInfoPanel] POIImage reference is not assigned.");

        if (imageContainer == null)
            Debug.LogError("[POIInfoPanel] ImageContainer reference is not assigned.");

        if (closeButton == null)
            Debug.LogError("[POIInfoPanel] CloseButton reference is not assigned.");
    }

    #endregion
}