using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Mediates between the horizontal zoom Slider, its plus and minus step buttons, and
/// CameraController's FOV API.
///
/// The Slider is horizontal, Left To Right. Its range is set to [minFov, maxFov] at start and
/// the value is inverted, so the right end = minimum FOV (maximum zoom) and the left end =
/// maximum FOV (minimum zoom). The fill therefore grows as the view zooms in.
///
/// The plus button (right of the track) zooms in and the minus button (left of the track)
/// zooms out, each by stepFraction of the full FOV range per click, clamped to the range.
///
/// FOV resets to default on every node navigation.
/// </summary>
public class ZoomSliderUI : MonoBehaviour
{
    #region Serialized Fields

    [Header("System References")]

    [Tooltip("Reference to the CameraController in the scene.")]
    [SerializeField] private CameraController cameraController;

    [Tooltip("Reference to the TourManager in the scene. Used to subscribe to node change events.")]
    [SerializeField] private TourManager tourManager;

    [Header("UI References")]

    [Tooltip("The Slider component that controls zoom. Should be horizontal, Left To Right " +
             "(right end = zoomed in).")]
    [SerializeField] private Slider slider;

    [Tooltip("Plus button, right of the track. Zooms in by one step.")]
    [SerializeField] private Button zoomInButton;

    [Tooltip("Minus button, left of the track. Zooms out by one step.")]
    [SerializeField] private Button zoomOutButton;

    [Header("Steps")]

    [Tooltip("Size of one plus/minus step as a fraction of the full FOV range (0.1 = 10%).")]
    [Range(0.01f, 1f)]
    [SerializeField] private float stepFraction = 0.1f;

    #endregion

    #region Private State

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

        InitializeSlider();
        InitializeButtons();

        tourManager.OnNodeChanged += HandleNodeChanged;
    }

    private void OnDestroy()
    {
        if (tourManager != null)
            tourManager.OnNodeChanged -= HandleNodeChanged;
    }

    #endregion

    #region Initialization

    private void InitializeSlider()
    {
        cameraController.GetFovRange(out float minFov, out float maxFov);

        slider.minValue = minFov;
        slider.maxValue = maxFov;

        SetSliderToFov(cameraController.GetFov());

        slider.onValueChanged.AddListener(HandleSliderChanged);
    }

    private void InitializeButtons()
    {
        if (zoomInButton != null)
            zoomInButton.onClick.AddListener(HandleZoomInClicked);

        if (zoomOutButton != null)
            zoomOutButton.onClick.AddListener(HandleZoomOutClicked);
    }

    #endregion

    #region Slider Logic

    private void HandleSliderChanged(float sliderValue)
    {
        cameraController.SetFov(SliderToFov(sliderValue));
    }

    private void HandleNodeChanged(NodeData node)
    {
        cameraController.ResetFov();

        SetSliderToFov(cameraController.GetFov());
    }

    private void HandleZoomInClicked()
    {
        StepFov(-1f);
    }

    private void HandleZoomOutClicked()
    {
        StepFov(1f);
    }

    /// <summary>
    /// Moves the FOV by one step (stepFraction of the full range) in the given direction:
    /// -1 narrows the FOV (zoom in), +1 widens it (zoom out). Moving the slider raises
    /// onValueChanged, which applies the FOV to the camera. At either end of the range
    /// the value does not change and nothing happens.
    /// </summary>
    private void StepFov(float direction)
    {
        float step = (slider.maxValue - slider.minValue) * stepFraction;
        float targetFov = Mathf.Clamp(cameraController.GetFov() + direction * step, slider.minValue, slider.maxValue);

        slider.value = SliderToFov(targetFov);
    }

    /// <summary>
    /// Converts a slider value to the corresponding FOV (and, being its own inverse, a FOV
    /// to the corresponding slider value). Inverts the range so the right end of the slider
    /// maps to the minimum FOV.
    /// </summary>
    private float SliderToFov(float sliderValue)
    {
        return slider.maxValue + slider.minValue - sliderValue;
    }

    /// <summary>
    /// Moves the slider to the position that represents the given FOV,
    /// without triggering the onValueChanged callback.
    /// </summary>
    private void SetSliderToFov(float fov)
    {
        slider.SetValueWithoutNotify(SliderToFov(fov));
    }

    #endregion

    #region Validation

    private bool ValidateReferences()
    {
        bool valid = true;

        if (cameraController == null)
        {
            Debug.LogError("[ZoomSliderUI] CameraController reference is not assigned.");
            valid = false;
        }

        if (tourManager == null)
        {
            Debug.LogError("[ZoomSliderUI] TourManager reference is not assigned.");
            valid = false;
        }

        if (slider == null)
        {
            Debug.LogError("[ZoomSliderUI] Slider reference is not assigned.");
            valid = false;
        }

        // The step buttons are optional for the slider to work, so their absence is a warning.
        if (zoomInButton == null)
            Debug.LogWarning("[ZoomSliderUI] ZoomInButton reference is not assigned.");

        if (zoomOutButton == null)
            Debug.LogWarning("[ZoomSliderUI] ZoomOutButton reference is not assigned.");

        return valid;
    }

    #endregion

}