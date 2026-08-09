#if UNITY_EDITOR
using System.IO;
using TMPro;
using TrustIssues;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

public static class WardrobeAssetBuilder
{
    const string Root = "Assets/Resources/Wardrobe";

    [MenuItem("Trust Issues/Rebuild Wardrobe Assets")]
    public static void Build()
    {
        if (TMP_Settings.instance == null)
        {
            TMP_PackageResourceImporter.ImportResources(true, false, false);
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        }
        Directory.CreateDirectory(Root + "/Background"); Directory.CreateDirectory(Root + "/Frames");
        Directory.CreateDirectory(Root + "/Icons"); Directory.CreateDirectory(Root + "/Avatars"); Directory.CreateDirectory(Root + "/Prefabs"); Directory.CreateDirectory(Root + "/Fonts");
        MakeFrames(); MakeIcons(); MakeAvatars(); AssetDatabase.Refresh(); ConfigureSprites(); AssetDatabase.Refresh(); MakeFont(); MakePrefab();
        AssetDatabase.SaveAssets(); Debug.Log("WARDROBE_ASSETS_READY");
    }

    static void MakeFrames()
    {
        Frame("avatar_card", 96, 96, new Color32(150, 105, 30, 255), new Color32(5, 3, 7, 245), 2);
        Frame("avatar_card_selected", 96, 96, new Color32(225, 36, 48, 255), new Color32(0, 0, 0, 0), 2);
        Frame("tab_frame", 128, 48, new Color32(156, 111, 32, 255), new Color32(7, 4, 7, 245), 2);
        Frame("tab_selected", 128, 48, new Color32(226, 40, 49, 255), new Color32(42, 5, 9, 245), 3);
        Frame("back_button", 128, 48, new Color32(156, 111, 32, 255), new Color32(7, 4, 7, 245), 2);
    }

    static void Frame(string name, int w, int h, Color32 border, Color32 fill, int thick)
    {
        var t = New(w, h); var p = new Color32[w * h];
        for (int y = 0; y < h; y++) for (int x = 0; x < w; x++)
        {
            bool edge = x < thick || y < thick || x >= w - thick || y >= h - thick;
            bool corner = (x + y < 11) || (x + h - y < 11) || (w - x + y < 11) || (w - x + h - y < 11);
            p[y * w + x] = corner ? new Color32(0, 0, 0, 0) : edge ? border : fill;
        }
        t.SetPixels32(p); t.Apply(); Write(name, t, Root + "/Frames/");
    }

    static void MakeIcons()
    {
        string[] names = { "icon_castle", "icon_endless", "icon_bloodmoon", "icon_skull", "icon_bestiary", "icon_crown" };
        for (int n = 0; n < names.Length; n++)
        {
            var t = New(48, 48); var p = new Color32[48 * 48];
            for (int y = 5; y < 43; y++) for (int x = 5; x < 43; x++)
            {
                bool on = n switch
                {
                    0 => y < 16 && ((x > 7 && x < 15) || (x > 20 && x < 28) || (x > 33 && x < 41)) || y >= 16 && y < 39 && x > 7 && x < 41,
                    1 => Mathf.Abs(Vector2.Distance(new Vector2(x,y), new Vector2(24,24)) - 15) < 3 || (x > 21 && x < 27 && y > 8 && y < 40),
                    2 => Vector2.Distance(new Vector2(x,y), new Vector2(24,25)) < 14 && !(x > 25 && y > 25),
                    3 => Vector2.Distance(new Vector2(x,y), new Vector2(24,26)) < 15 || (y < 13 && x > 14 && x < 34),
                    4 => (x > 8 && x < 22 || x > 26 && x < 40) && y > 7 && y < 40,
                    _ => y > 14 && y < 35 && x > 8 && x < 40 || (y >= 8 && y <= 17 && (Mathf.Abs(x-10)<4 || Mathf.Abs(x-24)<4 || Mathf.Abs(x-38)<4))
                };
                if (on) p[y * 48 + x] = new Color32(255, 255, 255, 255);
            }
            t.SetPixels32(p); t.Apply(); Write(names[n], t, Root + "/Icons/");
        }
    }

    static void MakeAvatars()
    {
        byte[] vampire = File.ReadAllBytes("Assets/Resources/art/vamp_idle.png");
        byte[] pink = File.ReadAllBytes("Assets/Resources/art/pinkman_idle.png");
        string[] names = { "heir", "crimson_lord", "spectre", "golden_cursed", "shadowbound", "pink_menace", "ashen_slayer", "bone_pale", "nosferatu", "royal_blood" };
        string[] colors = { "FFFFFF", "FF5555", "A36BFF", "F1B329", "6B4B87", "FF64B3", "F2762A", "D7C9AD", "42B6A1", "D62E39" };
        for (int i = 0; i < names.Length; i++)
        {
            var src = new Texture2D(2, 2); src.LoadImage(i == 5 || i == 9 ? pink : vampire);
            var tint = Color.white; ColorUtility.TryParseHtmlString("#" + colors[i], out tint);
            var pixels = src.GetPixels();
            for (int j = 0; j < pixels.Length; j++) if (pixels[j].a > 0) pixels[j] = new Color(Mathf.Lerp(pixels[j].r, tint.r, .48f), Mathf.Lerp(pixels[j].g, tint.g, .48f), Mathf.Lerp(pixels[j].b, tint.b, .48f), pixels[j].a);
            src.SetPixels(pixels); src.Apply(); Write(names[i], src, Root + "/Avatars/");
        }
    }

