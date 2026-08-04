using UnityEngine;

namespace TrustIssues
{
    public enum Difficulty { Casual = 0, Normal = 1, Nightmare = 2 }

    /// <summary>
    /// One central place every system reads difficulty from. Persisted in PlayerPrefs
    /// so the choice survives sessions. The whole point: let a wide audience FINISH
    /// the game (Casual) without removing the brutal core for veterans (Nightmare).
    /// Normal is the default and matches the game's original tuning.
    /// </summary>
    public static class Diff
    {
        public static Difficulty Current
        {
            get => (Difficulty)Mathf.Clamp(PlayerPrefs.GetInt("opt_difficulty", (int)Difficulty.Normal), 0, 2);
            set { PlayerPrefs.SetInt("opt_difficulty", (int)value); PlayerPrefs.Save(); }
        }

        public static string Name =>
            Current == Difficulty.Casual ? "CASUAL"
          : Current == Difficulty.Nightmare ? "NIGHTMARE"
          : "NORMAL";

        // One-line flavour for the menu so the choice is self-explanatory.
        public static string Blurb =>
            Current == Difficulty.Casual ? "for everyone — generous lives, gentler bosses, no sunrise"
          : Current == Difficulty.Nightmare ? "the original rage game — one mistake, you're dust"
          : "the intended challenge — fair but unforgiving";

        // ---- Lives / hearts ----
        // Starting hearts for Blood Moon and each Endless checkpoint segment.
        public static int StartHearts =>
            Current == Difficulty.Casual ? 8 : Current == Difficulty.Normal ? 5 : 3;
        // Cap on hearts banked from clearing floors.
        public static int MaxHearts =>
            Current == Difficulty.Casual ? 12 : Current == Difficulty.Normal ? 8 : 5;
        // Player hits inside a boss arena before death (Nightmare keeps one-shot).
        public static int BossPlayerHearts =>
            Current == Difficulty.Casual ? 3 : Current == Difficulty.Normal ? 2 : 1;

        // ---- Bosses ----
        // Boss max-HP multiplier (Casual fights are shorter).
        public static float BossHpMul =>
            Current == Difficulty.Casual ? 0.65f : Current == Difficulty.Normal ? 0.85f : 1f;
        // Telegraph/anticipation stretch (>1 = longer, more readable wind-ups).
        public static float BossTelegraphMul =>
            Current == Difficulty.Casual ? 1.4f : Current == Difficulty.Normal ? 1.15f : 1f;
        // Projectile / attack speed scale (<1 = slower, easier to dodge).
        public static float BossSpeedMul =>
            Current == Difficulty.Casual ? 0.8f : Current == Difficulty.Normal ? 0.92f : 1f;

        // ---- World pressure ----
        // The creeping sunrise wall — off entirely on Casual.
        public static bool SunRise => Current != Difficulty.Casual;
        // Max "Trust Issues" reactive traps learned from your deaths (0 = off).
        public static int ReactiveTrapCap =>
            Current == Difficulty.Casual ? 0 : Current == Difficulty.Normal ? 2 : 3;

        // ---- Endless ----
        // Floors per checkpoint segment (respawn anchor; same on every difficulty).
        public const int CheckpointEvery = 5;
    }
}
