
using JetBrains.Annotations;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AddressableAssets;

/// <summary>
/// Typed AssetReference for Cubemap textures. Required for Addressables to filter by Cubemap type in the picker.
/// </summary>
[System.Serializable]
public class AssetReferenceCubemap : AssetReferenceT<Cubemap>
{
    public AssetReferenceCubemap(string guid) : base(guid) { }
}

/// <summary>
/// Data container for a single panorama capture point within a tour.
/// Represents one physical location where a 360° image was captured.
/// </summary>
[CreateAssetMenu(fileName = "New Node", menuName = "Tour/Node Data", order = 1)]
public class NodeData : ScriptableObject
{
    [Header("Identification")]

    [Tooltip("Unique identifier for this node within its parent tour. Used for navigation references.")]
    public string id;

    [Tooltip("Human-readable name displayed in UI. does not need to be unique.")]
    public string displayName;

    [Header("Spatial")]

    [Tooltip("World-space position where this panorama was captured. Used for spatial navigation calculations.")]
    public Vector3 position;

    [Tooltip("Floor number for this node. Used for floor filtering in navigation and UI.")]
    public int floor;

    [Header("Panorama")]

    [Tooltip("Panorama image for this location. Uses Addressables for lazy loading")]
    public AssetReferenceCubemap panoramaTexture;

    [Tooltip("Default camera yaw (horizontal rotation) in degrees when arriving at this node. Range: 0-360.")]
    [Range(0f, 360f)]
    public float initialRotation;

    [Header("Points of Interest")]

    [Tooltip("POIs visible from this node. Each POI is a separate ScriptableObject asset")]
    public List<POIData> pointsOfInterest = new List<POIData>();
}
