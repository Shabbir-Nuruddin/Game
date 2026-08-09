using System;
using System.Collections.Generic;
using UnityEngine;

namespace TrustIssues
{
    /// <summary>Challenge-only Aura and Outfit progression for the Wardrobe.</summary>
    public static class WardrobeCosmetics
    {
        public sealed class Def
        {
            public string id, name, hint;
            public Color color;
            public Func<bool> unlocked;
        }

        static bool Castle(int floor) => PlayerPrefs.GetInt("castle_unlocked", 0) >= floor - 1;
        static bool Endless(int floor) => PlayerPrefs.GetInt("best_endless", 0) >= floor - 1;
        static bool Deaths(int count) => PlayerPrefs.GetInt("castle_deaths", 0) >= count;

        public static readonly List<Def> Auras = new()
        {
            new Def { id="none", name="No Aura", hint="Default", color=Color.clear, unlocked=()=>true },
            new Def { id="gilded", name="Gilded Demise", hint="Reach Castle Floor 10", color=Theme.Hex("E6B84A"), unlocked=()=>Castle(10) },
            new Def { id="bat", name="Bat Burst", hint="Reach Endless Nights Floor 10", color=Theme.Hex("8D4DCC"), unlocked=()=>Endless(10) },
            new Def { id="ash", name="Crumble to Ash", hint="Die 100 Times", color=Theme.Hex("BFC0C5"), unlocked=()=>Deaths(100) },
            new Def { id="blood", name="Blood Mist", hint="Reach Castle Floor 20", color=Theme.Hex("D52732"), unlocked=()=>Castle(20) },
            new Def { id="ember", name="Ember Wake", hint="Reach Endless Nights Floor 15", color=Theme.Hex("EF6B24"), unlocked=()=>Endless(15) },
            new Def { id="ecto", name="Ectoplasm", hint="Reach Endless Nights Floor 20", color=Theme.Hex("23B7C8"), unlocked=()=>Endless(20) },
            new Def { id="skill", name="Skill Issue", hint="Die to 10 Different Traps", color=Theme.Hex("D64A9A"), unlocked=()=>Codex.KnownCount() >= 10 },
            new Def { id="easy", name="Easy, BTW", hint="Clear Castle Floor 40", color=Theme.Hex("64BC45"), unlocked=()=>Castle(40) },
            new Def { id="plan", name="All Part of the Plan", hint="Complete the Bestiary (19/19)", color=Theme.Hex("C7A15B"), unlocked=()=>Codex.KnownCount() >= Codex.Total },
        };

        public static readonly List<Def> Outfits = new()
        {
            new Def { id="classic", name="Classic Heir", hint="Default Outfit", color=Color.white, unlocked=()=>true },
            new Def { id="velvet", name="Royal Velvet", hint="Reach Castle Floor 8", color=Theme.Hex("A62D43"), unlocked=()=>Castle(8) },
            new Def { id="shadow", name="Shadow Cloak", hint="Reach Endless Nights Floor 10", color=Theme.Hex("58658B"), unlocked=()=>Endless(10) },
            new Def { id="hunter", name="Blood Hunter", hint="Reach Blood Moon Night 5", color=Theme.Hex("C62C35"), unlocked=()=>Meta.Streak >= 5 },
            new Def { id="tuxedo", name="Noble Tuxedo", hint="Reach Castle Floor 18", color=Theme.Hex("85709F"), unlocked=()=>Castle(18) },
            new Def { id="frost", name="Frost Regalia", hint="Reach Endless Nights Floor 15", color=Theme.Hex("72C9EA"), unlocked=()=>Endless(15) },
            new Def { id="infernal", name="Infernal Finery", hint="Reach Castle Floor 28", color=Theme.Hex("E35A22"), unlocked=()=>Castle(28) },
            new Def { id="void", name="Void Ensemble", hint="Reach Endless Nights Floor 22", color=Theme.Hex("7243B7"), unlocked=()=>Endless(22) },
            new Def { id="bone", name="Bone Majesty", hint="Complete Bestiary (19/19)", color=Theme.Hex("C9BEA7"), unlocked=()=>Codex.KnownCount() >= Codex.Total },
            new Def { id="regent", name="Eternal Regent", hint="Clear Castle Floor 40", color=Theme.Hex("CF2930"), unlocked=()=>Castle(40) },
        };

        public static string CurrentAuraId => PlayerPrefs.GetString("ti_aura", "none");
        public static string CurrentOutfitId => PlayerPrefs.GetString("ti_outfit", "classic");
        public static Def CurrentAura => Auras.Find(x => x.id == CurrentAuraId) ?? Auras[0];
        public static Def CurrentOutfit => Outfits.Find(x => x.id == CurrentOutfitId) ?? Outfits[0];
        public static bool IsUnlocked(Def d) => d != null && (d.unlocked == null || d.unlocked());

        public static void EquipAura(string id)
        {
            var d = Auras.Find(x => x.id == id);
            if (!IsUnlocked(d)) return;
            PlayerPrefs.SetString("ti_aura", id); PlayerPrefs.Save();
        }

        public static void EquipOutfit(string id)
        {
            var d = Outfits.Find(x => x.id == id);
            if (!IsUnlocked(d)) return;
            PlayerPrefs.SetString("ti_outfit", id); PlayerPrefs.Save();
        }

        public static Color PlayerTint(Color avatarTint)
        {
            var outfit = CurrentOutfit;
            return outfit.id == "classic" ? avatarTint : Color.Lerp(avatarTint, outfit.color, 0.42f);
        }

        public static void AttachAura(GameObject player)
        {
            var aura = CurrentAura;
            if (player == null || aura.id == "none") return;
            var go = new GameObject("WardrobeAura");
            go.transform.SetParent(player.transform, false);
            go.transform.localPosition = new Vector3(0f, 0.1f, 0.15f);
            var ps = go.AddComponent<ParticleSystem>();
            var main = ps.main; main.loop = true; main.startLifetime = 0.8f; main.startSpeed = 0.45f;
            main.startSize = 0.14f; main.startColor = new Color(aura.color.r, aura.color.g, aura.color.b, 0.8f);
            main.simulationSpace = ParticleSystemSimulationSpace.Local; main.maxParticles = 40;
            var emission = ps.emission; emission.rateOverTime = 14f;
            var shape = ps.shape; shape.shapeType = ParticleSystemShapeType.Circle; shape.radius = 0.55f;
            var renderer = ps.GetComponent<ParticleSystemRenderer>(); renderer.sortingOrder = 7;
        }
    }
}
