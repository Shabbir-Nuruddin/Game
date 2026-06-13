using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace WordBloom
{
    /// <summary>
    /// The whole game, built from code: a Home screen and a Word-Cookies-style
    /// gameplay screen with a tap-to-spell letter wheel, animated answer tiles,
    /// coins, hints, a level-complete celebration, and saved progress.
    ///
    /// Attach this to ONE empty GameObject in the scene. That's the only setup.
    /// </summary>
    public class Game : MonoBehaviour
    {
        // ---- saved progress ----
        int _level;   // 0-based index of current level
        int _coins;

        // ---- runtime ----
        Canvas _canvas;
        GameObject _screen;            // current screen root (Home or Game)
        WordPuzzle _puzzle;
        readonly List<(char ch, Image img, Text txt, int index)> _wheel = new();
        readonly List<int> _selected = new();
        Text _currentWordText;
        Text _coinsText;
        readonly Dictionary<string, Text[]> _slotTiles = new();   // target -> tiles
        readonly Dictionary<string, HashSet<int>> _hintsShown = new();
        RectTransform _currentWordBar;

        const int HintCost = 25;

        void Start()
        {
            _level = PlayerPrefs.GetInt("wb_level", 0);
            _coins = PlayerPrefs.GetInt("wb_coins", 100);
            BuildCanvas();
            ShowHome();
        }

        void Save()
        {
            PlayerPrefs.SetInt("wb_level", _level);
            PlayerPrefs.SetInt("wb_coins", _coins);
            PlayerPrefs.Save();
        }

        void BuildCanvas()
        {
            if (Camera.main != null) Camera.main.backgroundColor = Brand.Bg;

            var go = new GameObject("Canvas");
            _canvas = go.AddComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = go.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1080, 1920);
            scaler.matchWidthOrHeight = 0.5f;
            go.AddComponent<GraphicRaycaster>();
        }

        // Swap screens with a quick fade so transitions feel intentional.
        void SwapScreen(System.Action build)
        {
            if (_screen != null) Destroy(_screen);
            _selected.Clear();
            _wheel.Clear();
            _slotTiles.Clear();

            _screen = new GameObject("Screen", typeof(RectTransform));
            _screen.transform.SetParent(_canvas.transform, false);
            Brand.Stretch(_screen.GetComponent<RectTransform>());

            var bg = Brand.Panel(_screen.transform, Brand.Bg, "Bg");
            Brand.Stretch(bg.rectTransform);

            build();

            var cg = _screen.AddComponent<CanvasGroup>();
            StartCoroutine(Brand.Fade(cg, 0f, 1f, 0.22f));
        }

        // =====================================================================
        //  HOME SCREEN
        // =====================================================================
        void ShowHome()
        {
            SwapScreen(() =>
            {
                var root = _screen.transform;

                // Decorative top blob (warm), purely cosmetic.
                var top = Brand.Panel(root, Brand.BgDeep, "TopBlob");
                Brand.Place(top.rectTransform, new Vector2(0.5f, 1f), new Vector2(0, 60),
                            new Vector2(1300, 900));

                var title = Brand.Label(root, Brand.Name, 150, FontStyle.Bold, Brand.Primary, "Title");
                Brand.Place(title.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0, 520),
                            new Vector2(1000, 220));
                StartCoroutine(Brand.PopIn(title.transform, 0.45f));

                var tag = Brand.Label(root, Brand.Tagline, 52, FontStyle.Italic, Brand.InkSoft, "Tag");
                Brand.Place(tag.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0, 390),
                            new Vector2(900, 80));

                // A little growing-letters motif.
                var motif = Brand.Label(root, "A  B  C", 90, FontStyle.Bold, Brand.Leaf, "Motif");
                Brand.Place(motif.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0, 210),
                            new Vector2(700, 140));
                StartCoroutine(Brand.PopIn(motif.transform, 0.4f, 0.15f));

                // PLAY button (big, friendly).
                var (pImg, pBtn, _) = Brand.Button(root, "PLAY", Brand.Primary, 70);
                Brand.Soften(pImg);
                Brand.Place(pImg.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0, -150),
                            new Vector2(620, 170));
                pBtn.onClick.AddListener(() => ShowGame());
                StartCoroutine(Brand.PopIn(pImg.transform, 0.4f, 0.25f));

                // Progress line.
                var prog = Brand.Label(root, $"Level {_level + 1}", 50, FontStyle.Bold, Brand.Ink, "Prog");
                Brand.Place(prog.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0, -360),
                            new Vector2(700, 70));

                // Coins badge.
                BuildCoinsBadge(root, new Vector2(0.5f, 0.5f), new Vector2(0, -470));
            });
        }

        void BuildCoinsBadge(Transform root, Vector2 anchor, Vector2 pos)
        {
            var badge = Brand.Panel(root, Brand.BgDeep, "Coins");
            Brand.Soften(badge);
            Brand.Place(badge.rectTransform, anchor, pos, new Vector2(280, 90));
            var coin = Brand.Label(badge.transform, "★", 60, FontStyle.Bold, Brand.Gold, "CoinIcon");
            Brand.Place(coin.rectTransform, new Vector2(0f, 0.5f), new Vector2(50, 0), new Vector2(70, 70));
            _coinsText = Brand.Label(badge.transform, _coins.ToString(), 50, FontStyle.Bold, Brand.Ink, "CoinNum");
            Brand.Place(_coinsText.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(30, 0), new Vector2(180, 70));
        }

        // =====================================================================
        //  GAMEPLAY SCREEN
        // =====================================================================
        void ShowGame()
        {
            _puzzle = new WordPuzzle(Levels.Get(_level));
            if (!_hintsShown.ContainsKey(_puzzle.Level.Letters))
                _hintsShown.Clear();

            SwapScreen(() =>
            {
                var root = _screen.transform;

                // ---- top bar ----
                var (bImg, bBtn, _) = Brand.Button(root, "‹", Brand.BgDeep, 70, Brand.Ink);
                Brand.Place(bImg.rectTransform, new Vector2(0f, 1f), new Vector2(90, -90), new Vector2(110, 110));
                bBtn.onClick.AddListener(() => ShowHome());

                var lvl = Brand.Label(root, $"Level {_level + 1}", 56, FontStyle.Bold, Brand.Ink, "Lvl");
                Brand.Place(lvl.rectTransform, new Vector2(0.5f, 1f), new Vector2(0, -90), new Vector2(500, 90));

                BuildCoinsBadge(root, new Vector2(1f, 1f), new Vector2(-170, -90));

                // ---- answer slots ----
                BuildSlots(root);

                // ---- current word bar ----
                var bar = Brand.Panel(root, Brand.BgDeep, "WordBar");
                Brand.Soften(bar);
                _currentWordBar = bar.rectTransform;
                Brand.Place(_currentWordBar, new Vector2(0.5f, 0.5f), new Vector2(0, -180), new Vector2(760, 130));
                _currentWordText = Brand.Label(bar.transform, "", 84, FontStyle.Bold, Brand.Ink, "Current");
                Brand.Stretch(_currentWordText.rectTransform);

                // ---- letter wheel ----
                BuildWheel(root, new Vector2(0, -560));

                // ---- controls ----
                var (clrImg, clrBtn, _) = Brand.Button(root, "Clear", Brand.BgDeep, 44, Brand.Ink);
                Brand.Place(clrImg.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(-320, -560), new Vector2(190, 110));
                clrBtn.onClick.AddListener(ClearSelection);

                var (entImg, entBtn, _) = Brand.Button(root, "ENTER", Brand.Leaf, 48);
                Brand.Place(entImg.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(320, -560), new Vector2(220, 120));
                entBtn.onClick.AddListener(SubmitWord);

                var (hintImg, hintBtn, _) = Brand.Button(root, $"Hint  ★{HintCost}", Brand.Gold, 40, Brand.Ink);
                Brand.Soften(hintImg);
                Brand.Place(hintImg.rectTransform, new Vector2(0.5f, 1f), new Vector2(0, -210), new Vector2(300, 90));
                hintBtn.onClick.AddListener(UseHint);
            });
        }

        void BuildSlots(Transform root)
        {
            _slotTiles.Clear();
            var targets = _puzzle.Level.Targets;

            // Layout the word rows in the upper area, packed left-to-right and
            // wrapping to new rows. Tile size scales down a touch for long words.
            float areaTop = 560f, x = 0f, y = areaTop;
            float rowH = 96f, tile = 84f, gap = 10f, rowGap = 18f;
            float maxWidth = 980f;

            // Group rows: we lay each word as its own block; multiple short words
            // can share a horizontal line.
            float lineWidthUsed = 0f;
            var lineWords = new List<List<string>>();
            var line = new List<string>();
            foreach (var w in targets)
            {
                float wWidth = w.Length * (tile + gap) + 60f;
                if (lineWidthUsed + wWidth > maxWidth && line.Count > 0)
                {
                    lineWords.Add(line); line = new List<string>(); lineWidthUsed = 0f;
                }
                line.Add(w); lineWidthUsed += wWidth;
            }
            if (line.Count > 0) lineWords.Add(line);

            for (int r = 0; r < lineWords.Count; r++)
            {
                var words = lineWords[r];
                float totalW = 0f;
                foreach (var w in words) totalW += w.Length * (tile + gap) + 50f;
                float startX = -totalW / 2f;
                float cy = y - r * (rowH + rowGap);

                float cx = startX;
                foreach (var w in words)
                {
                    var tiles = new Text[w.Length];
                    for (int i = 0; i < w.Length; i++)
                    {
                        var cell = Brand.Panel(root, Brand.TileEmpty, $"Slot_{w}_{i}");
                        var ol = cell.gameObject.AddComponent<Outline>();
                        ol.effectColor = new Color(0, 0, 0, 0.10f);
                        Brand.Place(cell.rectTransform, new Vector2(0.5f, 0.5f),
                            new Vector2(cx + tile / 2f, cy), new Vector2(tile, tile));
                        var tx = Brand.Label(cell.transform, "", 56, FontStyle.Bold, Brand.Primary);
                        Brand.Stretch(tx.rectTransform);
                        tiles[i] = tx;
                        cx += tile + gap;
                    }
                    cx += 50f; // gap between words on the same line
                    _slotTiles[w] = tiles;
                }
            }

            // Re-show any letters already revealed by hints this session.
            foreach (var kv in _hintsShown)
                if (_slotTiles.TryGetValue(kv.Key, out var tiles))
                    foreach (int idx in kv.Value)
                        if (idx < tiles.Length) tiles[idx].text = kv.Key[idx].ToString();
        }

        void BuildWheel(Transform root, Vector2 center)
        {
            string letters = _puzzle.Level.Letters;
            int n = letters.Length;

            // A base disc behind the letters.
            var disc = Brand.Panel(root, Brand.Wheel, "Wheel");
            Brand.Soften(disc);
            Brand.Place(disc.rectTransform, new Vector2(0.5f, 0.5f), center, new Vector2(520, 520));

            float radius = n <= 3 ? 120f : 160f;
            for (int i = 0; i < n; i++)
            {
                float ang = Mathf.PI / 2f + i * (2f * Mathf.PI / n); // start at top
                float px = center.x + Mathf.Cos(ang) * radius;
                float py = center.y + Mathf.Sin(ang) * radius;

                int index = i;
                var img = Brand.Panel(root, Brand.Key, $"WLetter_{i}");
                Brand.Soften(img);
                Brand.Place(img.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(px, py),
                            new Vector2(140, 140));
                var btn = img.gameObject.AddComponent<Button>();
                btn.targetGraphic = img;
                var txt = Brand.Label(img.transform, letters[i].ToString(), 80, FontStyle.Bold, Brand.Ink);
                Brand.Stretch(txt.rectTransform);
                btn.onClick.AddListener(() => ToggleLetter(index));
                _wheel.Add((letters[i], img, txt, index));
            }
        }

        // ---------------- input ----------------
        void ToggleLetter(int index)
        {
            int at = _selected.IndexOf(index);
            if (at >= 0)
            {
                // Retrace: remove this letter and everything chosen after it.
                _selected.RemoveRange(at, _selected.Count - at);
            }
            else
            {
                _selected.Add(index);
            }
            RefreshSelection();
        }

        void ClearSelection()
        {
            _selected.Clear();
            RefreshSelection();
        }

        void RefreshSelection()
        {
            var sb = new System.Text.StringBuilder();
            foreach (int idx in _selected) sb.Append(_wheel[idx].ch);
            _currentWordText.text = sb.ToString();

            foreach (var w in _wheel)
            {
                bool on = _selected.Contains(w.index);
                w.img.color = on ? Brand.Primary : Brand.Key;
                w.txt.color = on ? Color.white : Brand.Ink;
            }
        }

        void SubmitWord()
        {
            if (_selected.Count == 0) return;
            var sb = new System.Text.StringBuilder();
            foreach (int idx in _selected) sb.Append(_wheel[idx].ch);
            string word = sb.ToString();

            var result = _puzzle.Submit(word);
            switch (result)
            {
                case SubmitResult.Found:
                    StartCoroutine(RevealWord(word));
                    break;
                case SubmitResult.Bonus:
                    _coins += 10;
                    UpdateCoins();
                    StartCoroutine(Brand.Punch(_currentWordBar, 0.16f));
                    FloatToast("+10 ★ bonus!");
                    break;
                case SubmitResult.AlreadyFound:
                    StartCoroutine(Brand.Punch(_currentWordBar));
                    break;
                default:
                    StartCoroutine(Brand.Shake(_currentWordBar));
                    break;
            }
            ClearSelection();
        }

        IEnumerator RevealWord(string word)
        {
            var tiles = _slotTiles[word];
            for (int i = 0; i < tiles.Length; i++)
            {
                tiles[i].text = word[i].ToString();
                var cell = tiles[i].transform.parent;
                ((Image)cell.GetComponent<Image>()).color = Brand.Leaf;
                tiles[i].color = Color.white;
                StartCoroutine(Brand.PopIn(cell, 0.22f));
                yield return new WaitForSeconds(0.06f);
            }
            _coins += 5;
            UpdateCoins();

            if (_puzzle.IsComplete)
            {
                yield return new WaitForSeconds(0.4f);
                ShowLevelComplete();
            }
        }

        void UseHint()
        {
            if (_coins < HintCost) { FloatToast("Not enough ★"); return; }
            var (word, _) = _puzzle.NextHint();
            if (word == null) return;

            if (!_hintsShown.TryGetValue(word, out var shown))
                _hintsShown[word] = shown = new HashSet<int>();

            int reveal = -1;
            for (int i = 0; i < word.Length; i++)
                if (!shown.Contains(i)) { reveal = i; break; }
            if (reveal < 0) return;

            shown.Add(reveal);
            _coins -= HintCost;
            UpdateCoins();
            if (_slotTiles.TryGetValue(word, out var tiles))
            {
                tiles[reveal].text = word[reveal].ToString();
                StartCoroutine(Brand.PopIn(tiles[reveal].transform.parent, 0.22f));
            }
        }

        void UpdateCoins()
        {
            if (_coinsText != null) _coinsText.text = _coins.ToString();
            Save();
        }

        // A little message that pops above the word bar and fades.
        void FloatToast(string msg)
        {
            var t = Brand.Label(_screen.transform, msg, 48, FontStyle.Bold, Brand.PrimaryDk, "Toast");
            Brand.Place(t.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0, -60), new Vector2(700, 80));
            StartCoroutine(ToastRoutine(t));
        }

        IEnumerator ToastRoutine(Text t)
        {
            float e = 0f; var rt = t.rectTransform; Vector2 home = rt.anchoredPosition;
            while (e < 1f)
            {
                e += Time.deltaTime;
                rt.anchoredPosition = home + new Vector2(0, e * 60f);
                var c = t.color; c.a = 1f - e; t.color = c;
                yield return null;
            }
            Destroy(t.gameObject);
        }

        // =====================================================================
        //  LEVEL COMPLETE
        // =====================================================================
        void ShowLevelComplete()
        {
            var overlay = Brand.Panel(_screen.transform, new Color(0, 0, 0, 0.55f), "Overlay");
            Brand.Stretch(overlay.rectTransform);
            overlay.gameObject.AddComponent<Button>(); // swallow taps behind the card

            var card = Brand.Panel(overlay.transform, Brand.Bg, "Card");
            Brand.Soften(card);
            Brand.Place(card.rectTransform, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(860, 900));
            StartCoroutine(Brand.PopIn(card.transform, 0.4f));

            var well = Brand.Label(card.transform, "Lovely!", 110, FontStyle.Bold, Brand.Primary, "Well");
            Brand.Place(well.rectTransform, new Vector2(0.5f, 1f), new Vector2(0, -130), new Vector2(700, 150));

            var stars = Brand.Label(card.transform, "★ ★ ★", 130, FontStyle.Bold, Brand.Gold, "Stars");
            Brand.Place(stars.rectTransform, new Vector2(0.5f, 1f), new Vector2(0, -320), new Vector2(700, 160));
            StartCoroutine(Brand.PopIn(stars.transform, 0.5f, 0.15f));

            string bonusLine = _puzzle.FoundBonus.Count > 0
                ? $"+{_puzzle.FoundBonus.Count} bonus words!" : "All words found!";
            var sub = Brand.Label(card.transform, bonusLine, 50, FontStyle.Normal, Brand.InkSoft, "Sub");
            Brand.Place(sub.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0, 20), new Vector2(700, 80));

            var (nImg, nBtn, _) = Brand.Button(card.transform, "NEXT  ›", Brand.Leaf, 64);
            Brand.Soften(nImg);
            Brand.Place(nImg.rectTransform, new Vector2(0.5f, 0f), new Vector2(0, 170), new Vector2(520, 160));
            nBtn.onClick.AddListener(() =>
            {
                _level++;
                Save();
                ShowGame();
            });
        }
    }
}
