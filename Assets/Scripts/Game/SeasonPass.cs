using System;
using UnityEngine;

namespace StumbleClone.Game
{
    /// The seasonal Battle Pass — the long-arc retention treadmill that sits above the daily/weekly
    /// quest loop. Players accumulate pass XP (from <see cref="QuestSystem"/> claims and a small
    /// per-round drip on LevelEnded) which climbs a ~30-tier track. Each tier carries a FREE reward
    /// (always claimable) and a PREMIUM reward gated behind <see cref="OwnsPremium"/> — a stub that
    /// stays false until a future IAP unlock is wired (NO real IAP here).
    ///
    /// Everything is local PlayerPrefs today; the tier table + reward ids are deliberately data-shaped
    /// (static arrays) so a later remote-config pass can replace them without touching callers. Rewards
    /// pay through TokenWallet (tokens) or grant an unlock id (skin/emote) that other systems can read.
    public static class SeasonPass
    {
        // ---- tuning -------------------------------------------------------------
        public const int TierCount = 30;          // tiers 0..29 (shown to players as 1..30)
        public const int XpPerTier = 100;         // flat XP cost per tier — simple, readable progression
        public const int XpPerRound = 20;         // small drip awarded just for finishing a round

        /// The active season number. Bumping this (locally now, remote-config later) resets every
        /// player's progress + claims for the new season. Stored alongside progress so a mismatch
        /// triggers a one-time wipe in <see cref="EnsureSeason"/>.
        public const int Season = 1;

        // ---- reward tables (parallel to tier index) -----------------------------
        // A reward is either tokens (amount > 0) or an unlock id (non-empty). The UI reads these to
        // label each claim button. Kept compact + repeating so 30 tiers stay meaningful without a
        // huge hand-authored list — a remote-config season would ship its own tables.

        // FREE track: mostly tokens, with a couple of milestone unlocks players get for free.
        private static readonly int[] FreeTokens =
        {
            25, 0, 30, 25, 40, 0, 35, 30, 45, 50,
            30, 35, 0, 40, 50, 35, 45, 0, 50, 60,
            40, 45, 50, 0, 55, 50, 60, 55, 0, 100,
        };
        private static readonly string[] FreeUnlocks =
        {
            "", "emote.wave", "", "", "", "skin.Cowboy_Male", "", "", "", "",
            "", "", "emote.dance", "", "", "", "", "skin.Chef_Male", "", "",
            "", "", "", "emote.taunt", "", "", "", "", "skin.Ninja_Male", "",
        };

        // PREMIUM track: richer tokens + the marquee cosmetic unlocks. Gated by OwnsPremium.
        private static readonly int[] PremiumTokens =
        {
            50, 60, 0, 75, 60, 80, 0, 70, 90, 100,
            60, 0, 80, 90, 100, 0, 85, 110, 90, 0,
            100, 120, 0, 110, 130, 0, 120, 140, 0, 250,
        };
        private static readonly string[] PremiumUnlocks =
        {
            "", "", "skin.Knight_Male", "", "", "", "emote.flex", "", "", "skin.Goblin_Male",
            "", "emote.spin", "", "", "", "skin.Elf", "", "", "", "emote.victory",
            "", "", "skin.BlueSoldier_Male", "", "", "emote.backflip", "", "", "skin.premium_gold", "",
        };

        // ---- PlayerPrefs keys (season-scoped via EnsureSeason wipe) -------------
        private const string SeasonKey   = "stumbleclone.pass.season";
        private const string XpKey       = "stumbleclone.pass.xp";
        private const string PremiumKey  = "stumbleclone.pass.premium";   // 0/1 — IAP stub
        private const string FreeClaimPrefix    = "stumbleclone.pass.free.";    // + tier => 0/1
        private const string PremiumClaimPrefix = "stumbleclone.pass.prem.";    // + tier => 0/1

        private static bool _bootstrapped;

        /// Raised whenever XP, tier, premium ownership, or a claim changes — UI refreshes on this.
        public static event Action Changed;

        // ---- public read API ----------------------------------------------------

        /// Total pass XP earned this season (never negative).
        public static int TotalXp { get { EnsureSeason(); return Mathf.Max(0, PlayerPrefs.GetInt(XpKey, 0)); } }

        /// Current tier index (0..TierCount-1). Player-facing tier number is this + 1.
        public static int CurrentTier => Mathf.Clamp(TotalXp / XpPerTier, 0, TierCount - 1);

        /// XP earned toward the *next* tier (0..XpPerTier-1). At max tier this pins to XpPerTier.
        public static int XpIntoTier =>
            CurrentTier >= TierCount - 1 ? XpPerTier : TotalXp - CurrentTier * XpPerTier;

        /// 0..1 progress through the current tier, for a progress bar. 1 at max tier.
        public static float TierProgress01 =>
            CurrentTier >= TierCount - 1 ? 1f : Mathf.Clamp01((float)XpIntoTier / XpPerTier);

        /// IAP stub — true once the player owns the premium track. Always false today; flip via
        /// <see cref="GrantPremiumStub"/> when a real purchase flow is wired later. NO real IAP here.
        public static bool OwnsPremium { get { EnsureSeason(); return PlayerPrefs.GetInt(PremiumKey, 0) == 1; } }

        public static int SeasonNumber => Season;

        // ---- tier reward queries (read by the UI) -------------------------------

