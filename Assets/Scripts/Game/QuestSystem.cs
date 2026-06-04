using System;
using System.Collections.Generic;
using StumbleClone.Core;
using UnityEngine;

namespace StumbleClone.Game
{
    /// A single quest definition + its live progress. Definitions are static (the catalog below);
    /// progress + claimed state live in PlayerPrefs, keyed by <see cref="Id"/> and the current
    /// period stamp so a new day/week starts everyone fresh. A quest pays BOTH pass XP (feeding
    /// <see cref="SeasonPass"/>) and tokens (via <see cref="TokenWallet"/>) on claim.
    public sealed class Quest
    {
        public readonly string Id;
        public readonly string Description;
        public readonly int Target;
        public readonly int XpReward;
        public readonly int TokenReward;
        public readonly bool Weekly;
        public readonly QuestMetric Metric;

        public int Progress;     // filled in by QuestSystem from PlayerPrefs
        public bool Claimed;     // filled in by QuestSystem from PlayerPrefs

        public Quest(string id, string description, int target, int xp, int tokens, bool weekly, QuestMetric metric)
        {
            Id = id; Description = description; Target = target;
            XpReward = xp; TokenReward = tokens; Weekly = weekly; Metric = metric;
        }

        public bool Complete => Progress >= Target;
        public float Progress01 => Target <= 0 ? 1f : Mathf.Clamp01((float)Progress / Target);
    }

    /// What gameplay signal advances a quest. Mapped to GameEvents / TokenWallet in QuestSystem.
    public enum QuestMetric
    {
        RoundsPlayed,    // any LevelEnded
        RoundsWon,       // LevelEnded where the human player is the winner
        RacersEliminated,// RacerEliminated where the human player did NOT get knocked out
        TokensEarned,    // positive deltas on TokenWallet.Changed
    }

    /// The daily/weekly quest treadmill that feeds the Battle Pass. Self-bootstrapping static service:
    /// subscribes once to LevelEnded / RacerEliminated / TokenWallet.Changed and advances the active
    /// quests' progress as the player plays. Dailies reset per UTC calendar day; weeklies per ISO week
    /// (reusing DailyRewardStore's UTC-stamp idea — a comparable integer period stamp). Progress and
    /// claimed flags persist in PlayerPrefs; <see cref="Claim"/> pays SeasonPass XP + tokens.
    ///
    /// Local-only today (catalog is hard-coded); a later remote-config pass can swap the catalog and
    /// reset cadence without touching subscribers or the UI.
    public static class QuestSystem
    {
        private const string ProgressPrefix = "stumbleclone.quest.prog.";   // + periodStamp + "." + id
        private const string ClaimedPrefix  = "stumbleclone.quest.claim.";  // + periodStamp + "." + id
        private const string DailyStampKey   = "stumbleclone.quest.dailystamp";
        private const string WeeklyStampKey  = "stumbleclone.quest.weeklystamp";

        /// Raised whenever any quest's progress or claimed state changes, so UI can refresh.
        public static event Action Changed;

        // ---- catalog (static; remote-config later) ------------------------------
        // Dailies: small, same set each day. Weeklies: larger targets + bigger rewards.
        private static readonly Quest[] DailyCatalog =
        {
            new Quest("d_play3",   "Play 3 rounds",        3,   30, 40,  false, QuestMetric.RoundsPlayed),
            new Quest("d_win1",    "Win 1 round",          1,   40, 60,  false, QuestMetric.RoundsWon),
            new Quest("d_elim5",   "Eliminate 5 racers",   5,   35, 50,  false, QuestMetric.RacersEliminated),
            new Quest("d_earn150", "Earn 150 tokens",      150, 30, 40,  false, QuestMetric.TokensEarned),
        };
        private static readonly Quest[] WeeklyCatalog =
        {
            new Quest("w_play20",   "Play 20 rounds",        20,  120, 200, true, QuestMetric.RoundsPlayed),
            new Quest("w_win5",     "Win 5 rounds",          5,   150, 250, true, QuestMetric.RoundsWon),
            new Quest("w_elim40",   "Eliminate 40 racers",   40,  140, 220, true, QuestMetric.RacersEliminated),
            new Quest("w_earn1000", "Earn 1000 tokens",      1000,130, 200, true, QuestMetric.TokensEarned),
        };

