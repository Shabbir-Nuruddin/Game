using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.UI;

namespace MorningWords
{
    /// <summary>
    /// Builds the ENTIRE game UI from code at runtime, so there is no fragile
    /// hand-built scene to maintain. You only need one empty GameObject in the
    /// scene with this script attached (the SETUP.md explains how).
    ///
    /// Design choices here are deliberately senior-friendly:
    ///   - very large text and tiles
    ///   - high-contrast, warm/calm colours
    ///   - a big on-screen keyboard (no reliance on a physical keyboard)
    ///   - no timers, no penalties, encouraging messages
    /// </summary>
    public class GameBootstrap : MonoBehaviour
    {
        // ---- Senior-friendly palette (warm, calm, high contrast) ----
        static readonly Color ColBackground = new Color(0.98f, 0.96f, 0.90f); // cream
        static readonly Color ColText       = new Color(0.18f, 0.16f, 0.14f); // soft black
        static readonly Color ColTileEmpty  = new Color(0.93f, 0.90f, 0.83f); // light tile
        static readonly Color ColTileBorder = new Color(0.80f, 0.76f, 0.68f);
        static readonly Color ColCorrect    = new Color(0.45f, 0.66f, 0.41f); // calm green
        static readonly Color ColPresent    = new Color(0.92f, 0.74f, 0.36f); // warm amber
        static readonly Color ColAbsent     = new Color(0.74f, 0.71f, 0.66f); // soft grey
        static readonly Color ColKey        = new Color(0.88f, 0.84f, 0.76f);

        WordGame _game;
        string _currentGuess = "";
        Text _messageText;
        readonly Text[,] _tileTexts = new Text[WordGame.MaxGuesses, WordGame.WordLength];
        readonly Image[,] _tileImages = new Image[WordGame.MaxGuesses, WordGame.WordLength];
        Font _font;

        void Start()
        {
            _font = LoadFont();
            _game = new WordGame(WordList.WordForDate(DateTime.Now), WordList.All);
            BuildUI();
            ShowMessage("Welcome! Guess the 5-letter word.");
        }

        // Unity removed the built-in Arial in newer versions; LegacyRuntime.ttf
        // replaced it. Try the new name first, fall back to the old one.
        static Font LoadFont()
        {
            Font f = null;
            try { f = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"); } catch { }
            if (f == null)
            {
                try { f = Resources.GetBuiltinResource<Font>("Arial.ttf"); } catch { }
            }
            return f;
        }

        void BuildUI()
        {
            // --- Camera background ---
            var cam = Camera.main;
            if (cam != null) cam.backgroundColor = ColBackground;

            // --- Canvas ---
            var canvasGo = new GameObject("Canvas");
            var canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = canvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1080, 1920); // portrait phone
            scaler.matchWidthOrHeight = 0.5f;
            canvasGo.AddComponent<GraphicRaycaster>();

            // --- Background panel (so it's cream even in the Editor Game view) ---
            var bg = NewImage("Background", canvasGo.transform, ColBackground);
            Stretch(bg.rectTransform);

            // --- Title ---
            var title = NewText("Title", canvasGo.transform, "Morning Words", 84, FontStyle.Bold);
            Anchor(title.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                   new Vector2(0, -120), new Vector2(900, 120));

            // --- Message line ---
            _messageText = NewText("Message", canvasGo.transform, "", 44, FontStyle.Normal);
            Anchor(_messageText.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                   new Vector2(0, -230), new Vector2(1000, 80));

            // --- Tile grid (6 rows x 5 columns) ---
            BuildGrid(canvasGo.transform);

            // --- On-screen keyboard ---
            BuildKeyboard(canvasGo.transform);
        }

        void BuildGrid(Transform parent)
        {
            float tile = 150f, gap = 16f;
            float gridW = WordGame.WordLength * tile + (WordGame.WordLength - 1) * gap;
            float startX = -gridW / 2f + tile / 2f;
            float startY = 380f; // from centre, upward

            for (int r = 0; r < WordGame.MaxGuesses; r++)
            {
                for (int c = 0; c < WordGame.WordLength; c++)
                {
                    var img = NewImage($"Tile_{r}_{c}", parent, ColTileEmpty);
                    var outline = img.gameObject.AddComponent<Outline>();
                    outline.effectColor = ColTileBorder;
                    outline.effectDistance = new Vector2(3, 3);
                    Anchor(img.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                           new Vector2(startX + c * (tile + gap), startY - r * (tile + gap)),
                           new Vector2(tile, tile));

                    var t = NewText($"TileText_{r}_{c}", img.transform, "", 90, FontStyle.Bold);
                    Stretch(t.rectTransform);

                    _tileImages[r, c] = img;
                    _tileTexts[r, c] = t;
                }
            }
        }

