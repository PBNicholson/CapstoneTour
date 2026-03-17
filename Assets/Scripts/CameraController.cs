
using System.Runtime.CompilerServices;
using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>
/// Controls panorama camera rotation via left-click drag and exposes
/// FOV and rotation API for external systems.
/// </summary>
[RequireComponent(typeof(Camera))]
public class CameraController : MonoBehaviour
{
    #region Serialized Fields

    [Header ("Rotation")]

    [Tooltip("Degrees per pixel of mouse movement.")]
    [SerializeField] public float rotationSpeed = 5.0f;

    [Tooltip("Lower bound for vertical (pitch) rotation in degrees.")]
    [SerializeField] public float minVerticalAngle = -80f;

    [Tooltip("Upper bound for vertical (pitch) rotation in degrees")]
    [SerializeField] public float maxVerticalAngle = 80f;

    [Header("Horizontal Clamp")]

    [Tooltip("When enabled, yaw is restricted to [minHorizontalAngle, maxHorizontalAngle]")]
    [SerializeField] public bool useHorizontalClamp = false;

    [Tooltip("Lower bound for horizontal (yaw) rotation in degrees. Only applied when useHorizontalClamp is enabled")]
    [SerializeField] public float minHorizontalAngle = -45f;

    [Tooltip("Upper bound for horizontal (yaw) rotation in degrees. Only applied when useHorizontalClamp is enabled")]
    [SerializeField] public float maxHorizontalAngle = 45f;

    [Header("FOV")]

    [Tooltip("Minimum field of view in degrees (maximum zoom).")]
    [SerializeField] private float minFov = 30f;

    [Tooltip("Maximum field of view in degrees (minimum zoom).")]
    [SerializeField] private float maxFov = 90f;

    [Tooltip("Field of view applied on start and on each node navigation reset.")]
    [SerializeField] private float defaultFov = 60f;

    #endregion

    #region Private State

    private Camera _camera;
    private Vector3 _lastMousePosition;
    private float _yaw;
    private float _pitch;
    private bool _isReady;
    private bool _isDragging;

    #endregion

    #region Unity Lifecycle

    private void Awake()
    {
        _camera = GetComponent<Camera>();
        _isReady = ValidateReferences();
    }

    private void Start()
    {
        if (!_isReady)
            return;

        ResetFov();
    }

    private void Update()
    {
        if (!_isReady)
            return;
        HandleRotationInput();
    }

    #endregion

    #region Input Handling

    private void HandleRotationInput()
    {
        
        if (Input.GetMouseButtonDown(0))
        {
            if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject())
                return;

            _lastMousePosition = Input.mousePosition;
            _isDragging = true;
        }

        if (Input.GetMouseButtonUp(0))
        {
            _isDragging = false;
        }

        if (_isDragging && Input.GetMouseButton(0))
        {
            Vector3 delta = Input.mousePosition - _lastMousePosition;

            float yawDelta = delta.x * rotationSpeed * Time.deltaTime;
            float pitchDelta = -delta.y * rotationSpeed * Time.deltaTime;

            ApplyRotation(_yaw + yawDelta, _pitch + pitchDelta);

            _lastMousePosition = Input.mousePosition;
        }
    }

    #endregion

    #region Public API

    /// <summary>
    /// Sets the camera rotation to the specified yaw and pitch values.
    /// Pitch is clamped to configured vertical angle limits.
    /// Yaw is clamped to horizontal limits only if useHorizontalClamp is enabled.
    /// </summary>
    /// <param name="yaw">Horizontal rotation in degrees.</param>
    /// <param name="pitch">Vertical rotation in degrees (Positive = looking down).</param>
    public void SetRotation(float yaw, float pitch)
    {
        ApplyRotation(yaw, pitch);
    }

    /// <summary>
    /// Gets the current camera rotation values.
    /// </summary>
    /// <param name="yaw">Current horizontal rotation in degrees.</param>
    /// <param name="pitch">Current vertical rotation in degrees.</param>
    public void GetRotation(out float yaw, out float pitch)
    {
        yaw = _yaw;
        pitch = _pitch;
    }

    /// <summary>
    /// Sets the camera field of view, clamped to [minFov, maxFov].
    /// </summary>
    /// <param name="fov">target FOV in degrees</param>
    public void SetFov(float fov)
    {
        if (_camera == null)
            return;

        _camera.fieldOfView = Mathf.Clamp(fov, minFov, maxFov);
    }

    /// <summary>
    /// Returns the camera's current field of view in degrees.
    /// </summary>
    /// <returns></returns>
    public float GetFov()
    {
        if (_camera == null)
            return defaultFov;

        return _camera.fieldOfView;
    }

    /// <summary>
    /// Resets the camera field of view to defaultFov
    /// </summary>
    public void ResetFov()
    {
        SetFov(defaultFov);
    }

    /// <summary>
    /// Exposes the configured FOV range for external systems (e.g. zoom slider)
    /// </summary>
    /// <param name="min">Minimum FOV in degrees.</param>
    /// <param name="max">Maximum FOV in degrees.</param>
    public void GetFovRange(out float min, out float max)
    {
        min = minFov;
        max = maxFov;
    }

    #endregion

    #region Private Methods

    /// <summary>
    /// Applies clamping and writes yaw/pitch to both the backing fields and the transform.
    /// All rotation changes funnel through here.
    /// </summary>
    private void ApplyRotation(float yaw, float pitch)
    {
        _yaw = useHorizontalClamp
            ? Mathf.Clamp(yaw, minHorizontalAngle, maxHorizontalAngle)
            : yaw;

        _pitch = Mathf.Clamp(pitch, minVerticalAngle, maxVerticalAngle);

        transform.rotation = Quaternion.Euler(_pitch, _yaw, 0f);
    }

    private bool ValidateReferences()
    {
        if(_camera == null)
        {
            Debug.LogError("[CameraController] No Camera component found on this GameObject.");
            return false;
        }

        return true;
    }

    #endregion
}