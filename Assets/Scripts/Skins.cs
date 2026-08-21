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
        public int price;                    // Legacy field retained for save compatibility.
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
                          ability = "Balanced — no tricks", unlockHint = "Default Avatar" },
            new SkinDef { id = "crimson", name = "Crimson Lord",   tint = Theme.Hex("FF4D4D"),
                          ability = "Blood Dash (K)", dash = true,
                          unlockHint = "Reach Castle Floor 5",
                          unlocked = () => PlayerPrefs.GetInt("castle_unlocked", 0) >= 4 },
            new SkinDef { id = "spectre", name = "The Spectre",    tint = Theme.Hex("8FD4FF"),
                          ability = "Mist Double-Jump", airJumps = 1,
                          unlockHint = "Reach Endless Nights Floor 8",
                          unlocked = () => PlayerPrefs.GetInt("best_endless", 0) >= 7 },
            // THE HINT USED TO LIE. It read "Reach Blood Moon Night 3", but the
            // condition is Meta.Streak, which counts CONSECUTIVE DAYS the player
            // opened Blood Moon — not how deep they got in one sitting. So anyone
            // who actually reached night 3 in an evening watched the skin stay
            // locked, with no way to tell whether they had misread the hint or hit
            // a bug. A task the player cannot verify is worse than no task.
            new SkinDef { id = "golden",  name = "Golden Cursed",  tint = Theme.Hex("F2C84B"),
                          ability = "High Leaper (+jump)", jumpMul = 1.1f,
                          unlockHint = "Play Blood Moon 3 Days Running",
                          unlocked = () => Meta.Streak >= 3 },
            new SkinDef { id = "shadow",  name = "Shadowbound",    tint = Theme.Hex("4A3A5A"),
                          ability = "Twin Dash (K) + speed", dash = true, moveMul = 1.1f,
                          unlockHint = "Die 50 Times",
                          unlocked = () => PlayerPrefs.GetInt("castle_deaths", 0) >= 50 },
            // pinkman was a stand-in for skins that had no vampire art of their own.
            // Every avatar now ships real redressed sheets (see SkinArt), so the whole
            // roster keeps one silhouette — which is what keeps hitboxes and animation
            // timing identical no matter what you wear.
            new SkinDef { id = "pink",    name = "Pink Menace",    tint = Color.white,
                          ability = "Fleet-Footed (+speed)", moveMul = 1.18f,
                          unlockHint = "Reach Castle Floor 15",
                          unlocked = () => PlayerPrefs.GetInt("castle_unlocked", 0) >= 14 },
            new SkinDef { id = "ash",     name = "Ashen Slayer",   tint = Theme.Hex("FF8A3D"),
                          ability = "Dash + Double-Jump", dash = true, airJumps = 1,
                          unlockHint = "Reach Endless Nights Floor 15",
                          unlocked = () => PlayerPrefs.GetInt("best_endless", 0) >= 14 },
            // THE LAST THREE USED TO BE THE WORST DEAL IN THE GAME.
            //
            // All three said "Balanced" — i.e. no signature trait at all — while
            // sitting behind the three DEEPEST tasks on the board (Castle 30,
            // Endless 25, and the book). So the reward curve ran backwards: the
            // early skins handed out a dash and a double-jump, and the ones that
            // cost forty floors of work handed out a colour. Nobody chases that.
            //
            // Each now carries a trait that is worth the walk and still obeys the
            // one hard rule — mobility only, never invulnerability, so a skin can
            // change HOW you take a floor but can never let you ignore a hazard.
            new SkinDef { id = "bone",    name = "Bone Pale",      tint = Theme.Hex("D8D8E8"),
                          ability = "Sure-Footed (softer landings)", moveMul = 1.06f,
                          unlockHint = "Discover 10 Bestiary Entries",
                          unlocked = () => Codex.KnownCount() >= 10 },
            new SkinDef { id = "nosferatu", name = "Nosferatu",    tint = Theme.Hex("4FB7A4"),
                          ability = "Long Glide (+jump, +reach)", jumpMul = 1.14f, moveMul = 1.05f,
                          unlockHint = "Reach Castle Floor 30",
                          unlocked = () => PlayerPrefs.GetInt("castle_unlocked", 0) >= 29 },
            new SkinDef { id = "royal",   name = "Royal Blood",    tint = Theme.Hex("C62032"),
                          ability = "Dash + Double-Jump + speed",
                          dash = true, airJumps = 1, moveMul = 1.08f,
                          unlockHint = "Reach Endless Nights Floor 25",
                          unlocked = () => PlayerPrefs.GetInt("best_endless", 0) >= 24 },
        };

        // A SpriteRenderer multiplies its colour onto the art, so a fully-saturated
        // tint (e.g. FF4D4D) zeroes the green/blue channels and flattens every bit of
        // shading into one solid red silhouette — that's the "full red vampire" look.
        // Blend the tint halfway toward white first: the costume still reads as red/
        // blue/gold, but the sprite keeps its highlights and shadows. Used both for the
        // live player and the Wardrobe preview so they always match.
        public static Color Shade(SkinDef s) => Color.Lerp(Color.white, s.tint, 0.5f);

        /// <summary>Screenshot-harness escape hatch. Equipping a locked avatar
        /// silently falls back to The Heir, which made a shot run of all ten skins
        /// come back as ten identical Heirs. Never set outside ShotBot.</summary>
        public static bool DevUnlockAll;

        public static bool IsUnlocked(SkinDef s) => DevUnlockAll || s.unlocked == null || s.unlocked();

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
