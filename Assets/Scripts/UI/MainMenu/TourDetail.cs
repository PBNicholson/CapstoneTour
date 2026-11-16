using UnityEngine.UI;
using TMPro;

/// <summary>
/// Shared display rule for one tour's detail content in the menu: thumbnail, name and
/// description. Used by the tour list's detail region (ListViewPanel) and by the campus
/// map's detail card (CampusMapPanel), so both show the same content the same way.
///
/// The thumbnail follows TourThumbnail (cover-cropped, placeholder frame when empty).
/// The description scroll position is reset to the top on every call, so a newly selected
/// tour never opens at the previous tour's scroll offset.
/// </summary>
public static class TourDetail
{
    /// <summary>
    /// Fills the given UI elements from the tour. Any element may be null and is then skipped.
    /// Call it after the container holding these elements has been activated.
    /// </summary>
    /// <param name="data">The tour to show. Null clears the name and description and shows the thumbnail placeholder.</param>
    /// <param name="thumbnailImage">Photo Image inside a thumbnail frame. See TourThumbnail.</param>
    /// <param name="nameText">Text that receives TourData.buildingName.</param>
    /// <param name="descriptionText">Text that receives TourData.description.</param>
    /// <param name="descriptionScrollRect">ScrollRect that scrolls the description. Reset to the top.</param>
    public static void Apply(
        TourData data,
        Image thumbnailImage,
        TMP_Text nameText,
        TMP_Text descriptionText,
        ScrollRect descriptionScrollRect)
    {
        TourThumbnail.Apply(thumbnailImage, data != null ? data.buildingThumbnail : null);

        if (nameText != null)
            nameText.text = data != null ? data.buildingName : string.Empty;

        if (descriptionText != null)
            descriptionText.text = data != null ? data.description : string.Empty;

        if (descriptionScrollRect != null)
        {
            descriptionScrollRect.StopMovement();
            descriptionScrollRect.verticalNormalizedPosition = 1f;
        }
    }
}
