
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Mediates between the vertical zoom Slider and CameraController's FOV API.
/// Slider top = minimum FOV (maximum zoom). Slider bottom = maxiumum FOV (minimum zoom).
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

    [Tooltip("The Slider component that controls zoom. Should be configured as vertical, Bottom To Top.")]
    [SerializeField] private Slider slider;

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

    /// <summary>
    /// Converts a slider value to the corresponding FOV.
    /// Inverts the range so the top of the slider maps to the minimum FOV.
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

        return valid;
    }

    #endregion

}
