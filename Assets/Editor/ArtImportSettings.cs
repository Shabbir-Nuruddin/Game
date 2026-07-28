using UnityEditor;
using UnityEngine;

namespace TrustIssues
{
    /// <summary>
    /// Forces crisp pixel-art import settings on every texture under
    /// Resources/art and clean audio import on Resources/audio. This is what
    /// makes copied asset-pack PNGs render SHARP in a WebGL build (Point filter,
    /// no compression) instead of the blurry default. Runs automatically on
    /// import — no menu click, no manual per-file fiddling. Editor-only, so it is
    /// excluded from the player build.
    /// </summary>
    public class ArtImportSettings : AssetPostprocessor
    {
        static bool InResourcesArt(string p) =>
            p.Replace('\\', '/').Contains("/Resources/art/");

        void OnPreprocessTexture()
        {
            if (!InResourcesArt(assetPath)) return;
            var ti = (TextureImporter)assetImporter;
            ti.textureType = TextureImporterType.Sprite;
            ti.spriteImportMode = SpriteImportMode.Single; // sheets are sliced in code
            // Point filtering is right for the pixel-art sprites, but art/ui holds
            // pieces cut straight out of the painted reference (the control rings,
            // the portrait). Those are smooth, gradient-heavy and always drawn at a
            // reduced size, so Point makes their rims crawl and alias — they want
            // bilinear.
            bool painted = assetPath.Replace('\\', '/').Contains("/Resources/art/ui/");
            ti.filterMode = painted ? FilterMode.Bilinear : FilterMode.Point;
            ti.mipmapEnabled = false;
            ti.alphaIsTransparency = true;
            ti.spritePixelsPerUnit = 100;                  // scale is driven in code
            ti.textureCompression = TextureImporterCompression.Uncompressed;

            var def = ti.GetDefaultPlatformTextureSettings();
            def.textureCompression = TextureImporterCompression.Uncompressed;
            ti.SetPlatformTextureSettings(def);
        }
    }
}
