using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace TrustIssues
{
    /// <summary>Responsive Wardrobe assembled from real Unity UI and a reusable card prefab.</summary>
    public sealed class WardrobeUI : MonoBehaviour
    {
        static readonly Color Bone = Hex("F0E3CC"), Gold = Hex("C99A3B"), Blood = Hex("D32936");
        readonly List<Button> _tabs = new();
        RectTransform _grid;
        Action _back;
        int _tab;

        public static void Build(Transform parent, GameObject panel, Action onBack)
        {
            var go = new GameObject("WardrobeCanvas", typeof(RectTransform), typeof(WardrobeUI));
            go.transform.SetParent(parent, false); Stretch((RectTransform)go.transform);
            go.GetComponent<WardrobeUI>().Setup(onBack);
        }

        void Setup(Action onBack)
        {
            _back = onBack;
            var canvas = GetComponentInParent<Canvas>();
            if (canvas != null)
            {
                var s = canvas.GetComponent<CanvasScaler>() ?? canvas.gameObject.AddComponent<CanvasScaler>();
                s.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
                s.referenceResolution = new Vector2(1600, 900); s.matchWidthOrHeight = .5f;
            }
            var bg = NewImage("Background", transform, Load("Wardrobe/Background/wardrobe_bg"), Color.white); Stretch(bg.rectTransform); bg.transform.SetAsFirstSibling();
            var veil = NewImage("AtmosphericVeil", transform, null, new Color(.01f, .004f, .008f, .28f)); Stretch(veil.rectTransform);
            Header(); Tabs(); Back();
            _grid = NewRect("AvatarGrid", transform, new Vector2(.5f, .5f), new Vector2(0, -98), new Vector2(1280, 590));
            ShowTab(0);
        }

        void Header()
        {
            var h = NewRect("Header", transform, new Vector2(.5f, 1), new Vector2(0, -88), new Vector2(1000, 160));
            var title = NewText("WardrobeTitle", h, "WARDROBE", 76, Bone); title.fontStyle = FontStyles.Bold;
            title.rectTransform.anchorMin = new Vector2(0, .35f); title.rectTransform.anchorMax = Vector2.one; title.rectTransform.offsetMin = title.rectTransform.offsetMax = Vector2.zero;
            title.outlineColor = new Color(.35f, .02f, .03f, 1); title.outlineWidth = .18f;
            var sub = NewText("Subtitle", h, "DESCEND  •  DISTRUST  •  DIE", 22, Gold);
            sub.rectTransform.anchorMin = Vector2.zero; sub.rectTransform.anchorMax = new Vector2(1, .35f); sub.rectTransform.offsetMin = sub.rectTransform.offsetMax = Vector2.zero;
        }

        void Tabs()
        {
            var bar = NewRect("Tabs", transform, new Vector2(.5f, 1), new Vector2(0, -195), new Vector2(900, 58));
            var row = bar.gameObject.AddComponent<HorizontalLayoutGroup>(); row.spacing = 16; row.childControlWidth = row.childForceExpandWidth = true;
            string[] names = { "AVATAR", "AURA", "OUTFIT" };
            for (int i = 0; i < 3; i++)
            {
                int n = i; var go = new GameObject(names[i] + "Tab", typeof(RectTransform), typeof(Image), typeof(Button), typeof(LayoutElement)); go.transform.SetParent(bar, false);
                go.GetComponent<LayoutElement>().preferredHeight = 58;
                var img = go.GetComponent<Image>(); img.sprite = Load("Wardrobe/Frames/tab_frame"); img.type = Image.Type.Sliced;
                var btn = go.GetComponent<Button>(); btn.targetGraphic = img; btn.onClick.AddListener(() => ShowTab(n));
                var txt = NewText("Label", go.transform, names[i], 27, Gold); txt.fontStyle = FontStyles.Bold; Stretch(txt.rectTransform); _tabs.Add(btn);
            }
        }

        void ShowTab(int tab)
        {
            _tab = tab;
            for (int i = 0; i < 3; i++) { _tabs[i].GetComponent<Image>().sprite = Load(i == tab ? "Wardrobe/Frames/tab_selected" : "Wardrobe/Frames/tab_frame"); _tabs[i].GetComponentInChildren<TMP_Text>().color = i == tab ? Blood : Gold; }
            for (int i = _grid.childCount - 1; i >= 0; i--) Destroy(_grid.GetChild(i).gameObject);
            _grid.name = tab == 0 ? "AvatarGrid" : tab == 1 ? "AuraGrid" : "OutfitGrid";
            var layout = _grid.GetComponent<GridLayoutGroup>() ?? _grid.gameObject.AddComponent<GridLayoutGroup>();
            layout.enabled = tab != 1;
            layout.constraint = GridLayoutGroup.Constraint.FixedColumnCount; layout.constraintCount = 5; layout.cellSize = new Vector2(238, 278); layout.spacing = new Vector2(18, 16); layout.childAlignment = TextAnchor.MiddleCenter;
            if (tab == 0) Avatars(); else if (tab == 1) Auras(); else Cosmetics(false);
        }

        public void DevSelectTab(int tab) => ShowTab(Mathf.Clamp(tab, 0, 2));

        void Avatars()
        {
            for (int i = 0; i < Skins.All.Count; i++)
            {
                var d = Skins.All[i]; string id = d.id; bool open = Skins.IsUnlocked(d), worn = Skins.CurrentId == id;
                Card().Bind(d.name, d.unlockHint, Load("Wardrobe/AvatarsExact/" + AvatarFile(id)), Icon(d.unlockHint), Accent(i), Color.white, open, worn,
                    open ? (Action)(() => { Skins.Equip(id); ShowTab(_tab); }) : () => GameRoot.I?.ShowHint(d.unlockHint));
            }
        }

        void Cosmetics(bool aura)
        {
            var list = aura ? WardrobeCosmetics.Auras : WardrobeCosmetics.Outfits;
            foreach (var d in list)
            {
                string id = d.id; bool open = WardrobeCosmetics.IsUnlocked(d);
                bool worn = aura ? WardrobeCosmetics.CurrentAuraId == id : WardrobeCosmetics.CurrentOutfitId == id;
                Color tint = Color.white;
                Card().Bind(d.name, d.hint, Load("Wardrobe/OutfitsExact/" + id), Icon(d.hint), d.color.a > .05f ? d.color : Gold, tint, open, worn,
                    open ? (Action)(() => { if (aura) WardrobeCosmetics.EquipAura(id); else WardrobeCosmetics.EquipOutfit(id); ShowTab(_tab); }) : () => GameRoot.I?.ShowHint(d.hint));
            }
        }

        void Auras()
        {
            var list = WardrobeCosmetics.Auras;
            for (int sourceIndex = 1; sourceIndex < list.Count; sourceIndex++)
            {
                int visual = sourceIndex - 1;
                var d = list[sourceIndex]; string id = d.id;
                bool open = WardrobeCosmetics.IsUnlocked(d);
                bool worn = WardrobeCosmetics.CurrentAuraId == id;
                var card = Card();
                card.Bind(d.name, d.hint, Load("Wardrobe/AurasExact/" + id), Icon(d.hint), d.color,
                    Color.white, open, worn,
                    open ? (Action)(() => { WardrobeCosmetics.EquipAura(id); ShowTab(_tab); }) : () => GameRoot.I?.ShowHint(d.hint));

                if (WardrobeCosmetics.CurrentAuraId == "none" && id == "gilded")
                    card.selectedGlow.enabled = true;

                var rt = (RectTransform)card.transform;
                if (visual < 5)
                {
                    rt.anchoredPosition = new Vector2((visual - 2) * 252f, 147f);
                    rt.sizeDelta = new Vector2(238, 278);
                }
                else if (visual < 8)
                {
                    rt.anchoredPosition = new Vector2(-490f + (visual - 5) * 270f, -147f);
                    rt.sizeDelta = new Vector2(254, 278);
                }
                else
                {
                    rt.anchoredPosition = new Vector2(398f, -147f);
                    rt.sizeDelta = new Vector2(492, 278);
                    card.avatarImage.rectTransform.sizeDelta = new Vector2(390, 160);
                    card.requirementIcon.rectTransform.anchoredPosition = new Vector2(-155, -78);
                    card.requirementText.rectTransform.sizeDelta = new Vector2(300, 48);
                    card.requirementText.rectTransform.anchoredPosition = new Vector2(35, -74);
                }
            }
        }

        AvatarCardView Card()
        {
            var p = Resources.Load<AvatarCardView>("Wardrobe/Prefabs/AvatarCard");
            if (p == null) throw new InvalidOperationException("Wardrobe/Prefabs/AvatarCard is missing");
            return Instantiate(p, _grid, false);
        }

        void Back()
        {
            var go = new GameObject("BackButton", typeof(RectTransform), typeof(Image), typeof(Button)); go.transform.SetParent(transform, false);
            var r = (RectTransform)go.transform; r.anchorMin = r.anchorMax = Vector2.zero; r.pivot = Vector2.zero; r.anchoredPosition = new Vector2(28, 22); r.sizeDelta = new Vector2(190, 56);
            var img = go.GetComponent<Image>(); img.sprite = Load("Wardrobe/Frames/back_button"); img.type = Image.Type.Sliced;
            go.GetComponent<Button>().onClick.AddListener(() => _back?.Invoke()); var t = NewText("Label", go.transform, "‹  BACK", 25, Gold); t.fontStyle = FontStyles.Bold; Stretch(t.rectTransform);
        }

        static string AvatarFile(string id) => id switch { "crimson" => "crimson_lord", "golden" => "golden_cursed", "shadow" => "shadowbound", "pink" => "pink_menace", "ash" => "ashen_slayer", "bone" => "bone_pale", "royal" => "royal_blood", _ => id };
        static Color Accent(int i) { Color[] c = { Gold, Hex("E63B43"), Hex("629EEA"), Hex("E5A51D"), Hex("B96AE5"), Hex("ED5AA4"), Hex("F07823"), Hex("D8C9A7"), Hex("49C2A8"), Hex("D8343D") }; return c[i]; }
        static Sprite Icon(string s) { s = s.ToLowerInvariant(); string n = s.Contains("castle") ? "icon_castle" : s.Contains("endless") ? "icon_endless" : s.Contains("blood moon") ? "icon_bloodmoon" : s.Contains("die") ? "icon_skull" : s.Contains("bestiary") || s.Contains("trap") ? "icon_bestiary" : "icon_crown"; return Load("Wardrobe/Icons/" + n); }
        static Sprite Load(string p) => Resources.Load<Sprite>(p);
        static Color Hex(string h) { ColorUtility.TryParseHtmlString("#" + h, out var c); return c; }
        static RectTransform NewRect(string n, Transform p, Vector2 anchor, Vector2 pos, Vector2 size) { var g = new GameObject(n, typeof(RectTransform)); g.transform.SetParent(p, false); var r = (RectTransform)g.transform; r.anchorMin = r.anchorMax = anchor; r.anchoredPosition = pos; r.sizeDelta = size; return r; }
        static Image NewImage(string n, Transform p, Sprite s, Color c) { var g = new GameObject(n, typeof(RectTransform), typeof(Image)); g.transform.SetParent(p, false); var i = g.GetComponent<Image>(); i.sprite = s; i.color = c; i.raycastTarget = false; return i; }
        static TMP_Text NewText(string n, Transform p, string value, float size, Color color) { var g = new GameObject(n, typeof(RectTransform), typeof(TextMeshProUGUI)); g.transform.SetParent(p, false); var t = g.GetComponent<TextMeshProUGUI>(); t.text = value; t.fontSize = size; t.color = color; t.alignment = TextAlignmentOptions.Center; t.enableAutoSizing = true; t.fontSizeMin = Mathf.Max(10, size * .52f); t.fontSizeMax = size; t.raycastTarget = false; var gothic = Resources.Load<TMP_FontAsset>("Wardrobe/Fonts/Gothic"); if (gothic != null) t.font = gothic; return t; }
        static void Stretch(RectTransform r) { r.anchorMin = Vector2.zero; r.anchorMax = Vector2.one; r.offsetMin = r.offsetMax = Vector2.zero; }
    }
}
