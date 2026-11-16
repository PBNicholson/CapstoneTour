using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Editor-only placement aid for the campus map hotspots. Adds three commands to the
/// CampusMapPanel component's context menu (the three-dot menu on the component header):
///
///   Placement 1 - Spawn hotspots
///       Shows TourCatalog.campusMap in the map Image, then creates one hotspot object per
///       catalog entry under the map Image, at the entry's saved position. Entries that have
///       no position yet (0,0) are parked in a row along the bottom of the map. Each object
///       is named "NN TourAssetName" and shows its name pill.
///   Placement 2 - Write positions to catalog
///       Reads where each placement hotspot now sits on the map and writes it to
///       TourCatalogEntry.mapHotspotPosition, normalised to the map image ((0,0) bottom-left,
///       (1,1) top-right). Saves the catalog asset and logs the values and any pair of
///       hotspots closer than the style spec's 48 units. Can be run as often as needed.
///   Placement 3 - Remove placement hotspots
///       Deletes the placement objects. Run it before saving the scene.
///
/// Workflow: 1, drag the hotspots in the Scene view (Rect tool), 2, then 3, then save the scene.
/// The same steps re-position hotspots after the map image is replaced.
///
/// The placement objects are ordinary prefab instances in the scene. If they are saved with
/// the scene by mistake, CampusMapPanel removes them at runtime and logs a warning.
///
/// The commands read CampusMapPanel's private serialized fields by name
/// (tourCatalog, mapImage, hotspotPrefab) and MapHotspot's namePillRoot. If those fields are
/// renamed, update the constants below.
/// </summary>
public static class CampusMapHotspotPlacement
{
    private const string MenuRoot = "CONTEXT/CampusMapPanel/";
    private const string SpawnMenu = MenuRoot + "Placement 1 - Spawn hotspots";
    private const string WriteMenu = MenuRoot + "Placement 2 - Write positions to catalog";
    private const string RemoveMenu = MenuRoot + "Placement 3 - Remove placement hotspots";

    // Serialized field names read through SerializedObject.
    private const string CatalogField = "tourCatalog";
    private const string MapImageField = "mapImage";
    private const string HotspotPrefabField = "hotspotPrefab";
    private const string NamePillField = "namePillRoot";

    // Style spec, "Map hotspot": hotspots are at least 48 apart, centre to centre.
    private const float MinSpacing = 48f;

    // Where entries with no saved position are parked, normalised to the map image.
    private const float ParkedRowY = 0.15f;
    private const float ParkedRowXMin = 0.15f;
    private const float ParkedRowXMax = 0.85f;

    private const string LogPrefix = "[CampusMapHotspotPlacement] ";

    #region Menu Commands

