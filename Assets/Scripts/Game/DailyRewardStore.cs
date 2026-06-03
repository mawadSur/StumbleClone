using System;
using UnityEngine;

namespace StumbleClone.Game
{
    /// Daily login reward — the core "come back tomorrow" retention hook. The first time the title
    /// screen opens on a new calendar day (UTC), the player is granted tokens that scale with their
    /// consecutive-day streak. Missing a day resets the streak. State is in PlayerPrefs; rewards are
    /// paid through TokenWallet so they feed straight into the shop economy.
    public static class DailyRewardStore
    {
        private const string LastClaimKey = "stumbleclone.daily.lastclaim"; // yyyyMMdd stamp of last claim
        private const string StreakKey = "stumbleclone.daily.streak";

        private const int BaseReward = 25;       // day 1 payout
        private const int PerDayBonus = 15;       // added per consecutive day
        private const int MaxRewardDay = 7;       // payout stops growing after a week

        /// True if today's reward has not been claimed yet.
        public static bool RewardAvailable => PlayerPrefs.GetInt(LastClaimKey, 0) != TodayStamp();

        /// Current consecutive-day streak (0 before the first claim).
        public static int CurrentStreak => Mathf.Max(0, PlayerPrefs.GetInt(StreakKey, 0));

        /// Grant today's reward if it hasn't been claimed yet. Returns the tokens awarded (0 if
        /// already claimed today). <paramref name="streak"/> receives the updated streak length.
        public static int TryClaim(out int streak)
        {
            int today = TodayStamp();
            int last = PlayerPrefs.GetInt(LastClaimKey, 0);
            if (last == today) { streak = CurrentStreak; return 0; } // already claimed today

            // Continue the streak only if the previous claim was literally yesterday; otherwise reset.
            streak = last == YesterdayStamp() ? CurrentStreak + 1 : 1;

            int rewardDay = Mathf.Clamp(streak, 1, MaxRewardDay);
            int amount = BaseReward + PerDayBonus * (rewardDay - 1);

            PlayerPrefs.SetInt(StreakKey, streak);
            PlayerPrefs.SetInt(LastClaimKey, today);
            PlayerPrefs.Save();

            TokenWallet.Add(amount);
            return amount;
        }

        // Calendar day as a comparable yyyyMMdd integer. AddDays handles month/year rollover.
        private static int TodayStamp() => Stamp(DateTime.UtcNow.Date);
        private static int YesterdayStamp() => Stamp(DateTime.UtcNow.Date.AddDays(-1));
        private static int Stamp(DateTime d) => d.Year * 10000 + d.Month * 100 + d.Day;
    }
}
