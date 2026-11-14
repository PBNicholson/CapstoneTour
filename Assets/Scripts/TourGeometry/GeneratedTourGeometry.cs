using UnityEngine;

/// <summary>
/// Marker on the root of a hierarchy built by the Tour Geometry Generator.
/// The generator uses it to find and replace its own output, and to detect stale geometry.
/// Do not add this manually; regeneration destroys every object that carries it for the same layout.
/// </summary>
[DisallowMultipleComponent]
[AddComponentMenu("Tour/Generated Tour Geometry (managed)")]
public class GeneratedTourGeometry : MonoBehaviour
{
    [Tooltip("Layout this hierarchy was generated from.")]
    [SerializeField] private BuildingLayout sourceLayout;

    [Tooltip("BuildingLayout.ComputeContentHash() at generation time.")]
    [SerializeField] private string layoutHash;

    [Tooltip("Generation time, UTC, ISO 8601.")]
    [SerializeField] private string generatedAtUtc;

    public BuildingLayout SourceLayout => sourceLayout;
    public string LayoutHash => layoutHash;
    public string GeneratedAtUtc => generatedAtUtc;

    /// <summary>True if the source layout still exists and its content matches what was generated.</summary>
    public bool IsUpToDate => sourceLayout != null && sourceLayout.ComputeContentHash() == layoutHash;

    /// <summary>Called by the generator after building the hierarchy.</summary>
    public void SetGenerationInfo(BuildingLayout layout, string hash, string timestampUtc)
    {
        sourceLayout = layout;
        layoutHash = hash;
        generatedAtUtc = timestampUtc;
    }
}