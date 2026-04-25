
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Registry of all tours available from the main menu.
/// One TourCatalog asset exists per project and is referenced by the menu UI.
/// Designers add new buildings by appending entries here
/// </summary>
[CreateAssetMenu(fileName = "New Tour Catalog", menuName = "Tour/Tour Catalog", order = 10)]
public class TourCatalog : ScriptableObject
{
    [Header("Menu Presentation")]

    [Tooltip("Optional header text displayed at the top of the main menu.")]
    public string menuTitle;

    [Tooltip("2D campus map image displayed in the map view of the main menu.")]
    public Sprite campusMap;

    [Header("Entries")]

    [Tooltip("Tours available from the main menu. Order determines list view ordering.")]
    public List<TourCatalogEntry> entries = new List<TourCatalogEntry>();

    public TourCatalogEntry GetEntryForTour(TourData tour)
    {
        if (tour == null)
            return null;

        for (int i = 0; i < entries.Count; i++)
        {
            TourCatalogEntry entry = entries[i];

            if (entry == null || entry.tourData == null)
                continue;

            if (entry.tourData == tour)
                return entry;
        }

        return null;
    }
}

[System.Serializable]
public class TourCatalogEntry
{
    [Tooltip("The tour this entry represents. Must be assigned for the entry to appear in the menu.")]
    public TourData tourData;

    [Tooltip("Anchored position of this tour's hotspot on the campus map, in the map RectTransform's local space.")]

    public Vector2 mapHotspotPosition;
}
