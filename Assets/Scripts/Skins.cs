using System.Collections.Generic;
using UnityEngine;

namespace TrustIssues
{
    /// <summary>
    /// Cosmetic-only character skins. A skin is just a base sprite set (the vampire
    /// grid, or the Pink-Man sheets) plus a colour tint — so it reuses all existing
    /// art and never touches gameplay/balance. The equipped skin is saved locally
    /// and applied in GameRoot.SpawnPlayer. Unlocks key off stats the game already
    /// tracks (floors cleared, Endless best, death tally, daily streak, badges).
    /// </summary>
    public class SkinDef
    {
        public string id, name, unlockHint;
        public bool pinkman;                 // use the Pink-Man set instead of the vampire
        public Color tint = Color.white;
        public System.Func<bool> unlocked;   // null => always unlocked

        // Signature trait/ability — turns each skin into a playstyle. STRICTLY
        // mobility/utility, never invulnerability — you can never phase through a
        // hazard, so nothing can be cheesed; abilities only change HOW you move.
        public string ability = "Balanced";  // short label shown in the Wardrobe
        public bool dash;                    // K / Ctrl mist-dash
        public int airJumps;                 // extra mid-air jumps (double-jump)
        public float moveMul = 1f, jumpMul = 1f;
    }

    public static class Skins
    {
        public static readonly List<SkinDef> All = new()
        {
            new SkinDef { id = "heir",    name = "The Heir",       tint = Color.white,
                          ability = "Balanced — no tricks", unlockHint = "Default" },
            new SkinDef { id = "crimson", name = "Crimson Lord",   tint = Theme.Hex("FF4D4D"),
                          ability = "Blood Dash (K)", dash = true,
                          unlockHint = "Clear Castle floor 5",
                          unlocked = () => PlayerPrefs.GetInt("castle_unlocked", 0) >= 4 },
            new SkinDef { id = "spectre", name = "The Spectre",    tint = Theme.Hex("8FD4FF"),
                          ability = "Mist Double-Jump", airJumps = 1,
                          unlockHint = "Reach Endless floor 10",
                          unlocked = () => PlayerPrefs.GetInt("best_endless", 0) >= 9 },
            new SkinDef { id = "golden",  name = "Golden Cursed",  tint = Theme.Hex("F2C84B"),
                          ability = "High Leaper (+jump)", jumpMul = 1.1f,
                          unlockHint = "Keep a 7-day Blood Moon streak",
                          unlocked = () => Meta.Streak >= 7 },
            new SkinDef { id = "shadow",  name = "Shadowbound",    tint = Theme.Hex("4A3A5A"),
                          ability = "Twin Dash (K) + speed", dash = true, moveMul = 1.1f,
                          unlockHint = "Die 100 times (you've earned the dark)",
                          unlocked = () => PlayerPrefs.GetInt("castle_deaths", 0) >= 100 },
            new SkinDef { id = "pink",    name = "Pink Menace",    pinkman = true, tint = Color.white,
                          ability = "Fleet-Footed (+speed)", moveMul = 1.18f,
                          unlockHint = "Defeat the first boss",
                          unlocked = () => Badges.Has("boss1") },
            new SkinDef { id = "ash",     name = "Ashen Slayer",   tint = Theme.Hex("FF8A3D"),
                          ability = "Dash + Double-Jump", dash = true, airJumps = 1,
                          unlockHint = "Defeat all four bosses",
                          unlocked = () => Badges.Has("boss4") },
        };

        // A SpriteRenderer multiplies its colour onto the art, so a fully-saturated
        // tint (e.g. FF4D4D) zeroes the green/blue channels and flattens every bit of
        // shading into one solid red silhouette — that's the "full red vampire" look.
        // Blend the tint halfway toward white first: the costume still reads as red/
        // blue/gold, but the sprite keeps its highlights and shadows. Used both for the
        // live player and the Wardrobe preview so they always match.
        public static Color Shade(SkinDef s) => Color.Lerp(Color.white, s.tint, 0.5f);

        public static bool IsUnlocked(SkinDef s) => s.unlocked == null || s.unlocked();

        public static SkinDef Get(string id) => All.Find(s => s.id == id) ?? All[0];

        public static string CurrentId => PlayerPrefs.GetString("ti_skin", "heir");

        public static SkinDef Current
        {
            get { var s = Get(CurrentId); return IsUnlocked(s) ? s : All[0]; }
        }

        public static void Equip(string id)
        {
            PlayerPrefs.SetString("ti_skin", id); PlayerPrefs.Save();
        }
    }
}
