
using UnityEngine;
using UnityEngine.AddressableAssets;

/// <summary>
/// Defines interaction behaviors for Points of Interest
/// </summary>
public enum POIInteractionType
{
    /// <summary>
    /// Shows label/description when in view. No click interaction.
    /// </summary>
    DisplayOnly,

    /// <summary>
    /// Click to show detailed panel with full description and image.
    /// </summary>
    Expandable,

    /// <summary>
    /// Click to open URL in new browser tab.
    /// </summary>
    ExternalLink,

   /// <summary>
   /// Click to navigate to a specific node.
   /// </summary>
    NavigationTrigger
}

/// <summary>
/// Data container for a Point of Interest within a panorama node.
/// POIs display contextual information based on camera orientation
/// 
/// A POI represents a fixed physical location in the building (a doorway, a display
/// case, a sign). One POIData asset can be referenced by any number of NodeData assets.
/// The system computes the viewing direction from each node at runtime
/// </summary>
[CreateAssetMenu(fileName = "New POI", menuName = "Tour/POI Data", order = 2)]
public class POIData : ScriptableObject
{
    [Header("Spatial")]

    [Tooltip("The real-world location of this POI in Unity world space. " +
             "Set this once to match where the physical feature is located in the building. " +
             "This position is shared across all nodes that reference this POI - you do not need to set a direction " +
             "per node. The system computes the correct viewing angle from each node automatically.")]
    public Vector3 worldPosition;

    [Tooltip("When enabled, draws gizmos for this POI in the scene view when TourManager is selected." +
             "Disable to reduce visual clutter when working with many POIs.")]
    public bool showGizmos = true;

    [Header("Display")]

    [Tooltip("Short display name shown when POI is visible.")]
    public string label;

    [Tooltip("Detailed text shown in expanded view. Supports multiple lines")]
    [TextArea(3, 6)]
    public string description;

    [Tooltip("Optional image displayed in expanded view. Uses Addressables for lazy loading.")]
    public AssetReferenceSprite image;

    [Header("Interaction")]

    [Tooltip("Determines how the POI responds to user interaction.")]
    public POIInteractionType interactionType = POIInteractionType.DisplayOnly;

    [Tooltip("URL opened when interaction type is ExternalLink. Include full URL with http://.")]
    public string externalUrl;

    [Tooltip("Node ID to navigate to when interaction type is NavigationTrigger. Must match a node ID within the same tour")]
    public string targetNodeId;

    #region Spatial Helpers

    /// <summary>
    /// Computes the normalized direction and distance from a given world position to this POI's worldPosition.
    /// </summary>
    /// <param name="fromPosition">
    /// The world-space origin to measure from. Typically the owning NodeData's position field.
    /// </param>
    /// <param name="direction">
    /// Normalized direction vector from <paramref name="fromPosition"/> toward worldPosition.
    /// Returns Vector3.forward if the POI is at the same position as the origin (degenerate case).
    /// </param>
    /// <param name="distance">
    /// Distance in world units between <paramref name="fromPosition"/> and worldPosition.
    /// </param>
    public void GetDirectionAndDistance(Vector3 fromPosition, out Vector3 direction, out float distance)
    {
        Vector3 offset = worldPosition - fromPosition;
        distance = offset.magnitude;

        if (distance < 0.001f)
        {
            Debug.LogWarning($"[POIData] '{name}' — worldPosition is at or extremely close to fromPosition ({fromPosition}). " +
                             "This is likely an authoring error (worldPosition not set, or POI placed at a node origin). " +
                             "Returning Vector3.forward as fallback direction.");
            direction = Vector3.forward;
            return;
        }

        direction = offset / distance; // equivalent to .normalized but avoids a second magnitude call
    }

    #endregion
}
