
using System;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;

/// <summary>
/// Loads panorama textures asynchronously via Addressables and displays them as the scene skybox
/// Handles texture memory management by releasing previous textures on swap.
/// </summary>
public class PanoramaRenderer : MonoBehaviour
{
    [Header("Configuration")]

    [Tooltip("Template material using Skybox/Panoramic shader. A runtime copy is created to avoid modifying the asset.")]
    [SerializeField] private Material skyboxMaterialTemplate;

    [Tooltip("Shader property name for the panorama texture. Default works with Unity's Skybox/Panoramic shader.")]
    [SerializeField] private string texturePropertyName = "_Tex";

    [Header("Testing")]

    [Tooltip("Test node for ContextMenu validation.")]
    [SerializeField] private NodeData testNodeA;

    [Tooltip("Test node for ContextMenu validation.")]
    [SerializeField] private NodeData testNodeB;

    /// <summary>
    /// True while a panorama texture is being loaded.
    /// </summary>
    public bool isLoading { get; private set; }

    /// <summary>
    /// The node currently displayed in the skybox, or null if none/cleared.
    /// </summary>
    public NodeData CurrentNode { get; private set; }

    /// <summary>
    /// Fired when a panorama texture has been loaded and applied to the skybox.
    /// Parameter is the NodeData that was loaded, or null if loaded via direct AssetReference.
    /// </summary>
    public event Action<NodeData> OnPanoramaLoaded;

    // Runtime instance of the skybox material (we modify this, not the template asset)
    private Material _runtimeMaterial;

    // Handle for the currently loading cubemap (may be in progress)
    private AsyncOperationHandle<Cubemap> _loadingHandle;
    private bool _hasLoadingHandle;

    // Handle for the previously completed cubemap (held until next swap to keep it visible during load)
    private AsyncOperationHandle<Cubemap> _activeHandle;
    private bool _hasActiveHandle;

    // Reference to the node currently being loaded (needed for event callback)
    private NodeData _pendingNode;

    #region Unity Lifecycle

    private void Awake()
    {
        InitializeRuntimeMaterial();
    }

    private void OnDestroy()
    {
        ReleaseAllHandles();
        DestroyRuntimeMaterial();
    }

    #endregion

    #region Public API

    /// <summary>
    /// Loads and displays the panorama cubemap from the specified node.
    /// Cancels any in-progress load. Previous texture remains visible until new one is ready.
    /// </summary>
    /// <param name="node">The node containing the panorama texture reference.</param>
    public void LoadPanorama(NodeData node)
    {
        if (node == null)
        {
            Debug.LogWarning($"[PanoramaRenderer] LoadPanorama called with null NodeData.");
            return;
        }

        if (!node.panoramaTexture.RuntimeKeyIsValid())
        {
            Debug.LogWarning($"[PanoramaRenderer] NodeData '{node.id}' has invalid panoramaTexture reference.");
            return;
        }

        if (CurrentNode == node && _hasActiveHandle)
        { 
            return;
        }

        CancelLoadingOperation();

        _pendingNode = node;
        isLoading = true;

        _loadingHandle = node.panoramaTexture.LoadAssetAsync<Cubemap>();
        _hasLoadingHandle = true;
        _loadingHandle.Completed += OnCubemapLoadCompleted;
    }

    /// <summary>
    /// Loads and displays a panorama cubemap directly from an AssetReference.
    /// Useful for testing or cases where a full NodeData isn't available.
    /// OnPanoramaLoaded will fire with null NodeData parameter.
    /// </summary>
    /// <param name="CubemapReference">Addressable reference to the panorama cubemap</param>
    public void LoadPanorama(AssetReferenceCubemap cubemapReference)
    {
        if (cubemapReference == null || !cubemapReference.RuntimeKeyIsValid())
        {
            Debug.LogWarning($"[PanoramaRenderer] LoadPanorama called with invalid texture reference.");
            return;
        }

        CancelLoadingOperation();

        _pendingNode = null;
        isLoading = true;

        _loadingHandle = cubemapReference.LoadAssetAsync<Cubemap>();
        _hasLoadingHandle = true;
        _loadingHandle.Completed += OnCubemapLoadCompleted;
    }

