using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Shared display rule for TourData.buildingThumbnail in the menu.
///
/// Expected hierarchy: a frame object (placeholder-coloured Image plus RectMask2D) with the
/// photo Image as its child. The photo Image has an AspectRatioFitter set to Envelope Parent
/// and Preserve Aspect switched off.
///
/// With no sprite the photo Image is disabled, so the frame's placeholder colour shows.
/// With a sprite the fitter's aspect ratio is set from the sprite, so the photo covers the
/// frame and the frame's RectMask2D crops the overflow.
/// </summary>
public static class TourThumbnail
{
    /// <summary>
    /// Assigns the sprite to the photo Image and applies the rule above.
    /// </summary>
    /// <param name="photoImage">The photo Image inside the frame. Null is ignored.</param>
    /// <param name="sprite">The thumbnail to show, or null to show the placeholder.</param>
    public static void Apply(Image photoImage, Sprite sprite)
    {
        if (photoImage == null)
            return;

        photoImage.sprite = sprite;
        photoImage.enabled = sprite != null;

        if (sprite == null)
            return;

        AspectRatioFitter fitter = photoImage.GetComponent<AspectRatioFitter>();

        if (fitter != null && sprite.rect.height > 0f)
            fitter.aspectRatio = sprite.rect.width / sprite.rect.height;
    }
}
