using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>
/// One-shot import settings for the SUNY Canton restyle sprites
/// (UI style spec v1, section 4). Menu: Tools > Canton > Apply Sprite Import Settings.
///
/// Applies to every PNG in <see cref="Folder"/> whose file name is in <see cref="Borders"/>:
/// Sprite (2D and UI), Single, PPU 200, Full Rect, centre pivot, no physics shape,
/// sRGB, alpha is transparency, no Read/Write, no mip maps, Clamp, Bilinear,
/// max size 2048, uncompressed, and clears any WebGL platform override.
/// Runs only when clicked, so later deliberate changes are not reverted.
/// Logs a read-back of every file so the result can be checked against the spec table.
/// </summary>
public static class CantonSpriteImport
{
    private const string Folder = "Assets/UI Assets/Canton";
    private const float PixelsPerUnit = 200f;

    // 9-slice border in source pixels (equal on all four sides). 0 = no border.
    private static readonly Dictionary<string, int> Borders = new Dictionary<string, int>
    {
        { "ui_rect_r6", 16 },
        { "ui_rect_r6_outline", 16 },
        { "ui_rect_r8", 20 },
        { "ui_rect_r8_outline1", 20 },
        { "ui_rect_r8_outline3", 20 },
        { "ui_pill", 63 },
        { "ui_pill_small", 11 },
        { "ui_circle", 0 },
        { "icon_close", 0 },
        { "icon_arrow_left", 0 },
        { "icon_plus", 0 },
        { "icon_minus", 0 },
        { "icon_info", 0 },
        { "icon_look", 0 },
        { "icon_move", 0 },
        { "icon_zoom", 0 },
        { "compass_dial", 0 },
        { "compass_needle", 0 },
        { "reticle_ring_outlined", 0 },
        { "reticle_ring_dot_outlined", 0 },
        { "CursorDotAssetCropped", 0 },
        { "SUNYCanton-Wordmark-web", 0 }, // Stage 4
    };

    [MenuItem("Tools/Canton/Apply Sprite Import Settings")]
    private static void Apply()
    {
        if (!AssetDatabase.IsValidFolder(Folder))
        {
            Debug.LogError($"[CantonSpriteImport] Folder not found: {Folder}");
            return;
        }

        string[] guids = AssetDatabase.FindAssets("t:Texture2D", new[] { Folder });
        var found = new HashSet<string>();
        var log = new StringBuilder("[CantonSpriteImport] Results (name | PPU | border L,B,R,T | mesh | filter | compression | mips | WebGL override):\n");
        int applied = 0;

        AssetDatabase.StartAssetEditing();
        try
        {
            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (Path.GetDirectoryName(path).Replace('\\', '/') != Folder) continue; // top level only
                if (!path.EndsWith(".png", System.StringComparison.OrdinalIgnoreCase)) continue;

                string name = Path.GetFileNameWithoutExtension(path);
                if (!Borders.TryGetValue(name, out int border))
                {
                    Debug.LogWarning($"[CantonSpriteImport] Skipped, not in the spec table: {path}");
                    continue;
                }

                var importer = AssetImporter.GetAtPath(path) as TextureImporter;
                if (importer == null) continue;
                found.Add(name);

                importer.textureType = TextureImporterType.Sprite;
                importer.spriteImportMode = SpriteImportMode.Single;
                importer.spritePixelsPerUnit = PixelsPerUnit;
                importer.spriteBorder = new Vector4(border, border, border, border); // x=L, y=B, z=R, w=T
                importer.sRGBTexture = true;
                importer.alphaSource = TextureImporterAlphaSource.FromInput;
                importer.alphaIsTransparency = true;
                importer.isReadable = false;
                importer.mipmapEnabled = false;
                importer.wrapMode = TextureWrapMode.Clamp;
                importer.filterMode = FilterMode.Bilinear;
                importer.maxTextureSize = 2048;
                importer.textureCompression = TextureImporterCompression.Uncompressed;
                importer.ClearPlatformTextureSettings("WebGL");

                var settings = new TextureImporterSettings();
                importer.ReadTextureSettings(settings);
                settings.spriteMeshType = SpriteMeshType.FullRect;
                settings.spriteAlignment = (int)SpriteAlignment.Center;
                settings.spriteGenerateFallbackPhysicsShape = false;
                importer.SetTextureSettings(settings);

                importer.SaveAndReimport();
                applied++;
            }
        }
        finally
        {
            AssetDatabase.StopAssetEditing();
            AssetDatabase.Refresh();
        }

        // Read back what Unity actually stored.
        foreach (string guid in AssetDatabase.FindAssets("t:Texture2D", new[] { Folder }))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            var importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer == null || !found.Contains(Path.GetFileNameWithoutExtension(path))) continue;

            var s = new TextureImporterSettings();
            importer.ReadTextureSettings(s);
            Vector4 b = importer.spriteBorder;
            bool webglOverride = importer.GetPlatformTextureSettings("WebGL").overridden;
            log.AppendLine($"{Path.GetFileName(path)} | {importer.spritePixelsPerUnit} | {b.x},{b.y},{b.z},{b.w} | " +
                           $"{s.spriteMeshType} | {importer.filterMode} | {importer.textureCompression} | " +
                           $"{importer.mipmapEnabled} | {webglOverride}");
        }

        foreach (var kv in Borders)
        {
            if (!found.Contains(kv.Key))
                log.AppendLine($"MISSING (expected in folder): {kv.Key}.png");
        }

        log.Insert(0, $"Applied to {applied} file(s).\n");
        Debug.Log(log.ToString());
    }
}
