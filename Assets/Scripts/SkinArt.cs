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
    /// direction the platformer ever draws — at 256px frames. The shared fallback
    /// sheet is still a 4-direction grid at 64px, so callers must take the frame
    /// size AND the row from here rather than hard-coding either.
    ///
    /// 256 is not arbitrary: the art ships as 512px frames, and the longest sheet
    /// (8 frames of run/death) would then be 4096 wide — past the importer's 2048
    /// cap, which would silently downscale the texture and break `Assets.Grid`'s
    /// width/frame slicing. Halved, the widest sheet lands exactly on 2048.
    /// Inside every frame the character is normalised to a fixed body height and
    /// foot line, so all ten skins share one footprint and the collider never moves.
    /// </summary>
    public static class SkinArt
    {
        public const int HdFrame = 256;   // per-skin sheets
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
