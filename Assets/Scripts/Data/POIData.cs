
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
/// </summary>
[CreateAssetMenu(fileName = "New POI", menuName = "Tour/POI Data", order = 2)]
public class POIData : ScriptableObject
{
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
}
