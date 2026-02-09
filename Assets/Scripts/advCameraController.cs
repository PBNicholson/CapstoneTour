using UnityEngine;

public class advCameraController : MonoBehaviour
{
    public float rotationSpeed = 5.0f;
    public float minVerticalAngle = -80f;
    public float maxVerticalAngle = 80f;

    // --- Horizontal Clamping Feature ---
    [Header("Horizontal Clamp Settings")]
    public bool useHorizontalClamp = false; // Toggle horizontal clamping
    public float minHorizontalAngle = -45f; // Minimum yaw (relative to start)
    public float maxHorizontalAngle = 45f;  // Maximum yaw (relative to start)

    private Vector3 lastMousePosition;
    private float verticalRotation = 0f;

    // --- Horizontal Clamping Feature ---
    private float horizontalRotation = 0f; // Track yaw for clamping

    void Start()
    {
        // Initialize horizontal rotation from current Y rotation
        horizontalRotation = transform.eulerAngles.y;

        // Initialize vertical rotation from current X rotation
        verticalRotation = transform.eulerAngles.x;
    }

    void Update()
    {
        if (Input.GetMouseButtonDown(0))
        {
            lastMousePosition = Input.mousePosition;
        }

        if (Input.GetMouseButton(0))
        {
            Vector3 delta = Input.mousePosition - lastMousePosition;
            float horizontal = delta.x * rotationSpeed * Time.deltaTime;
            float vertical = -delta.y * rotationSpeed * Time.deltaTime;

            // --- Horizontal Clamping Feature ---
            if (useHorizontalClamp)
            {
                horizontalRotation += horizontal;
                horizontalRotation = Mathf.Clamp(horizontalRotation, minHorizontalAngle, maxHorizontalAngle);
            }
            else
            {
                horizontalRotation += horizontal;
            }

            // Apply horizontal rotation (world Y axis)
            transform.rotation = Quaternion.Euler(0f, horizontalRotation, 0f);

            // --- END Horizontal Clamping Feature ---

            // Accumulate vertical rotation
            verticalRotation += vertical;
            verticalRotation = Mathf.Clamp(verticalRotation, minVerticalAngle, maxVerticalAngle);

            // Apply vertical rotation on local X axis
            Vector3 currentEuler = transform.localEulerAngles;
            currentEuler.x = verticalRotation;
            transform.localEulerAngles = currentEuler;

            lastMousePosition = Input.mousePosition;
        }
    }
}