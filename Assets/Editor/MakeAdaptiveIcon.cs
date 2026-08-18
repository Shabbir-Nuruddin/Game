using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEngine;

namespace TrustIssues.EditorTools
{
    /// <summary>
    /// Builds the Android ADAPTIVE icon (separate background + foreground
    /// layers) from the game's existing app icon painting, and wires both
    /// layers into every adaptive icon slot Android asks for.
    ///
    /// Android masks adaptive icons into different shapes (circle, squircle,
    /// rounded square) per device skin, so content must sit inside the
    /// middle ~66% "safe zone" or it gets clipped by the mask:
    ///   - Background: a plain sky gradient (no scene detail — safe to crop).
    ///   - Foreground: the moon/castle painting shrunk into the safe zone,
    ///     on a transparent layer.
    ///
    /// Menu: Trust Issues → Generate Adaptive Icon. Also runnable headless via
    ///   -executeMethod TrustIssues.EditorTools.MakeAdaptiveIcon.Build
    /// Requires Assets/AppIcon/app_icon.png to already exist (run
    /// MakeAppIcon.Build first if it doesn't).
    /// </summary>
    public static class MakeAdaptiveIcon
    {
        const int S = 512;
        const string SourcePath = "Assets/AppIcon/app_icon.png";
        const string BgPath = "Assets/AppIcon/app_icon_bg.png";
        const string FgPath = "Assets/AppIcon/app_icon_fg.png";

        [MenuItem("Trust Issues/Generate Adaptive Icon")]
        public static void Build()
        {
            if (!File.Exists(SourcePath))
            {
                Debug.LogError($"[MakeAdaptiveIcon] {SourcePath} not found — run " +
                    "Trust Issues → Generate App Icon first.");
                return;
            }
            var srcPx = ReadPngPixels(SourcePath);

            // ---- background: flat sky gradient, same palette as the source art ----
            Color skyTop = Hex("070510"), skyLow = Hex("300A14");
            var bgPx = new Color[S * S];
            for (int y = 0; y < S; y++)
            {
                var row = Color.Lerp(skyLow, skyTop, (float)y / (S - 1));
                for (int x = 0; x < S; x++) bgPx[y * S + x] = row;
            }
            WritePng(bgPx, BgPath, hasAlpha: false);

            // ---- foreground: source art shrunk into the ~66% safe zone, transparent pad ----
            var fgPx = new Color[S * S];
            for (int i = 0; i < fgPx.Length; i++) fgPx[i] = new Color(0, 0, 0, 0);
            const float safeScale = 0.66f;
            int inset = Mathf.RoundToInt(S * (1f - safeScale) / 2f);
            int innerSize = S - inset * 2;
            for (int y = 0; y < innerSize; y++)
            {
                int sy = Mathf.Clamp(Mathf.RoundToInt(y * (float)S / innerSize), 0, S - 1);
                for (int x = 0; x < innerSize; x++)
                {
                    int sx = Mathf.Clamp(Mathf.RoundToInt(x * (float)S / innerSize), 0, S - 1);
                    fgPx[(y + inset) * S + (x + inset)] = srcPx[sy * S + sx];
                }
            }
            WritePng(fgPx, FgPath, hasAlpha: true);

            var source = AssetDatabase.LoadAssetAtPath<Texture2D>(SourcePath);
            var bg = AssetDatabase.LoadAssetAtPath<Texture2D>(BgPath);
            var fg = AssetDatabase.LoadAssetAtPath<Texture2D>(FgPath);

            // Android exposes its icon slots as PlatformIconKind instances (not a
            // plain enum) via GetSupportedIconKinds — pick out Adaptive/Round/Legacy.
            var kinds = PlayerSettings.GetSupportedIconKinds(NamedBuildTarget.Android);
            var adaptiveKind = kinds.First(k => k.ToString().StartsWith("Adaptive"));
            var roundKind = kinds.First(k => k.ToString().StartsWith("Round"));
            var legacyKind = kinds.First(k => k.ToString().StartsWith("Legacy"));

            var adaptiveIcons = PlayerSettings.GetPlatformIcons(NamedBuildTarget.Android, adaptiveKind);
            foreach (var icon in adaptiveIcons) icon.SetTextures(new[] { bg, fg });
            PlayerSettings.SetPlatformIcons(NamedBuildTarget.Android, adaptiveKind, adaptiveIcons);

            // Round + legacy icon slots still exist for older devices — point them
            // at the plain (non-adaptive) app icon so they're not left empty too.
            var roundIcons = PlayerSettings.GetPlatformIcons(NamedBuildTarget.Android, roundKind);
            foreach (var icon in roundIcons) icon.SetTextures(new[] { source });
            PlayerSettings.SetPlatformIcons(NamedBuildTarget.Android, roundKind, roundIcons);

            var legacyIcons = PlayerSettings.GetPlatformIcons(NamedBuildTarget.Android, legacyKind);
            foreach (var icon in legacyIcons) icon.SetTextures(new[] { source });
            PlayerSettings.SetPlatformIcons(NamedBuildTarget.Android, legacyKind, legacyIcons);

            AssetDatabase.SaveAssets();
            Debug.Log("[MakeAdaptiveIcon] wrote background/foreground layers and wired all Android icon slots.");
        }

        static Color[] ReadPngPixels(string path)
        {
            var bytes = File.ReadAllBytes(path);
            var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            tex.LoadImage(bytes);
            if (tex.width != S || tex.height != S)
                Debug.LogWarning($"[MakeAdaptiveIcon] {path} is {tex.width}x{tex.height}, expected {S}x{S}.");
            var px = tex.GetPixels();
            Object.DestroyImmediate(tex);
            return px;
        }

        static void WritePng(Color[] px, string path, bool hasAlpha)
        {
            var tex = new Texture2D(S, S, TextureFormat.RGBA32, false);
            tex.SetPixels(px); tex.Apply();
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllBytes(path, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);

            var imp = (TextureImporter)AssetImporter.GetAtPath(path);
            imp.textureType = TextureImporterType.Default;
            imp.alphaIsTransparency = hasAlpha;
            imp.textureCompression = TextureImporterCompression.Uncompressed;
            imp.mipmapEnabled = false;
            imp.SaveAndReimport();
        }

        static Color Hex(string h)
        {
            ColorUtility.TryParseHtmlString("#" + h, out var c);
            return c;
        }
    }
}
