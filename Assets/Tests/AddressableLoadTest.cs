using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;


public class AddressableLoadTest : MonoBehaviour
{
    [Header("Test Configuration")]
    public TourData tourData;

    [Header("Runtime State (Read Only)")]
    [SerializeField] private bool isLoading;
    [SerializeField] private string loadedTextureName;
    [SerializeField] private Vector2 loadedTextureSize;

    private AsyncOperationHandle<Texture2D> currentHandle;

    [ContextMenu("Test Load Start Node Panorama")]
    public void TestLoadStartNode()
    {
        if (tourData == null)
        {
            Debug.LogError("[AddressableTest] No TourData assigned.");
            return;
        }

        NodeData startNode = tourData.GetStartNode();

        if (startNode == null)
        {
            Debug.LogError($"[AddressableTest] Start node '{tourData.defaultStartNodeId}' not found");
            return;
        }

        if (startNode.panoramaTexture == null || !startNode.panoramaTexture.RuntimeKeyIsValid())
        {
            Debug.LogError($"[AddressableTest] Start node '{startNode.id}' has no valid panorama texture reference.");
            return;
        }

        Debug.Log($"[AddressableTest] Loading panorama for node: {startNode.id}");
        LoadPanorama(startNode);
    }

    private void LoadPanorama(NodeData node)
    {
        // Release previous handle if exists
        if (currentHandle.IsValid())
        {
            Addressables.Release(currentHandle);
            {
                Addressables.Release(currentHandle);
                Debug.Log("[AddressableTest] Released previous texture.");
            }  
        }

        isLoading = true;
        loadedTextureName = "Loading...";
        loadedTextureSize = Vector2.zero;

        currentHandle = node.panoramaTexture.LoadAssetAsync<Texture2D>();
        currentHandle.Completed += OnLoadComplete;
    }

    private void OnLoadComplete(AsyncOperationHandle<Texture2D> handle)
    {
        isLoading = false;

        if (handle.Status == AsyncOperationStatus.Succeeded)
        {
            Texture2D texture = handle.Result;
            loadedTextureName = texture.name;
            loadedTextureSize = new Vector2(texture.width, texture.height);

            Debug.Log($"[AddressableTest] SUCCESS - Loaded: {texture.name}");
            Debug.Log($"[AddressableTest] Dimensions: {texture.width}x{texture.height}");
            Debug.Log($"[AddressableTest] Format: {texture.format}");
            Debug.Log($"[AddressableTest] Memory: ~{(texture.width * texture.height * 4) / (1024f * 1024f):F2} MB (estimated)");
        }
        else
        {
            loadedTextureName = "FAILED";
            Debug.LogError($"[AddressableTest] FAILED to load panorama: {handle.OperationException}");
        }
    }

    [ContextMenu("Release Loaded Texture")]
    public void ReleaseTexture()
    {
        if (currentHandle.IsValid())
        {
            Addressables.Release(currentHandle);
            loadedTextureName = "(released)";
            loadedTextureSize = Vector2.zero;
            Debug.Log("[AddressableTest] Texture released.");
        }
        else
        {
            Debug.Log("[AddressableTest] No texture currently loaded.");
        }
    }

    private void OnDestroy()
    {
        // Clean up on destroy
        if (currentHandle.IsValid())
        {
            Addressables.Release(currentHandle);
        }
    }
}
