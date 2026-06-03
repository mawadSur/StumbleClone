using StumbleClone.Core;
using UnityEngine;

namespace StumbleClone.Game
{
    /// Which level modes the player has unlocked. Knockout (Last-Standing) is the free default mode;
    /// Race and Survival are token-gated unlocks — extra playable content (the scenes already ship)
    /// and a token sink that gives a reason to keep earning. Persisted in PlayerPrefs.
    public static class LevelProgress
    {
        private const string Prefix = "stumbleclone.modeunlocked.";

        /// Token cost to unlock a mode. Knockout is free; Race/Survival are paid.
        public static int PriceOf(LevelMode mode)
        {
            switch (mode)
            {
                case LevelMode.Race: return 100;
                case LevelMode.Survival: return 150;
                default: return 0; // LastStanding (Knockout) — the free default
            }
        }

        public static bool IsUnlocked(LevelMode mode)
        {
            if (PriceOf(mode) == 0) return true;
            return PlayerPrefs.GetInt(Prefix + (int)mode, 0) == 1;
        }

        /// Unlock a mode if affordable. Already-unlocked modes return true without spending.
        public static bool TryUnlock(LevelMode mode)
        {
            if (IsUnlocked(mode)) return true;
            if (!TokenWallet.TrySpend(PriceOf(mode))) return false;
            PlayerPrefs.SetInt(Prefix + (int)mode, 1);
            PlayerPrefs.Save();
            return true;
        }

        public static string DisplayName(LevelMode mode)
        {
            switch (mode)
            {
                case LevelMode.Race: return "Race";
                case LevelMode.Survival: return "Survival";
                case LevelMode.LastStanding: return "Knockout";
                default: return mode.ToString();
            }
        }
    }
}
