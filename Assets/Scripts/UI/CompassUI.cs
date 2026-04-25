
using UnityEngine;

/// <summary>
/// Rotates a UI needle RectTransform to indicate panorama north relative to the current camera yaw.
/// Accounts for per-node panoramaRotation offsets applied to the skybox shader.
/// </summary>
public class CompassUI : MonoBehaviour
{
    #region Serialized Fields

    [Header("System References")]

    [Tooltip("Reference to the TourManager in the scene")]
    [SerializeField] private TourManager tourManager;

    [Tooltip("Reference to the CameraController in the scene.")]
    [SerializeField] private CameraController cameraController;

    [Header("UI References")]

    [Tooltip("The RectTransform the will be rotated to indicate north. " +
             "Should be a child with an Image component displaying the needle sprite.")]
    [SerializeField] private RectTransform needleTransform;

    #endregion

    #region Private State

    // Cached panorama rotation for the current node, updated on node change.
    private float _panoramaRotation;

    // Cached northOffset for the current tour, updated on tour change.
    private float _tourNorthOffset;

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

        tourManager.OnNodeChanged += HandleNodeChanged;
        tourManager.OnTourChanged += HandleTourChanged;

        if (tourManager.CurrentTour != null)
        {
            _tourNorthOffset = tourManager.CurrentTour.northOffset;
        }

        // Initialize from current state in case tour loaded before this component.
        if (tourManager.CurrentNode != null)
        {
            _panoramaRotation = tourManager.CurrentNode.panoramaRotation;
        }
    }

    private void OnDestroy()
    {
        if (tourManager != null)
        {
            tourManager.OnNodeChanged -= HandleNodeChanged;
            tourManager.OnTourChanged -= HandleTourChanged;
        }
    }

    private void Update()
    {
        if (!_isReady)
            return;

        UpdateNeedle();
    }

    #endregion

    #region Compass Logic

    private void UpdateNeedle()
    {
        cameraController.GetRotation(out float yaw, out float _);

        // The skybox _Rotation property shifts the cubemap clockwise by panoramaRotation degrees.
        // This means the panorama's original 0° content now appears at yaw = panoramaRotation.
        // To point the needle back toward that original north:
        //  offset = yaw - panoramaRotation (how far the camera has turned from panorama north)
        //  needleZ = -offset               (negate because UI Z-rotation is counter-clockwise positive)
        float needleZ = -(yaw - _panoramaRotation - _tourNorthOffset);

        needleTransform.localRotation = Quaternion.Euler(0f, 0f, needleZ);
    }

    #endregion

    #region Event Handlers

    private void HandleNodeChanged(NodeData node)
    {
        if (node != null)
        {
            _panoramaRotation = node.panoramaRotation;
        }
    }

    private void HandleTourChanged(TourData newTour)
    {
        if (newTour != null)
        {
            _tourNorthOffset = newTour.northOffset;
        }
    }

    #endregion

    #region Validation

    private bool ValidateReferences()
    {
        bool valid = true;

        if (tourManager == null)
        {
            Debug.LogError("[Compass UI] TourManager reference is not assigned.");
            valid = false;
        }

        if (cameraController == null)
        {
            Debug.LogError("[Compass UI] CameraController reference is not assigned.");
            valid = false;
        }

        if (needleTransform == null)
        {
            Debug.LogError("[Compass UI] needleTransform reference is not assigned.");
            valid = false;
        }

        return valid;
    }

    #endregion
}
