using System.Collections.Generic;
using UnityEngine;

namespace TrustIssues
{
    /// <summary>
    /// Earnable badges — the bragging-rights layer that feeds skins and the share
    /// card. Each badge is a one-time PlayerPrefs flag ("badge_&lt;id&gt;"). Award()
    /// returns true the first time it's earned so the caller can celebrate it.
    /// </summary>
    public static class Badges
    {
        public class Def { public string id, name, hint; }

        public static readonly List<Def> All = new()
        {
            new Def { id = "castle_clear", name = "Castle Escapee", hint = "Escape the Castle" },
            new Def { id = "boss1", name = "Ghoul Slayer",     hint = "Defeat the floor-10 boss" },
            new Def { id = "boss2", name = "Countess Killer",   hint = "Defeat the floor-20 boss" },
            new Def { id = "boss3", name = "Warlock's Bane",     hint = "Defeat the floor-30 boss" },
            new Def { id = "boss4", name = "Lord Vanquisher",    hint = "Defeat the final boss" },
            new Def { id = "streak3",  name = "Night Owl",       hint = "3-day Blood Moon streak" },
            new Def { id = "streak7",  name = "Creature of Habit", hint = "7-day Blood Moon streak" },
            new Def { id = "endless10", name = "Deep Diver",      hint = "Reach Endless floor 10" },
            new Def { id = "endless20", name = "Abyss Walker",    hint = "Reach Endless floor 20" },
            new Def { id = "versus_win", name = "Blood Rival",    hint = "Win a Versus race" },
            new Def { id = "die100", name = "Glutton for Punishment", hint = "Die 100 times" },
        };

        public static Def Get(string id) => All.Find(b => b.id == id);

        public static bool Has(string id) => PlayerPrefs.GetInt("badge_" + id, 0) == 1;

        /// <summary>Grant a badge. Returns true only the FIRST time it's earned.</summary>
        public static bool Award(string id)
        {
            if (Has(id)) return false;
            PlayerPrefs.SetInt("badge_" + id, 1);
            PlayerPrefs.SetString("badge_newest", id);
            PlayerPrefs.Save();
            return true;
        }

        public static int Count
        {
            get { int n = 0; foreach (var b in All) if (Has(b.id)) n++; return n; }
        }

        public static Def Newest
        {
            get { var id = PlayerPrefs.GetString("badge_newest", ""); return string.IsNullOrEmpty(id) ? null : Get(id); }
        }
    }
}