        // Live working copies (the catalog with Progress/Claimed hydrated for the active period).
        private static readonly List<Quest> _daily = new List<Quest>();
        private static readonly List<Quest> _weekly = new List<Quest>();

        private static int _dailyStamp;
        private static int _weeklyStamp;
        private static bool _ready;

        // ---- self-bootstrap ------------------------------------------------------

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Bootstrap()
        {
            // Guard against domain-reload double-subscribe (Enter Play Mode without reload).
            GameEvents.LevelEnded -= OnLevelEnded;
            GameEvents.RacerEliminated -= OnRacerEliminated;
            TokenWallet.Changed -= OnTokensChanged;

            GameEvents.LevelEnded += OnLevelEnded;
            GameEvents.RacerEliminated += OnRacerEliminated;
            TokenWallet.Changed += OnTokensChanged;

            EnsureFresh();
        }

        // ---- public read API -----------------------------------------------------

        /// The active daily quests for today (UTC). Hydrated with current progress + claimed state.
        public static IReadOnlyList<Quest> Daily { get { EnsureFresh(); return _daily; } }

        /// The active weekly quests for this ISO week (UTC). Hydrated as above.
        public static IReadOnlyList<Quest> Weekly { get { EnsureFresh(); return _weekly; } }

        /// Seconds until the daily set resets (UTC midnight), for a "resets in" UI hint.
        public static TimeSpan TimeUntilDailyReset()
        {
            DateTime now = DateTime.UtcNow;
            return now.Date.AddDays(1) - now;
        }

        /// Claim a completed, unclaimed quest by id. Pays its pass XP into SeasonPass and its tokens
        /// into the wallet. Returns false if the quest is unknown, incomplete, or already claimed.
        public static bool Claim(string questId)
        {
            EnsureFresh();
            Quest q = Find(questId, out bool weekly, out int stamp);
            if (q == null || !q.Complete || q.Claimed) return false;

            q.Claimed = true;
            PlayerPrefs.SetInt(ClaimedPrefix + stamp + "." + q.Id, 1);
            PlayerPrefs.Save();

            // Pass XP first (drives tier-ups), then tokens into the wallet economy.
            SeasonPass.AddXp(q.XpReward);
            if (q.TokenReward > 0) TokenWallet.Add(q.TokenReward);

            Changed?.Invoke();
            return true;
        }

        // ---- progression hooks ---------------------------------------------------

        private static void OnLevelEnded(IRacer winner)
        {
            Advance(QuestMetric.RoundsPlayed, 1);

            var player = RacerRegistry.Player;
            bool playerWon = winner != null && player != null && ReferenceEquals(winner, player);
            if (playerWon) Advance(QuestMetric.RoundsWon, 1);
        }

        private static void OnRacerEliminated(IRacer racer)
        {
            // Count eliminations of OTHERS — the human getting knocked out shouldn't credit their own
            // "eliminate racers" quest. (We can't attribute the kill to the player from this event, so
            // this is the per-round "racers knocked out around you" proxy, matching the elimination feed.)
            if (racer != null && racer.IsPlayer) return;
            Advance(QuestMetric.RacersEliminated, 1);
        }

        // TokenWallet.Changed fires with the new balance; we want the positive delta that was earned.
        private static int _lastBalance = int.MinValue;
        private static void OnTokensChanged(int newBalance)
        {
            if (_lastBalance == int.MinValue) { _lastBalance = newBalance; return; }
            int delta = newBalance - _lastBalance;
            _lastBalance = newBalance;
            if (delta > 0) Advance(QuestMetric.TokensEarned, delta);
        }

        private static void Advance(QuestMetric metric, int amount)
        {
            if (amount <= 0) return;
            EnsureFresh();
            bool any = false;
            any |= AdvanceList(_daily, _dailyStamp, metric, amount);
            any |= AdvanceList(_weekly, _weeklyStamp, metric, amount);
            if (any) Changed?.Invoke();
        }

