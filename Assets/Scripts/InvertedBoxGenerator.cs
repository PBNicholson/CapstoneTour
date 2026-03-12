
using UnityEngine;

/// <summary>
/// Generates a box mesh with inward-facing normals and applies it to a MeshCollider.
/// Used as a raycast target for spatial navigation - raycasts originating inside the box
/// hit the interior faces (floor, walls, ceiling).
/// 
/// Designer workflow: Adjust 'size' in the Inspector to match the building's lobby dimensions.
/// Reposition the GameObject's Transform to center the box around the panorama capture area.
/// The mesh regenerates automatically when size changes in the Editor.
/// 
/// No visual rendering occurs - this object exists solely as a collision volume.
/// </summary>
[RequireComponent(typeof(MeshCollider))]
[ExecuteAlways]
public class InvertedBoxGenerator : MonoBehaviour
{
    [Tooltip("Interior dimensions of the box in meters (width, height, depth). Mesh regenerates on change.")]
    public Vector3 size = new Vector3(10f, 4f, 10f);

    private MeshCollider _meshCollider;
    private Mesh _mesh;

    // Track last-applied size to avoid unnecessary regeneration
    private Vector3 _lastSize;

    #region Unity Lifecycle

    private void Awake()
    {
        GenerateAndApply();
    }

    private void OnValidate()
    {
        // Clamp to minimum size to prevent degenerate meshes
        size.x = Mathf.Max(size.x, 0.1f);
        size.y = Mathf.Max(size.y, 0.1f);
        size.z = Mathf.Max(size.z, 0.1f);

        // Regenerate if size changed (OnValidate fires on every Inspector edit)
        if (_mesh != null && size != _lastSize)
        {
            GenerateAndApply();
        }
    }

    private void OnDestroy()
    {
        // Clean up the procedural mesh to avoid leaks
        if (_mesh != null)
        {
            if (Application.isPlaying)
                Destroy(_mesh);
            else
                DestroyImmediate(_mesh);
        }
    }

    #endregion

    #region Mesh Generation

    private void GenerateAndApply()
    {
        if (_mesh == null)
        {
            _mesh = new Mesh();
            _mesh.name = "InvertedBox";
        }

        BuildInvertedBox(_mesh, size);
        _lastSize = size;

        // Apply to MeshCollider
        _meshCollider = GetComponent<MeshCollider>();
        _meshCollider.sharedMesh = null; // Force reassignment
        _meshCollider.sharedMesh = _mesh;
    }

    private static void BuildInvertedBox(Mesh mesh, Vector3 size)
    {
        mesh.Clear();

        float hx = size.x * 0.5f;
        float hy = size.y * 0.5f;
        float hz = size.z * 0.5f;

        // 24 vertices: 4 per face, ordered for inward-facing normals
        Vector3[] vertices = new Vector3[24];
        Vector3[] normals = new Vector3[24];
        int[] triangles = new int[36];

        // --- +X face (right wall, normal points -X / inward) ---
        vertices[0] = new Vector3(hx, -hy, -hz);
        vertices[1] = new Vector3(hx, hy, -hz);
        vertices[2] = new Vector3(hx, hy, hz);
        vertices[3] = new Vector3(hx, -hy, hz);
        normals[0] = normals[1] = normals[2] = normals[3] = Vector3.left;

        // --- -X face (left wall, normal points +X / inward) ---
        vertices[4] = new Vector3(-hx, -hy, hz);
        vertices[5] = new Vector3(-hx, hy, hz);
        vertices[6] = new Vector3(-hx, hy, -hz);
        vertices[7] = new Vector3(-hx, -hy, -hz);
        normals[4] = normals[5] = normals[6] = normals[7] = Vector3.right;

        // --- +Y face (ceiling, normal points -Y / inward) ---
        vertices[8] = new Vector3(-hx, hy, -hz);
        vertices[9] = new Vector3(-hx, hy, hz);
        vertices[10] = new Vector3(hx, hy, hz);
        vertices[11] = new Vector3(hx, hy, -hz);
        normals[8] = normals[9] = normals[10] = normals[11] = Vector3.down;

        // --- -Y face (floor, normal points +Y / inward) ---
        vertices[12] = new Vector3(-hx, -hy, hz);
        vertices[13] = new Vector3(-hx, -hy, -hz);
        vertices[14] = new Vector3(hx, -hy, -hz);
        vertices[15] = new Vector3(hx, -hy, hz);
        normals[12] = normals[13] = normals[14] = normals[15] = Vector3.up;

        // --- +Z face (front wall, normal points -Z / inward) ---
        vertices[16] = new Vector3(hx, -hy, hz);
        vertices[17] = new Vector3(hx, hy, hz);
        vertices[18] = new Vector3(-hx, hy, hz);
        vertices[19] = new Vector3(-hx, -hy, hz);
        normals[16] = normals[17] = normals[18] = normals[19] = Vector3.back;

        // --- -Z face (back wall, normal points +Z / inward) ---
        vertices[20] = new Vector3(-hx, -hy, -hz);
        vertices[21] = new Vector3(-hx, hy, -hz);
        vertices[22] = new Vector3(hx, hy, -hz);
        vertices[23] = new Vector3(hx, -hy, -hz);
        normals[20] = normals[21] = normals[22] = normals[23] = Vector3.forward;

        // Triangles: 2 per face, 6 faces = 12 triangles = 36 indices
        // Winding order: clockwise when viewed from inside (matching inward normals)
        for (int face = 0; face < 6; face++)
        {
            int vi = face * 4;  // vertex index offset
            int ti = face * 6;  // triangle index offset

            triangles[ti + 0] = vi + 0;
            triangles[ti + 1] = vi + 2;
            triangles[ti + 2] = vi + 1;

            triangles[ti + 3] = vi + 0;
            triangles[ti + 4] = vi + 3;
            triangles[ti + 5] = vi + 2;
        }

        mesh.vertices = vertices;
        mesh.normals = normals;
        mesh.triangles = triangles;

        mesh.RecalculateBounds();
    }

    #endregion

    #region Editor Visualization

    private void OnDrawGizmosSelected()
    {
        // Draw wireframe box matching the collider bounds for spatial reference in Scene view
        Gizmos.color = new Color(0.2f, 0.8f, 0.2f, 0.5f);
        Gizmos.matrix = transform.localToWorldMatrix;
        Gizmos.DrawWireCube(Vector3.zero, size);
    }

    #endregion
}
