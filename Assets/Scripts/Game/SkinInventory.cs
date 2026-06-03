using System;
using UnityEngine;

namespace StumbleClone.Game
{
    /// Ownership + pricing for the selectable skins. The default skin (catalog index 0) is always
    /// owned and free; every other skin is locked until bought with tokens via TokenWallet. Owned
    /// state is persisted per-skin in PlayerPrefs. This is the first sink for the token economy;
    /// areas / game modes / abilities follow the same buy-then-unlock shape.
    public static class SkinInventory
    {
        private const string OwnedKeyPrefix = "stumbleclone.skinowned.";

        /// Token price per catalog index (parallel to SkinCatalog.Ids). Index 0 is the free default.
        /// If the catalog grows past this array, unlisted skins fall back to <see cref="DefaultPrice"/>.
        private static readonly int[] Prices = { 0, 100, 100, 150, 200, 250, 300, 400 };
        private const int DefaultPrice = 200;

        /// Raised when ownership changes (a skin was bought), so the shop UI can refresh.
        public static event Action Changed;

        /// Token cost of a skin. The default skin costs 0.
        public static int PriceOf(string id)
        {
            int i = SkinCatalog.IndexOf(id);
            if (i <= 0) return 0; // index 0 (default) is free; IndexOf returns 0 for unknown ids too
            return i < Prices.Length ? Prices[i] : DefaultPrice;
        }

        /// True if the player owns (or is granted by default) this skin.
        public static bool IsOwned(string id)
        {
            if (SkinCatalog.IndexOf(id) == 0) return true; // default skin is always owned
            return PlayerPrefs.GetInt(OwnedKeyPrefix + id, 0) == 1;
        }

        /// Buy a skin if it isn't already owned and the wallet can cover the price. Returns true if
        /// the skin is owned afterward (already-owned skins return true without spending).
        public static bool TryBuy(string id)
        {
            if (IsOwned(id)) return true;
            if (!TokenWallet.TrySpend(PriceOf(id))) return false;

            PlayerPrefs.SetInt(OwnedKeyPrefix + id, 1);
            PlayerPrefs.Save();
            Changed?.Invoke();
            return true;
        }
    }
}