    [MenuItem(SpawnMenu, false, 2000)]
    private static void SpawnHotspots(MenuCommand command)
    {
        if (!TryGetContext(command, true, out CampusMapPanel panel, out TourCatalog catalog, out Image mapImage, out MapHotspot prefab))
            return;

        if (prefab == null)
        {
            Debug.LogError(LogPrefix + "CampusMapPanel has no Hotspot Prefab assigned.");
            return;
        }

        // Start clean: a second Spawn replaces the objects from the first.
        int removed = DestroyPlacementHotspots(mapImage);

        SyncMapSprite(catalog, mapImage);

        // Count the entries with no saved position so they can be spread along the parked row.
        int unplacedTotal = 0;

        for (int i = 0; i < catalog.entries.Count; i++)
        {
            TourCatalogEntry entry = catalog.entries[i];

            if (entry != null && entry.tourData != null && entry.mapHotspotPosition == Vector2.zero)
                unplacedTotal++;
        }

        int spawned = 0;
        int unplacedIndex = 0;
        GameObject first = null;

        for (int i = 0; i < catalog.entries.Count; i++)
        {
            TourCatalogEntry entry = catalog.entries[i];

            if (entry == null || entry.tourData == null)
            {
                Debug.LogWarning(LogPrefix + $"TourCatalog entry at index {i} is null or has no TourData. Skipping.");
                continue;
            }

            Vector2 position = entry.mapHotspotPosition;

            if (position == Vector2.zero)
            {
                float t = unplacedTotal > 1 ? (float)unplacedIndex / (unplacedTotal - 1) : 0.5f;
                position = new Vector2(Mathf.Lerp(ParkedRowXMin, ParkedRowXMax, t), ParkedRowY);
                unplacedIndex++;
            }

            MapHotspot hotspot = PrefabUtility.InstantiatePrefab(prefab, mapImage.transform) as MapHotspot;

            if (hotspot == null)
            {
                Debug.LogError(LogPrefix + "Could not instantiate the hotspot prefab. Is Hotspot Prefab a prefab asset with MapHotspot on its root?");
                return;
            }

            Undo.RegisterCreatedObjectUndo(hotspot.gameObject, "Spawn placement hotspots");

            hotspot.gameObject.name = BuildPlacementName(i, entry.tourData);
            hotspot.Initialize(entry.tourData, position, null, null);
            ShowNamePill(hotspot);

            if (first == null)
                first = hotspot.gameObject;

            spawned++;
        }

        EditorSceneManager.MarkSceneDirty(panel.gameObject.scene);

        if (first != null)
            Selection.activeGameObject = first;

        Debug.Log(LogPrefix + $"Spawned {spawned} placement hotspot(s) under '{mapImage.name}'" +
                  (removed > 0 ? $" (replaced {removed} existing)" : string.Empty) + ". " +
                  (unplacedIndex > 0 ? $"{unplacedIndex} had no saved position and are parked along the bottom of the map. " : string.Empty) +
                  "Drag them in the Scene view, then run 'Placement 2 - Write positions to catalog'.");
    }

    [MenuItem(WriteMenu, false, 2001)]
    private static void WritePositions(MenuCommand command)
    {
        if (!TryGetContext(command, true, out CampusMapPanel panel, out TourCatalog catalog, out Image mapImage, out MapHotspot prefab))
            return;

        MapHotspot[] hotspots = mapImage.GetComponentsInChildren<MapHotspot>(true);

        if (hotspots.Length == 0)
        {
            Debug.LogWarning(LogPrefix + "No placement hotspots found under the map Image. Run 'Placement 1 - Spawn hotspots' first.");
            return;
        }

        // Make sure the map Image has its current size before positions are read.
        Canvas.ForceUpdateCanvases();

        Rect mapRect = mapImage.rectTransform.rect;

        if (mapRect.width <= 0f || mapRect.height <= 0f)
        {
            Debug.LogError(LogPrefix + "The map Image has no size. Is CampusMapPanel active and laid out?");
            return;
        }

        Undo.RecordObject(catalog, "Write campus map hotspot positions");

        bool[] written = new bool[catalog.entries.Count];
        List<int> writtenIndices = new List<int>();
        StringBuilder report = new StringBuilder();
        int skipped = 0;

        for (int i = 0; i < hotspots.Length; i++)
        {
            MapHotspot hotspot = hotspots[i];

            if (!TryResolveEntryIndex(hotspot.gameObject.name, catalog, out int entryIndex, out string problem))
            {
                Debug.LogWarning(LogPrefix + $"Skipped '{hotspot.gameObject.name}': {problem}", hotspot);
                skipped++;
                continue;
            }

            if (written[entryIndex])
            {
                Debug.LogWarning(LogPrefix + $"Skipped '{hotspot.gameObject.name}': catalog entry {entryIndex} was already written by another object.", hotspot);
                skipped++;
                continue;
            }

            Vector2 position = hotspot.ReadNormalizedPosition();
            position = new Vector2(Round4(position.x), Round4(position.y));

            if (position == Vector2.zero)
            {
                Debug.LogWarning(LogPrefix + $"'{hotspot.gameObject.name}' sits exactly on the map's bottom-left corner. (0,0) is read as 'not placed'.", hotspot);
            }

            catalog.entries[entryIndex].mapHotspotPosition = position;
            written[entryIndex] = true;
            writtenIndices.Add(entryIndex);

            // Re-anchor the placement object at the written position so it follows the map
            // if the Game view's aspect ratio is changed afterwards.
            Undo.RecordObject(hotspot.transform, "Write campus map hotspot positions");
            hotspot.SetNormalizedPosition(position);
        }

        EditorUtility.SetDirty(catalog);
        AssetDatabase.SaveAssetIfDirty(catalog);
        EditorSceneManager.MarkSceneDirty(panel.gameObject.scene);

        // Report: values written, entries not covered, and pairs that are too close.
        writtenIndices.Sort();
        report.AppendLine($"Wrote {writtenIndices.Count} position(s) to '{catalog.name}'" +
                          (skipped > 0 ? $", skipped {skipped} object(s) (see warnings)" : string.Empty) + ".");
        report.AppendLine("index | tour | x | y");

        for (int i = 0; i < writtenIndices.Count; i++)
        {
            TourCatalogEntry entry = catalog.entries[writtenIndices[i]];
            report.AppendLine($"{writtenIndices[i]} | {entry.tourData.name} | {entry.mapHotspotPosition.x:0.0000} | {entry.mapHotspotPosition.y:0.0000}");
        }

        for (int i = 0; i < catalog.entries.Count; i++)
        {
            TourCatalogEntry entry = catalog.entries[i];

            if (!written[i] && entry != null && entry.tourData != null)
                report.AppendLine($"NOT WRITTEN (no placement object): {i} | {entry.tourData.name}");
        }

        int tooClose = 0;

        for (int a = 0; a < writtenIndices.Count; a++)
        {
            for (int b = a + 1; b < writtenIndices.Count; b++)
            {
                TourCatalogEntry entryA = catalog.entries[writtenIndices[a]];
                TourCatalogEntry entryB = catalog.entries[writtenIndices[b]];

                Vector2 delta = entryA.mapHotspotPosition - entryB.mapHotspotPosition;
                float distance = new Vector2(delta.x * mapRect.width, delta.y * mapRect.height).magnitude;

                if (distance < MinSpacing)
                {
                    report.AppendLine($"TOO CLOSE ({distance:0.0} < {MinSpacing:0}): {entryA.tourData.name} and {entryB.tourData.name}");
                    tooClose++;
                }
            }
        }

        report.AppendLine($"Spacing measured with the map drawn at {mapRect.width:0} x {mapRect.height:0} canvas units " +
                          "(the current Game view size). Check it with the Game view at 1920x1080.");

        if (tooClose > 0)
            Debug.LogWarning(LogPrefix + report);
        else
            Debug.Log(LogPrefix + report);
    }

