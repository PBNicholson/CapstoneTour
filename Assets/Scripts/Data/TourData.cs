
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Data container for a complete building tour.
/// Holds references to all nodes and configuration for navigation behavior.
/// </summary>
[CreateAssetMenu(fileName = "New Tour", menuName = "Tour/Tour Data", order = 0)]
public class TourData : ScriptableObject
{
    [Header("Building Information")]

    [Tooltip("Display name for this building shown in UI.")]
    public string buildingName;

    [Tooltip("Optional thumbnail image for tour selection UI.")]
    public Sprite buildingThumbnail;

    [Header("Navigation Configuration")]

    [Tooltip("Maximum horizontal distance (in meters) for considering a node as a navigation candidate.")]
    [Min(0.1f)]
    public float navigationRadius = 3f;

    [Tooltip("Node ID that serves as the entry point when starting this tour. Must match an ID in the nodes list.")]
    public string defaultStartNodeId;

    [Header("Floor Configuration")]

    [Tooltip("List of floor numbers present in this building. Used for floor selection UI. Should be in ascending order.")]
    public List<int> floors = new List<int>();

    [Header("Nodes")]

    [Tooltip("All panorama nodes in this tour. Each node is a separate ScriptableObject asset.")]
    public List<NodeData> nodes = new List<NodeData>();

    #region Runtime Helper Methods

    /// <summary>
    /// Finds node by its unique identifier.
    /// </summary>
    /// <param name="id">The node ID to search for.</param>
    /// <returns>The matching NodeData, or null if not found.</returns>
    public NodeData GetNodeById(string id)
    {
        if (string.IsNullOrEmpty(id))
            return null;

        for (int i = 0; i < nodes.Count; i++)
        {
            if (nodes[i] != null && nodes[i].id == id)
                return nodes[i];
        }

        return null;
    }

    /// <summary>
    /// Gets the default starting node for this tour.
    /// </summary>
    /// <returns>The starting NodeData, or null if defaultStartNodeId is invalid.</returns>
    public NodeData GetStartNode()
    {
        return GetNodeById(defaultStartNodeId);
    }

    /// <summary>
    /// Gets all nodes located on a specific floor
    /// </summary>
    /// <param name="floor">The floor number to filter by.</param>
    /// <returns>List of nodes on the specified floor. Empty list if none found.</returns>
    public List<NodeData> GetNodesOnFloor(int floor)
    {
        List<NodeData> result = new List<NodeData>();

        for (int i = 0; i < nodes.Count; i++)
        {
            if (nodes[i] != null && nodes[i].floor == floor)
                    result.Add(nodes[i]);
        }

        return result;
    }

    /// <summary>
    /// Finds the nearest node to a given world position within the navigation radius.
    /// Only considers horizontal distance (ignores Y axis).
    /// </summary>
    /// <param name="worldPosition">The position to search from.</param>
    /// <param name="excludeNodeId">Optional node ID to exclude from results (typically current node).</param>
    /// <param name="floorFilter">Optional floor number to restrict search. Use null for all floors.</param>
    /// <returns>the nearest NodeData within radius, or null if none found</returns>
    public NodeData GetNearestNode(Vector3 worldPosition, string excludeNodeId = null, int? floorFilter = null)
    {
        NodeData nearest = null;
        float nearestDistanceSqr = navigationRadius * navigationRadius;

        for (int i = 0; i< nodes.Count; i++)
        {
            NodeData node = nodes[i];

            if (node == null)
                continue;

            // Skip excluded node
            if (!string.IsNullOrEmpty(excludeNodeId) && node.id == excludeNodeId)
                continue;

            // Apply floor filter if specified
            if (floorFilter.HasValue && node.floor != floorFilter.Value)
                continue;

            // Calculate horizontal distance (ignore Y)
            float dx = node.position.x - worldPosition.x;
            float dz = node.position.z - worldPosition.z;
            float distanceSqr = dx * dx + dz * dz;

            if (distanceSqr < nearestDistanceSqr)
            {
                nearestDistanceSqr = distanceSqr;
                nearest = node;
            }
        }

        return nearest;
    }

    #endregion

}
