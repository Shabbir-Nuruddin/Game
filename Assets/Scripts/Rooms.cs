using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace TrustIssues
{
    /// <summary>
    /// Runs the per-room rules on a roomed level.
    ///
    /// The genre insight this exists to serve: a trap is an object you learn to
    /// dodge once, but a RULE breaks a promise the room was built on, and that's
    /// what keeps players guessing past the first few floors. So each room owns
    /// exactly one rule; this watches which room the player is standing in and
    /// switches the active rule as they cross the doorway.
    ///
    /// Room entry also drops a checkpoint, so a five-room level never costs you
    /// more than the room you're in — the levels can be nastier precisely because
    /// the retry is cheap.
    ///
    /// Built and owned by GameRoot.BuildLevel; torn down with the level root.
    /// Levels with no rooms (11-40, Endless, Daily, Versus) never create one.
    /// </summary>
    public class RoomDirector : MonoBehaviour
    {
        List<RoomSpec> _rooms;
        Transform _player;
        int _active = -1;

        // --- Dark rule ---
        GameObject _darkGO;
        RectTransform _darkRT;
        Image _darkImg;
        float _darkT;              // 0 = lit, 1 = fully dark; eased so the lights "die" rather than cut
        bool _darkWanted;
        const float DarkFade = 2.2f;   // how fast the candles go out / relight

        public void Init(List<RoomSpec> rooms, Transform player)
        {
            _rooms = rooms; _player = player;
        }

        void LateUpdate()
        {
            if (_rooms == null || _rooms.Count == 0 || _player == null) return;

            int idx = RoomAt(_player.position.x);
            if (idx != _active && idx >= 0) EnterRoom(idx);

            _darkWanted = _active >= 0 && _rooms[_active].Rule == RoomRule.Dark;
            UpdateDark();
        }

        int RoomAt(float x)
        {
            for (int i = 0; i < _rooms.Count; i++)
                if (x >= _rooms[i].MinX && x < _rooms[i].MaxX) return i;
            return -1;   // in the doorway//past the end — keep whatever was active
        }

        void EnterRoom(int idx)
        {
            _active = idx;
            var r = _rooms[idx];
            // Respawn at this room's mouth rather than the level start. Silent and
            // forward-only — see SetRoomCheckpoint.
            if (GameRoot.I != null) GameRoot.I.SetRoomCheckpoint(new Vector3(r.EntryX, -2f, 0f));
        }

        // ---------------- Dark ----------------
        // A big soft-holed mask parked on the player. The mask is drawn larger than
        // the screen diagonal so that wherever the player stands, the dark still
        // reaches every corner — sizing it to the screen would leave a lit band on
        // the far side whenever the player walked toward an edge.
        void UpdateDark()
        {
            _darkT = Mathf.MoveTowards(_darkT, _darkWanted ? 1f : 0f, Time.unscaledDeltaTime / DarkFade);
            if (_darkT <= 0.001f)
            {
                if (_darkGO != null) _darkGO.SetActive(false);
                return;
            }
            if (_darkGO == null) BuildDark();
            if (!_darkGO.activeSelf) _darkGO.SetActive(true);

            var canvasRT = (RectTransform)Theme.Canvas.transform;
            var size = canvasRT.rect.size;
            float diag = Mathf.Sqrt(size.x * size.x + size.y * size.y);
            float s = diag * 2.2f;                 // ≥ 2× diagonal ⇒ always covers, wherever the hole sits
            _darkRT.sizeDelta = new Vector2(s, s);

            // Follow the player, in canvas space.
            var cam = Camera.main;
            if (cam != null)
            {
                Vector2 sp = cam.WorldToScreenPoint(_player.position + Vector3.up * 0.3f);
                RectTransformUtility.ScreenPointToLocalPointInRectangle(
                    canvasRT, sp, Theme.Canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : cam,
                    out var local);
                _darkRT.anchoredPosition = local;
            }

            var c = _darkImg.color;
            // Never quite 1 — a hair of visibility keeps it "a dark room" instead of
            // "the game crashed", which matters when the player is already suspicious.
            c.a = _darkT * 0.965f;
            _darkImg.color = c;
        }

        void BuildDark()
        {
            _darkGO = new GameObject("DarkMask", typeof(RectTransform));
            _darkGO.transform.SetParent(Theme.Canvas.transform, false);
            _darkRT = (RectTransform)_darkGO.transform;
            _darkRT.anchorMin = _darkRT.anchorMax = _darkRT.pivot = new Vector2(0.5f, 0.5f);
            _darkImg = _darkGO.AddComponent<Image>();
            _darkImg.sprite = Theme.DarkMask;
            _darkImg.color = new Color(0.02f, 0.01f, 0.03f, 0f);
            _darkImg.raycastTarget = false;
            // Above the world, below the HUD/menus, which are built after this.
            _darkGO.transform.SetAsFirstSibling();
        }

        void OnDestroy()
        {
            if (_darkGO != null) Destroy(_darkGO);
        }
    }
}