    [MenuItem(RemoveMenu, false, 2002)]
    private static void RemoveHotspots(MenuCommand command)
    {
        if (!TryGetContext(command, false, out CampusMapPanel panel, out TourCatalog catalog, out Image mapImage, out MapHotspot prefab))
            return;

        int removed = DestroyPlacementHotspots(mapImage);

        if (removed > 0)
            EditorSceneManager.MarkSceneDirty(panel.gameObject.scene);

        Debug.Log(LogPrefix + $"Removed {removed} placement hotspot(s). Save the scene.");
    }

    #endregion

    #region Helpers

    /// <summary>
    /// Resolves the panel from the menu command and reads its catalog, map Image and hotspot
    /// prefab. Logs the reason and returns false if the commands cannot run.
    /// </summary>
    private static bool TryGetContext(
        MenuCommand command,
        bool requireActive,
        out CampusMapPanel panel,
        out TourCatalog catalog,
        out Image mapImage,
        out MapHotspot prefab)
    {
        panel = command.context as CampusMapPanel;
        catalog = null;
        mapImage = null;
        prefab = null;

        if (panel == null)
            return false;

        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            Debug.LogWarning(LogPrefix + "The placement commands run in Edit mode only. Exit Play mode first.");
            return false;
        }

        SerializedObject serializedPanel = new SerializedObject(panel);
        catalog = ReadReference<TourCatalog>(serializedPanel, CatalogField);
        mapImage = ReadReference<Image>(serializedPanel, MapImageField);
        prefab = ReadReference<MapHotspot>(serializedPanel, HotspotPrefabField);

        if (catalog == null)
        {
            Debug.LogError(LogPrefix + "CampusMapPanel has no Tour Catalog assigned.");
            return false;
        }

        if (mapImage == null)
        {
            Debug.LogError(LogPrefix + "CampusMapPanel has no Map Image assigned.");
            return false;
        }

