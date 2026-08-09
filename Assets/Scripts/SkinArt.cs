using System.Collections.Generic;
using UnityEngine;

namespace TrustIssues
{
    /// <summary>
    /// Per-skin character art. Every Wardrobe avatar ships its own redressed
    /// vampire sheets under Resources/art/skins/&lt;id&gt;/, so equipping a skin
    /// swaps the actual sprite instead of only multiplying a colour over the one
    /// shared vampire (which flattened costumes into single-hue silhouettes).
    ///
    /// The shipped sheets carry ONE row — the right-facing side profile, the only
    /// direction the platformer ever draws — at 128px frames. The shared fallback
    /// sheet is still a 4-direction grid at 64px, so callers must take the frame
    /// size AND the row from here rather than hard-coding either.
    /// </summary>
    public static class SkinArt
    {
        public const int HdFrame = 128;   // per-skin sheets
        public const int LoFrame = 64;    // shared 4-direction vampire grid
        public const int LoRow = 3;       // bottom row of the shared grid = side profile

        // Resources.Load hits disk the first time; SpawnPlayer runs on every
        // respawn, so remember what exists rather than probing each death.
        static readonly Dictionary<string, bool> _has = new();

        public static bool Has(string id)
        {
            if (string.IsNullOrEmpty(id)) return false;
            if (_has.TryGetValue(id, out var known)) return known;
            bool found = Resources.Load<Texture2D>("art/skins/" + id + "/vamp_idle_sheet") != null;
            _has[id] = found;
            return found;
        }

        /// <summary>Path prefix for Assets.Grid — empty means "use the shared sheet".</summary>
        public static string Prefix(string id) => Has(id) ? "skins/" + id + "/" : "";

        public static int Frame(string id) => Has(id) ? HdFrame : LoFrame;

        /// <summary>Single-row per-skin sheet, or the side-profile row of the shared grid.</summary>
        public static int Row(string id) => Has(id) ? 0 : LoRow;
    }
}