        private static bool AdvanceList(List<Quest> list, int stamp, QuestMetric metric, int amount)
        {
            bool changed = false;
            foreach (var q in list)
            {
                if (q.Metric != metric || q.Claimed || q.Complete) continue;
                q.Progress = Mathf.Min(q.Target, q.Progress + amount);
                PlayerPrefs.SetInt(ProgressPrefix + stamp + "." + q.Id, q.Progress);
                changed = true;
            }
            if (changed) PlayerPrefs.Save();
            return changed;
        }

        // ---- period rollover + hydration ----------------------------------------

        // Rebuild the live daily/weekly lists if the UTC day or ISO week changed since we last loaded.
        // Hydrates Progress/Claimed from PlayerPrefs under the *current* period stamp, so a new period
        // automatically reads zeros (no manual wipe needed — stale keys simply go unread).
        private static void EnsureFresh()
        {
            int today = DayStamp(DateTime.UtcNow);
            int week = WeekStamp(DateTime.UtcNow);

            if (_ready && _dailyStamp == today && _weeklyStamp == week) return;

            if (_dailyStamp != today || !_ready)
            {
                _dailyStamp = today;
                PlayerPrefs.SetInt(DailyStampKey, today);
                Hydrate(_daily, DailyCatalog, today);
            }
            if (_weeklyStamp != week || !_ready)
            {
                _weeklyStamp = week;
                PlayerPrefs.SetInt(WeeklyStampKey, week);
                Hydrate(_weekly, WeeklyCatalog, week);
            }
            PlayerPrefs.Save();
            _ready = true;
        }

        // Build fresh Quest working copies from the catalog and load their saved progress/claim for
        // this period. We clone so the static catalog stays a pristine template across resets.
        private static void Hydrate(List<Quest> target, Quest[] catalog, int stamp)
        {
            target.Clear();
            foreach (var def in catalog)
            {
                var q = new Quest(def.Id, def.Description, def.Target, def.XpReward, def.TokenReward, def.Weekly, def.Metric)
                {
                    Progress = PlayerPrefs.GetInt(ProgressPrefix + stamp + "." + def.Id, 0),
                    Claimed = PlayerPrefs.GetInt(ClaimedPrefix + stamp + "." + def.Id, 0) == 1,
                };
                target.Add(q);
            }
        }

        private static Quest Find(string id, out bool weekly, out int stamp)
        {
            foreach (var q in _daily) if (q.Id == id) { weekly = false; stamp = _dailyStamp; return q; }
            foreach (var q in _weekly) if (q.Id == id) { weekly = true; stamp = _weeklyStamp; return q; }
            weekly = false; stamp = 0; return null;
        }

        // ---- UTC period stamps (DailyRewardStore-style comparable integers) ------

        // Calendar day as yyyyMMdd (AddDays handles rollover) — same shape DailyRewardStore uses.
        private static int DayStamp(DateTime d)
        {
            DateTime u = d.Date;
            return u.Year * 10000 + u.Month * 100 + u.Day;
        }

        // ISO-8601 week as yyyyWww (e.g. 2026 week 23 => 202623). ISO weeks start Monday and the
        // first week is the one containing the year's first Thursday — computed here without the
        // System.Globalization calendar so it stays deterministic across locales.
        private static int WeekStamp(DateTime d)
        {
            DateTime date = d.Date;
            // Shift to the Thursday of this week (ISO: the week's year is its Thursday's year).
            int dow = ((int)date.DayOfWeek + 6) % 7; // Mon=0 .. Sun=6
            DateTime thursday = date.AddDays(3 - dow);
            int isoYear = thursday.Year;
            DateTime firstThursday = FirstThursdayOfYear(isoYear);
            int week = (int)Math.Floor((thursday - firstThursday).TotalDays / 7) + 1;
            return isoYear * 100 + week;
        }

        private static DateTime FirstThursdayOfYear(int year)
        {
            DateTime jan1 = new DateTime(year, 1, 1);
            int dow = ((int)jan1.DayOfWeek + 6) % 7; // Mon=0 .. Sun=6
            int offset = (3 - dow + 7) % 7;          // days from Jan 1 to the first Thursday
            return jan1.AddDays(offset);
        }
    }
}