        if (requireActive && !mapImage.gameObject.activeInHierarchy)
        {
            Debug.LogError(LogPrefix + "The map Image is not active. Activate CampusMapPanel (and its parents) first, " +
                           "so the map is visible and has its real size.");
            return false;
        }

        return true;
    }

    private static T ReadReference<T>(SerializedObject serializedObject, string fieldName) where T : Object
    {
        SerializedProperty property = serializedObject.FindProperty(fieldName);

        if (property == null)
        {
            Debug.LogError(LogPrefix + $"Field '{fieldName}' was not found on {serializedObject.targetObject.GetType().Name}. " +
                           "Was it renamed? Update the constants in CampusMapHotspotPlacement.");
            return null;
        }

        return property.objectReferenceValue as T;
    }

    /// <summary>
    /// Shows the catalog's map in the map Image in Edit mode and sets the fitter's aspect ratio,
    /// matching what CampusMapPanel does at runtime.
    /// </summary>
    private static void SyncMapSprite(TourCatalog catalog, Image mapImage)
    {
        Sprite map = catalog.campusMap;

        if (map == null)
        {
            Debug.LogWarning(LogPrefix + $"'{catalog.name}' has no Campus Map assigned. Hotspots will be placed on the map Image as it is.");
            return;
        }

        Undo.RecordObject(mapImage, "Spawn placement hotspots");
        mapImage.sprite = map;
        mapImage.enabled = true;
        EditorUtility.SetDirty(mapImage);

        AspectRatioFitter fitter = mapImage.GetComponent<AspectRatioFitter>();

        if (fitter == null)
        {
            Debug.LogError(LogPrefix + "The map Image has no AspectRatioFitter. Add one set to Envelope Parent.");
            return;
        }

        if (map.rect.height > 0f)
        {
            Undo.RecordObject(fitter, "Spawn placement hotspots");
            fitter.aspectRatio = map.rect.width / map.rect.height;
            EditorUtility.SetDirty(fitter);
        }

        Canvas.ForceUpdateCanvases();
    }

    private static int DestroyPlacementHotspots(Image mapImage)
    {
        MapHotspot[] existing = mapImage.GetComponentsInChildren<MapHotspot>(true);

        for (int i = 0; i < existing.Length; i++)
            Undo.DestroyObjectImmediate(existing[i].gameObject);

        return existing.Length;
    }

    private static void ShowNamePill(MapHotspot hotspot)
    {
        GameObject pill = ReadReference<GameObject>(new SerializedObject(hotspot), NamePillField);

        if (pill != null)
            pill.SetActive(true);
    }

    /// <summary>
    /// Placement objects are named "NN TourAssetName", NN being the catalog entry index.
    /// </summary>
    private static string BuildPlacementName(int entryIndex, TourData tour)
    {
        return entryIndex.ToString("00") + " " + tour.name;
    }

    /// <summary>
    /// Maps a placement object back to its catalog entry from its name, and checks the entry
    /// still holds the same tour (the catalog may have been edited since Spawn).
    /// </summary>
    private static bool TryResolveEntryIndex(string objectName, TourCatalog catalog, out int entryIndex, out string problem)
    {
        entryIndex = -1;
        problem = null;

        int space = objectName.IndexOf(' ');

        if (space <= 0 || !int.TryParse(objectName.Substring(0, space), out entryIndex))
        {
            problem = "its name does not start with a catalog index. Was it renamed? Run Spawn again.";
            return false;
        }

        if (entryIndex < 0 || entryIndex >= catalog.entries.Count)
        {
            problem = $"catalog index {entryIndex} is out of range. Run Spawn again.";
            return false;
        }

        TourCatalogEntry entry = catalog.entries[entryIndex];

        if (entry == null || entry.tourData == null)
        {
            problem = $"catalog entry {entryIndex} has no TourData.";
            return false;
        }

        if (objectName != BuildPlacementName(entryIndex, entry.tourData))
        {
            problem = $"catalog entry {entryIndex} is now '{entry.tourData.name}'. The catalog changed since Spawn; run Spawn again.";
            return false;
        }

        return true;
    }

    private static float Round4(float value)
    {
        return Mathf.Round(value * 10000f) / 10000f;
    }

    #endregion
}
