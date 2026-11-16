using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Displays a reticle that tracks the mouse cursor on the TourGeometry collider.
/// Provides visual feedback for where a spatial navigation right-click raycast would land.
///
/// The reticle is a world-space SpriteRenderer. Perspective projection naturally handles
/// apparent size (closer = larger) and foreshortening (angled surfaces show the circle as
/// an ellipse).
///
/// Size: reticleRadius is half the width of the sprite's full extent, in world units
/// (1 unit = 1 foot). The scale is computed from the sprite's own size and the parent's
/// scale, so the world size does not depend on the sprite's pixels per unit or on the
/// parent (the reticle sits under OverlayCanvas, whose scale follows the screen size).
///
/// A raycast miss is a no-op: the reticle stays where it last landed.
///
/// Raycasts independently from SpatialNavigator.
/// Disable this component to stop the reticle updating without affecting navigation.
/// </summary>
public class NavigationReticle : MonoBehaviour
{
    #region Serialized Fields

    [Header("Raycast")]

    [Tooltip("Layer mask targeting the TourGeometry layer. Should match SpatialNavigator's layer mask.")]
    [SerializeField] private LayerMask tourGeometryMask;

    [Tooltip("Maximum raycast distance. Should exceed the largest InvertedBox dimension.")]
    [SerializeField] private float maxRaycastDistance = 50f;

    [Header("Scaling")]

    [Tooltip("World-space radius of the reticle in feet: half the width of the sprite's full extent. " +
             "Apparent screen size is determined by perspective.")]
    [SerializeField] private float reticleRadius = 0.3f;

    [Tooltip("Small offset along the surface normal to prevent z-fighting with the collider geometry.")]
    [SerializeField] private float surfaceOffset = 0.01f;

    [Header("References")]

    [Tooltip("Transform of the reticle GameObject (must have a SpriteRenderer). " +
             "Positioned, rotated and scaled by this script each frame.")]
    [SerializeField] private Transform reticleTransform;

    [Tooltip("Camera used for raycasting. Falls back to Camera.main if not assigned.")]
    [SerializeField] private Camera sourceCamera;

    #endregion

    #region Private State

    private Camera _camera;
    private SpriteRenderer _spriteRenderer;
    private bool _isReady;

    #endregion

    #region Unity Lifecycle

    private void Awake()
    {
        _camera = sourceCamera != null ? sourceCamera : Camera.main;

        if (reticleTransform != null)
            _spriteRenderer = reticleTransform.GetComponent<SpriteRenderer>();

        _isReady = ValidateReferences();

        // Mirror SpatialNavigator's auto-resolve pattern for the layer mask
        if (_isReady && tourGeometryMask.value == 0)
        {
            int layerIndex = LayerMask.NameToLayer("TourGeometry");

            if (layerIndex == -1)
            {
                Debug.LogError("[NavigationReticle] 'TourGeometry' layer does not exist. " +
                               "Create it in Project Settings > Tags and Layers.");
                _isReady = false;
            }
            else
            {
                tourGeometryMask = 1 << layerIndex;
                Debug.LogWarning("[NavigationReticle] TourGeometry layer mask was not assigned in the Inspector. " +
                                 "Resolved automatically - assign it explicitly to suppress this warning.");
            }
        }

        // Start hidden until first successful raycast
        SetReticleVisible(false);
    }

    private void OnDisable()
    {
        SetReticleVisible(false);
    }

    private void Update()
    {
        if (!_isReady)
            return;

        UpdateReticle();
    }

    #endregion

    #region Reticle Logic

    private void UpdateReticle()
    {
        Ray ray = _camera.ScreenPointToRay(Input.mousePosition);

        if (!Physics.Raycast(ray, out RaycastHit hit, maxRaycastDistance, tourGeometryMask))
        {
            SetReticleVisible(false);
            return;
        }

        // Position at hit point, pushed slightly off the surface to avoid z-fighting
        reticleTransform.position = hit.point + hit.normal * surfaceOffset;

        // Orient to lie flat on the surface.
        // hit.normal on the inverted box points inward (toward camera).
        // Quaternion.LookRotation sets the transform's +Z (forward) to the given direction.
        // For a quad/sprite whose visible face is +Z, forward = hit.normal makes it face the camera.

        // The up vector prevents spin on walls (use world up) but breaks on floor/ceiling
        // where the normal IS vertical - fall back to world forward in that case.
        Vector3 up = Mathf.Abs(Vector3.Dot(hit.normal, Vector3.up)) > 0.9f
            ? Vector3.forward
            : Vector3.up;

        reticleTransform.rotation = Quaternion.LookRotation(hit.normal, up);

        ApplyWorldSize();

        SetReticleVisible(true);
    }

    /// <summary>
    /// Scales the reticle so its sprite's full width is reticleRadius * 2 world units,
    /// dividing out the sprite's own world width and the parent's scale.
    /// </summary>
    private void ApplyWorldSize()
    {
        float spriteWidth = _spriteRenderer.sprite != null ? _spriteRenderer.sprite.bounds.size.x : 0f;
        float parentScale = reticleTransform.parent != null ? reticleTransform.parent.lossyScale.x : 1f;

        // No sprite, or the parent canvas has not been scaled yet (its saved scale is 0).
        if (spriteWidth <= 0f || parentScale <= 0.000001f)
            return;

        float scale = (reticleRadius * 2f) / (spriteWidth * parentScale);
        reticleTransform.localScale = new Vector3(scale, scale, 1f);
    }

    private void SetReticleVisible(bool visible)
    {
        // Intentionally a no-op: the reticle stays visible, and a raycast miss leaves it where it was.
        //if (reticleTransform != null && reticleTransform.gameObject.activeSelf != visible)
        //    reticleTransform.gameObject.SetActive(visible);
    }

    #endregion

    #region Validation

    private bool ValidateReferences()
    {
        bool valid = true;

        if (_camera == null)
        {
            Debug.LogError("[NavigationReticle] No camera assigned and Camera.main is null.");
            valid = false;
        }

        if (reticleTransform == null)
        {
            Debug.LogError("[NavigationReticle] Reticle Transform is not assigned.");
            valid = false;
        }
        else if (_spriteRenderer == null)
        {
            Debug.LogError("[NavigationReticle] Reticle Transform has no SpriteRenderer.");
            valid = false;
        }

        return valid;
    }

    #endregion
}