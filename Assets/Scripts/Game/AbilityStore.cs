using System;
using UnityEngine;

namespace StumbleClone.Game
{
    /// The kind of effect an equippable perk applies at the start of every round.
    public enum PerkEffect { None, Speed, Jump, Shield }

    /// Token-bought abilities, split into the two types the design calls for:
    ///   • Equippable PERKS — buy once, equip one at a time, applied every round (permanent while equipped).
    ///   • Consumables — buy charges that are spent automatically (the Token Doubler doubles a win payout).
    /// All state is PlayerPrefs-backed and paid through TokenWallet, so it shares the shop economy.
    public static class AbilityStore
    {
        // ---- Equippable perks ----------------------------------------------------
        public static readonly string[] PerkIds   = { "none", "swift", "spring", "guardian" };
        public static readonly string[] PerkNames = { "None", "Swift Boots", "Spring Step", "Guardian" };
        public static readonly string[] PerkDesc  = { "No perk equipped", "+12% move speed", "+30% jump height", "Start each round shielded" };
        private static readonly int[]    PerkPrices = { 0, 120, 120, 200 };
        private static readonly PerkEffect[] PerkEffects = { PerkEffect.None, PerkEffect.Speed, PerkEffect.Jump, PerkEffect.Shield };

        private const string OwnedPerkPrefix = "stumbleclone.perkowned.";
        private const string EquippedPerkKey = "stumbleclone.perkequipped";

        // ---- Consumable: Token Doubler ------------------------------------------
        private const string DoublerKey = "stumbleclone.consumable.doubler";
        public const int DoublerPrice = 50;

        // ---- Consumable power-ups -----------------------------------------------
        // Buy charges that are auto-applied at the start of the next round (one charge
        // spent per round), STACKING on top of the equipped perk. Mirrors the Token
        // Doubler pattern: per-id charge counts in PlayerPrefs, paid via TokenWallet.
        public static readonly string[] PowerupIds   = { "rocket", "bubble", "megahop" };
        public static readonly string[] PowerupNames = { "Rocket Start", "Bubble Shield", "Mega Hops" };
        private static readonly int[]    PowerupPrices = { 40, 60, 40 };

        private const string PowerupPrefix = "stumbleclone.powerup.";

        public static event Action Changed;

        public static int PerkCount => PerkIds.Length;
        public static int PerkPrice(int i) => (i >= 0 && i < PerkPrices.Length) ? PerkPrices[i] : 0;
        public static PerkEffect EffectOf(string id) { int i = PerkIndex(id); return PerkEffects[i]; }

        public static int PerkIndex(string id)
        {
            for (int i = 0; i < PerkIds.Length; i++) if (PerkIds[i] == id) return i;
            return 0;
        }

        public static bool IsPerkOwned(string id)
        {
            if (PerkIndex(id) == 0) return true; // "none" is always owned
            return PlayerPrefs.GetInt(OwnedPerkPrefix + id, 0) == 1;
        }

        public static string EquippedPerk
        {
            get
            {
                string id = PlayerPrefs.GetString(EquippedPerkKey, "none");
                return IsPerkOwned(id) ? id : "none";
            }
            set { PlayerPrefs.SetString(EquippedPerkKey, value); PlayerPrefs.Save(); Changed?.Invoke(); }
        }

        /// Buy a perk if affordable; already-owned perks succeed without spending.
        public static bool BuyPerk(string id)
        {
            if (IsPerkOwned(id)) return true;
            if (!TokenWallet.TrySpend(PerkPrice(PerkIndex(id)))) return false;
            PlayerPrefs.SetInt(OwnedPerkPrefix + id, 1);
            PlayerPrefs.Save();
            Changed?.Invoke();
            return true;
        }

        // ---- Token Doubler consumable -------------------------------------------
        public static int DoublerCount => Mathf.Max(0, PlayerPrefs.GetInt(DoublerKey, 0));

        public static bool BuyDoubler()
        {
            if (!TokenWallet.TrySpend(DoublerPrice)) return false;
            PlayerPrefs.SetInt(DoublerKey, DoublerCount + 1);
            PlayerPrefs.Save();
            Changed?.Invoke();
            return true;
        }

        /// Spend one Token Doubler charge if any remain (called on a win to double the payout).
        public static bool ConsumeDoubler()
        {
            int n = DoublerCount;
            if (n <= 0) return false;
            PlayerPrefs.SetInt(DoublerKey, n - 1);
            PlayerPrefs.Save();
            Changed?.Invoke();
            return true;
        }

        // ---- Consumable power-up API --------------------------------------------
        public static int PowerupCatalogCount => PowerupIds.Length;

        public static int PowerupIndex(string id)
        {
            for (int i = 0; i < PowerupIds.Length; i++) if (PowerupIds[i] == id) return i;
            return -1;
        }

        public static int PowerupPrice(int i) => (i >= 0 && i < PowerupPrices.Length) ? PowerupPrices[i] : 0;
        public static int PowerupPrice(string id) => PowerupPrice(PowerupIndex(id));

        /// Charges currently owned for a given power-up id (never negative).
        public static int PowerupCount(string id)
        {
            if (PowerupIndex(id) < 0) return 0;
            return Mathf.Max(0, PlayerPrefs.GetInt(PowerupPrefix + id, 0));
        }

        /// Buy one charge if affordable; spends tokens then increments the charge count.
        public static bool BuyPowerup(string id)
        {
            if (PowerupIndex(id) < 0) return false;
            if (!TokenWallet.TrySpend(PowerupPrice(id))) return false;
            PlayerPrefs.SetInt(PowerupPrefix + id, PowerupCount(id) + 1);
            PlayerPrefs.Save();
            Changed?.Invoke();
            return true;
        }

        /// Spend one charge if any remain (called at the start of a round to apply the effect).
        public static bool ConsumePowerup(string id)
        {
            int n = PowerupCount(id);
            if (n <= 0) return false;
            PlayerPrefs.SetInt(PowerupPrefix + id, n - 1);
            PlayerPrefs.Save();
            Changed?.Invoke();
            return true;
        }
    }
}