        public static int FreeTokenReward(int tier) => InRange(tier) ? FreeTokens[tier] : 0;
        public static string FreeUnlockReward(int tier) => InRange(tier) ? FreeUnlocks[tier] : "";
        public static int PremiumTokenReward(int tier) => InRange(tier) ? PremiumTokens[tier] : 0;
        public static string PremiumUnlockReward(int tier) => InRange(tier) ? PremiumUnlocks[tier] : "";

        /// Whether a given track's reward at <paramref name="tier"/> has been claimed.
        public static bool IsClaimed(int tier, bool premium)
        {
            EnsureSeason();
            if (!InRange(tier)) return false;
            return PlayerPrefs.GetInt((premium ? PremiumClaimPrefix : FreeClaimPrefix) + tier, 0) == 1;
        }

        /// True if the reward at <paramref name="tier"/>/track is reached, unclaimed, and (for the
        /// premium track) the premium pass is owned — i.e. the claim button should be active.
        public static bool CanClaim(int tier, bool premium)
        {
            EnsureSeason();
            if (!InRange(tier)) return false;
            if (tier > CurrentTier) return false;          // tier not yet reached
            if (premium && !OwnsPremium) return false;     // gated behind the IAP stub
            return !IsClaimed(tier, premium);
        }

        // ---- mutations ----------------------------------------------------------

        /// Award pass XP (from a quest claim or per-round drip). No-op for non-positive amounts.
        public static void AddXp(int amount)
        {
            if (amount <= 0) return;
            EnsureSeason();
            PlayerPrefs.SetInt(XpKey, Mathf.Max(0, TotalXp + amount));
            PlayerPrefs.Save();
            Changed?.Invoke();
        }

        /// Claim a reached, unclaimed tier reward on the given track. Pays tokens via TokenWallet and
        /// records any unlock id through <see cref="SeasonRewards"/>. Returns false if not claimable.
        public static bool Claim(int tier, bool premium)
        {
            if (!CanClaim(tier, premium)) return false;

            PlayerPrefs.SetInt((premium ? PremiumClaimPrefix : FreeClaimPrefix) + tier, 1);
            PlayerPrefs.Save();

            int tokens = premium ? PremiumTokenReward(tier) : FreeTokenReward(tier);
            if (tokens > 0) TokenWallet.Add(tokens);

            string unlock = premium ? PremiumUnlockReward(tier) : FreeUnlockReward(tier);
            if (!string.IsNullOrEmpty(unlock)) SeasonRewards.Grant(unlock);

            Changed?.Invoke();
            return true;
        }

        /// Premium-pass IAP STUB. There is intentionally no purchase/receipt flow — this exists so a
        /// future real IAP integration has a single entry point to flip the gate. Calling it locally
        /// just toggles the bool (e.g. for a debug menu); the shipping build never calls it.
        public static void GrantPremiumStub(bool owns)
        {
            EnsureSeason();
            PlayerPrefs.SetInt(PremiumKey, owns ? 1 : 0);
            PlayerPrefs.Save();
            Changed?.Invoke();
        }

        // ---- season rollover ----------------------------------------------------

        // Wipe progress + claims the first time we observe a new season number. This keeps the local
        // model honest about "seasons" even before remote-config drives the cadence: bumping the
        // Season constant in a build resets everyone for the new pass.
        private static void EnsureSeason()
        {
            if (_bootstrapped) return;
            _bootstrapped = true;

            int stored = PlayerPrefs.GetInt(SeasonKey, 0);
            if (stored == Season) return;

            PlayerPrefs.SetInt(XpKey, 0);
            PlayerPrefs.SetInt(PremiumKey, 0);
            for (int t = 0; t < TierCount; t++)
            {
                PlayerPrefs.DeleteKey(FreeClaimPrefix + t);
                PlayerPrefs.DeleteKey(PremiumClaimPrefix + t);
            }
            PlayerPrefs.SetInt(SeasonKey, Season);
            PlayerPrefs.Save();
        }

        private static bool InRange(int tier) => tier >= 0 && tier < TierCount;

        // ---- per-round XP drip --------------------------------------------------

        /// Self-bootstrapping listener: every finished round (LevelEnded) drips a flat chunk of pass
        /// XP just for playing, so the pass always moves even on a quest-less session. Quests layer
        /// the bigger jumps on top. Subscribed once at startup; survives scene loads.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Bootstrap()
        {
            StumbleClone.Core.GameEvents.LevelEnded -= OnLevelEnded; // guard against domain-reload double-subscribe
            StumbleClone.Core.GameEvents.LevelEnded += OnLevelEnded;
        }

        private static void OnLevelEnded(StumbleClone.Core.IRacer _winner) => AddXp(XpPerRound);
    }

    /// Tiny ledger of cosmetic ids unlocked through the season pass (skins / emotes). Kept separate
    /// from SkinInventory so pass unlocks don't have to model token prices — a granted id is simply
    /// owned. Other systems (a future emote wheel, the skin picker) can query <see cref="IsUnlocked"/>.
    public static class SeasonRewards
    {
        private const string Prefix = "stumbleclone.passunlock.";

        /// Raised with the granted reward id whenever a new cosmetic is unlocked.
        public static event Action<string> Unlocked;

        public static bool IsUnlocked(string id)
            => !string.IsNullOrEmpty(id) && PlayerPrefs.GetInt(Prefix + id, 0) == 1;

        public static void Grant(string id)
        {
            if (string.IsNullOrEmpty(id) || IsUnlocked(id)) return;
            PlayerPrefs.SetInt(Prefix + id, 1);
            PlayerPrefs.Save();
            Unlocked?.Invoke(id);
        }
    }
}
