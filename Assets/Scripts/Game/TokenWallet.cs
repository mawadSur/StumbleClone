using System;
using UnityEngine;

namespace StumbleClone.Game
{
    /// The player's token balance, persisted in PlayerPrefs so it survives scene loads and
    /// sessions. Tokens are earned per round (see GameManager.HandleLevelEnded) and spent in the
    /// title-screen shop (skins now; areas / modes / abilities later). Mirrors the SkinStore pattern.
    public static class TokenWallet
    {
        private const string Key = "stumbleclone.tokens";

        /// Raised with the new balance whenever it changes, so UI can refresh.
        public static event Action<int> Changed;

        /// Current token balance (never negative).
        public static int Balance => Mathf.Max(0, PlayerPrefs.GetInt(Key, 0));

        /// Grant tokens. No-op for non-positive amounts.
        public static void Add(int amount)
        {
            if (amount <= 0) return;
            SetBalance(Balance + amount);
        }

        /// True if the wallet currently holds at least <paramref name="cost"/> tokens.
        public static bool CanAfford(int cost) => Balance >= cost;

        /// Spend <paramref name="cost"/> tokens if affordable. Returns false (and spends nothing)
        /// when the balance is too low. A non-positive cost is treated as free (returns true).
        public static bool TrySpend(int cost)
        {
            if (cost <= 0) return true;
            if (Balance < cost) return false;
            SetBalance(Balance - cost);
            return true;
        }

        private static void SetBalance(int value)
        {
            int v = Mathf.Max(0, value);
            PlayerPrefs.SetInt(Key, v);
            PlayerPrefs.Save();
            Changed?.Invoke(v);
        }
    }
}