        void BuildKeyboard(Transform parent)
        {
            string[] rows = { "QWERTYUIOP", "ASDFGHJKL", "ZXCVBNM" };
            float keyW = 92f, keyH = 120f, gap = 10f;
            float baseY = -560f;

            for (int r = 0; r < rows.Length; r++)
            {
                string row = rows[r];
                float rowW = row.Length * keyW + (row.Length - 1) * gap;
                float startX = -rowW / 2f + keyW / 2f;
                float y = baseY - r * (keyH + gap);

                for (int i = 0; i < row.Length; i++)
                {
                    char letter = row[i];
                    MakeKey(parent, letter.ToString(), startX + i * (keyW + gap), y,
                            keyW, keyH, () => OnLetter(letter));
                }
            }

            // Big ENTER and DELETE keys on their own row.
            float wideY = baseY - rows.Length * (keyH + gap);
            MakeKey(parent, "ENTER", -260, wideY, 460, keyH, OnEnter, 40);
            MakeKey(parent, "DELETE", 260, wideY, 460, keyH, OnDelete, 40);
        }

        void MakeKey(Transform parent, string label, float x, float y, float w, float h,
                     Action onClick, int fontSize = 52)
        {
            var img = NewImage($"Key_{label}", parent, ColKey);
            Anchor(img.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                   new Vector2(x, y), new Vector2(w, h));

            var btn = img.gameObject.AddComponent<Button>();
            btn.targetGraphic = img;
            btn.onClick.AddListener(() => onClick());

            var t = NewText($"KeyText_{label}", img.transform, label, fontSize, FontStyle.Bold);
            Stretch(t.rectTransform);
        }

        // ---------------- Game interaction ----------------

        void OnLetter(char c)
        {
            if (_game.IsOver) return;
            if (_currentGuess.Length >= WordGame.WordLength) return;
            _currentGuess += c;
            RefreshCurrentRow();
        }

        void OnDelete()
        {
            if (_game.IsOver) return;
            if (_currentGuess.Length == 0) return;
            _currentGuess = _currentGuess.Substring(0, _currentGuess.Length - 1);
            RefreshCurrentRow();
        }

        void OnEnter()
        {
            if (_game.IsOver) return;
            if (_currentGuess.Length < WordGame.WordLength)
            {
                ShowMessage("Add a few more letters.");
                return;
            }
            if (!_game.IsValidWord(_currentGuess))
            {
                ShowMessage("Hmm, that isn't in our word list. Try another.");
                return;
            }

            int row = _game.CurrentRow;
            LetterState[] result = _game.SubmitGuess(_currentGuess);
            PaintRow(row, _currentGuess, result);
            _currentGuess = "";

            if (_game.IsWon)
            {
                ShowMessage("Wonderful! You got it. See you tomorrow!");
            }
            else if (_game.IsOver)
            {
                ShowMessage($"So close! The word was {_game.Secret}. Come back tomorrow!");
            }
            else
            {
                ShowMessage(Encourage(_game.CurrentRow));
            }
        }

        static string Encourage(int guessesUsed)
        {
            switch (guessesUsed)
            {
                case 1: return "Great start. Keep going!";
                case 2: return "Nice. You're narrowing it down.";
                case 3: return "You're doing well!";
                default: return "Almost there — you can do it!";
            }
        }

        void RefreshCurrentRow()
        {
            int row = _game.CurrentRow;
            for (int c = 0; c < WordGame.WordLength; c++)
            {
                _tileTexts[row, c].text = c < _currentGuess.Length
                    ? _currentGuess[c].ToString() : "";
            }
        }

        void PaintRow(int row, string guess, LetterState[] result)
        {
            for (int c = 0; c < WordGame.WordLength; c++)
            {
                _tileTexts[row, c].text = guess[c].ToString();
                _tileImages[row, c].color = ColorFor(result[c]);
                _tileTexts[row, c].color = result[c] == LetterState.Empty ? ColText : Color.white;
            }
        }

        static Color ColorFor(LetterState s)
        {
            switch (s)
            {
                case LetterState.Correct: return ColCorrect;
                case LetterState.Present: return ColPresent;
                case LetterState.Absent:  return ColAbsent;
                default: return ColTileEmpty;
            }
        }

        void ShowMessage(string msg)
        {
            if (_messageText != null) _messageText.text = msg;
        }

        // ---------------- Tiny UI helpers ----------------

        Image NewImage(string name, Transform parent, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var img = go.AddComponent<Image>();
            img.color = color;
            return img;
        }

        Text NewText(string name, Transform parent, string content, int size, FontStyle style)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var t = go.AddComponent<Text>();
            t.font = _font;
            t.text = content;
            t.fontSize = size;
            t.fontStyle = style;
            t.color = ColText;
            t.alignment = TextAnchor.MiddleCenter;
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            return t;
        }

        static void Stretch(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
        }

        static void Anchor(RectTransform rt, Vector2 anchorMin, Vector2 anchorMax,
                           Vector2 anchoredPos, Vector2 size)
        {
            rt.anchorMin = anchorMin;
            rt.anchorMax = anchorMax;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = anchoredPos;
            rt.sizeDelta = size;
        }
    }
}