    /// <summary>
    /// Clears the skybox to black and releases any held texture handles.
    /// </summary>
    public void ClearSkybox()
    {
        CancelLoadingOperation();
        ReleaseActiveHandle();
        ApplyCubemap(null);
        CurrentNode = null;
    }

    #endregion

    #region Private Methods

    private void InitializeRuntimeMaterial()
    {
        if (skyboxMaterialTemplate == null)
        {
            Debug.LogError($"[PanoramaRenderer] No skybox material template assigned.");
            return;
        }

        _runtimeMaterial = Instantiate(skyboxMaterialTemplate);
        _runtimeMaterial.name = "PanoramaSkybox_Runtime";

        // Start with black skybox
        ApplyCubemap(null);

        RenderSettings.skybox = _runtimeMaterial;
    }

    private void DestroyRuntimeMaterial()
    {
        if (_runtimeMaterial != null)
        {
            // Unassign from render settings before destroying
            if (RenderSettings.skybox == _runtimeMaterial)
            {
                RenderSettings.skybox = null;
            }

            Destroy(_runtimeMaterial);
            _runtimeMaterial = null;
        }
    }

    private void OnCubemapLoadCompleted(AsyncOperationHandle<Cubemap> handle)
    {
        // If this handle was cancelled/ released before completion, ignore the callback
        if (!_hasLoadingHandle)
            return;

        isLoading = false;

        if (handle.Status != AsyncOperationStatus.Succeeded)
        {
            string nodeId = _pendingNode != null ? _pendingNode.id : "direct reference";
            Debug.LogWarning($"[PanoramaRenderer] Failed to load panorama cubemap for {nodeId}' : {handle.OperationException?.Message}");

            // Release the failed handle
            Addressables.Release(handle);
            _hasLoadingHandle = false;
            _pendingNode = null;
            return;
        }

        // Release the previous active texture now that we have a new one
        ReleaseActiveHandle();

        // Apply the new cubemap
        ApplyCubemap(handle.Result);

        // Promote loading handle to active handle
        _activeHandle = handle;
        _hasActiveHandle = true;
        _hasLoadingHandle = false;

        // Cache node reference before clearing, then invoke event
        NodeData loadedNode = _pendingNode;
        _pendingNode = null;
        CurrentNode = loadedNode;

        OnPanoramaLoaded?.Invoke(loadedNode);
    }

    private void ApplyCubemap(Cubemap cubemap)
    {
        if (_runtimeMaterial == null)
        {
            Debug.LogError($"[PanoramaRenderer] Cannot apply cubemap: runtime material is null.");
            return;
        }

        _runtimeMaterial.SetTexture(texturePropertyName, cubemap);
    }

    private void CancelLoadingOperation()
    {
        if (!_hasLoadingHandle)
            return;

        // Unsubscribe to prevent callback from firing after cancel
        _loadingHandle.Completed -= OnCubemapLoadCompleted;

        // Release the handle (this cancels if still loading)
        Addressables.Release(_loadingHandle);

        _hasLoadingHandle = false;
        _pendingNode = null;
        isLoading = false;
    }

    private void ReleaseActiveHandle()
    {
        if (!_hasActiveHandle)
            return;

        Addressables.Release(_activeHandle);
        _hasActiveHandle = false;
    }

    private void ReleaseAllHandles()
    {
        CancelLoadingOperation();
        ReleaseActiveHandle();
    }

    #endregion

    #region ContextMenu Testing

    [ContextMenu("Load Test Node A")]
    private void LoadTestNodeA()
    {
        if (testNodeA == null)
        {
            Debug.LogWarning($"[PanoramaRenderer] Test node A is not assigned.");
            return;
        }

        LoadPanorama(testNodeA);
    }

    [ContextMenu("Load Test Node B")]
    private void LoadTestNodeB()
    {
        if (testNodeB == null)
        {
            Debug.LogWarning($"[PanoramaRenderer] Test node B is not assigned.");
            return;
        }

        LoadPanorama(testNodeB);
    }

    [ContextMenu("Clear Skybox")]
    private void ClearSkyboxFromMenu()
    {
        ClearSkybox();
    }

    #endregion
}