    static void ConfigureSprites()
    {
        foreach (string path in Directory.GetFiles(Root, "*.png", SearchOption.AllDirectories))
        {
            string p = path.Replace('\\', '/'); var i = AssetImporter.GetAtPath(p) as TextureImporter; if (i == null) continue;
            i.textureType = TextureImporterType.Sprite; i.spriteImportMode = SpriteImportMode.Single; i.alphaIsTransparency = true; i.mipmapEnabled = false;
            if (p.Contains("/Frames/")) i.spriteBorder = new Vector4(14, 14, 14, 14);
            i.SaveAndReimport();
        }
    }

    static void MakePrefab()
    {
        var root = new GameObject("AvatarCard", typeof(RectTransform), typeof(Image), typeof(Button), typeof(AvatarCardView));
        var rt = (RectTransform)root.transform; rt.sizeDelta = new Vector2(238, 278);
        var bg = root.GetComponent<Image>(); bg.sprite = Load("Frames/avatar_card"); bg.type = Image.Type.Sliced;
        var button = root.GetComponent<Button>(); button.targetGraphic = bg;
        var glow = ChildImage(root.transform, "SelectedGlow", Load("Frames/avatar_card_selected")); Stretch(glow.rectTransform, -4, 4); glow.type = Image.Type.Sliced;
        var avatar = ChildImage(root.transform, "AvatarImage", null); Pos(avatar.rectTransform, 0, 25, 178, 160); avatar.preserveAspect = true;
        var name = ChildText(root.transform, "NameText", 22); Pos(name.rectTransform, 0, 112, 220, 32);
        var icon = ChildImage(root.transform, "RequirementIcon", null); Pos(icon.rectTransform, -82, -78, 34, 34); icon.preserveAspect = true;
        var req = ChildText(root.transform, "RequirementText", 15); Pos(req.rectTransform, 18, -74, 160, 48);
        var divider = ChildImage(root.transform, "Divider", null); divider.color = new Color(.45f, .29f, .17f, .7f); Pos(divider.rectTransform, 0, -52, 194, 1);
        var equipped = ChildText(root.transform, "EquippedText", 14); Pos(equipped.rectTransform, 0, -118, 210, 26);
        var view = root.GetComponent<AvatarCardView>(); view.cardBackground = bg; view.selectedGlow = glow; view.avatarImage = avatar; view.nameText = name; view.requirementIcon = icon; view.requirementText = req; view.equippedText = equipped; view.button = button;
        PrefabUtility.SaveAsPrefabAsset(root, Root + "/Prefabs/AvatarCard.prefab"); Object.DestroyImmediate(root);
    }

    static void MakeFont()
    {
        string path = Root + "/Fonts/Gothic.asset";
        if (AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(path) != null) return;
        var source = AssetDatabase.LoadAssetAtPath<Font>("Assets/Resources/fonts/menu.ttf");
        if (source == null) return;
        var font = TMP_FontAsset.CreateFontAsset(source);
        AssetDatabase.CreateAsset(font, path);
        if (font.atlasTexture != null) AssetDatabase.AddObjectToAsset(font.atlasTexture, font);
        if (font.material != null) AssetDatabase.AddObjectToAsset(font.material, font);
        EditorUtility.SetDirty(font);
    }

    static Image ChildImage(Transform p, string n, Sprite s) { var g = new GameObject(n, typeof(RectTransform), typeof(Image)); g.transform.SetParent(p, false); var i = g.GetComponent<Image>(); i.sprite = s; i.raycastTarget = false; return i; }
    static TextMeshProUGUI ChildText(Transform p, string n, float size) { var g = new GameObject(n, typeof(RectTransform), typeof(TextMeshProUGUI)); g.transform.SetParent(p, false); var t = g.GetComponent<TextMeshProUGUI>(); t.fontSize = size; t.enableAutoSizing = true; t.fontSizeMin = 10; t.fontSizeMax = size; t.alignment = TextAlignmentOptions.Center; t.raycastTarget = false; return t; }
    static void Pos(RectTransform r, float x, float y, float w, float h) { r.anchorMin = r.anchorMax = new Vector2(.5f,.5f); r.anchoredPosition = new Vector2(x,y); r.sizeDelta = new Vector2(w,h); }
    static void Stretch(RectTransform r, float min, float max) { r.anchorMin = Vector2.zero; r.anchorMax = Vector2.one; r.offsetMin = new Vector2(min,min); r.offsetMax = new Vector2(max,max); }
    static Sprite Load(string p) => AssetDatabase.LoadAssetAtPath<Sprite>(Root + "/" + p + ".png");
    static Texture2D New(int w, int h) => new Texture2D(w, h, TextureFormat.RGBA32, false);
    static void Write(string n, Texture2D t, string dir) { File.WriteAllBytes(dir + n + ".png", t.EncodeToPNG()); Object.DestroyImmediate(t); }
}
#endif
