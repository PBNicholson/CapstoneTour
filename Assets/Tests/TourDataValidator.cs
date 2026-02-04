
using UnityEngine;

public class TourDataValidator : MonoBehaviour
{
    [Header("Test Assets")]
    public TourData testTour;

    [Header("Test Parameters")]
    public string testNodeId = "test_node_01";
    public int testFloor = 1;
    public Vector3 testPosition = new Vector3(5f, 0f, 5f);

    [ContextMenu("Run All Validations")]
    public void RunAllValidations()
    {
        if (testTour == null)
        {
            Debug.LogError("[Validator] No TourData assigned.");
            return;
        }

        Debug.Log("=== TourData Validation Start ===");

        ValidateGetNodeById();
        ValidateGetStartNode();
        ValidateGetNodesOnFloor();
        ValidateGetNearestNode();

        Debug.Log("=== TourData Validation Complete ===");
    }

    [ContextMenu("Validate GetNodeById")]
    public void ValidateGetNodeById()
    {
        Debug.Log($"[GetNodeById] Searching for ID: '{testNodeId}'");

        NodeData result = testTour.GetNodeById(testNodeId);

        if (result != null)
            Debug.Log($"[GetNodeById] Found: {result.displayName} (ID: {result.id}");
        else
            Debug.Log($"[GetNodeById] No node found with ID: '{testNodeId}'");

        // Test null/empty handling
        NodeData nullResult = testTour.GetNodeById(null);
        NodeData emptyResult = testTour.GetNodeById("");

        if (nullResult == null && emptyResult == null)
            Debug.Log("[GetNodeById] Null/empty ID handling: PASS");
        else
            Debug.LogError("[GetNodeById] Null/empty ID handling: FAIL");
    }

    [ContextMenu("Validate GetStartNode")]
    public void ValidateGetStartNode()
    {
        Debug.Log($"[GetStartNode] Default start ID: '{testTour.defaultStartNodeId}'");

        NodeData result = testTour.GetStartNode();

        if (result != null)
            Debug.Log($"[GetStartNode] Found: {result.displayName} (ID: {result.id})");
        else if (string.IsNullOrEmpty(testTour.defaultStartNodeId))
            Debug.Log("[GetStartNode] No default start ID configured (expected null)");
        else
            Debug.LogWarning($"[GetStartNode] Start ID '{testTour.defaultStartNodeId}' not found in nodes list");
    }

    [ContextMenu("Validate GetNodesOnFloor")]
    public void ValidateGetNodesOnFloor()
    {
        Debug.Log($"[GetNodesOnFloor] Searching floor: {testFloor}");

        var result = testTour.GetNodesOnFloor(testFloor);

        Debug.Log($"[GetNodesOnFloor] Found {result.Count} node(s) on floor {testFloor}");

        foreach (var node in result)
        {
            Debug.Log($"  - {node.displayName} (ID: {node.id}, ,Position: {node.position})");
        }
    }

    [ContextMenu("Validate GetNearestNode")]
    public void ValidateGetNearestNode()
    {
        Debug.Log($"[GetNearestNod] Searching from position: {testPosition}");
        Debug.Log($"[GetNearestNode] Navigation radius: {testTour.navigationRadius}m");

        // Test without filters
        NodeData result = testTour.GetNearestNode(testPosition);

        if (result != null)
        {
            float dx = result.position.x - testPosition.x;
            float dz = result.position.z - testPosition.z;
            float distance = Mathf.Sqrt(dx * dx + dz * dz);
            Debug.Log($"[GetNearestNode] Nearest: {result.displayName} (ID: {result.id}, Distance: {distance:F2}m");
        }
        else
        {
            Debug.Log("[GetNearestNode] No node within navigation radius");
        }

        // Test with floor filter
        NodeData floorFiltered = testTour.GetNearestNode(testPosition, null, testFloor);

        if (floorFiltered != null)
            Debug.Log($"[GetNearestNode] Nearest on floor {testFloor}: {floorFiltered.displayName}");
        else
            Debug.Log($"[GetNearestNode] No node on floor {testFloor} within radius");
    }
}
