using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using StumbleClone.Core;
using UnityEngine;

namespace StumbleClone.Game
{
    /// Local high-score store. Persists to PlayerPrefs as JSON (the safest cross-platform
    /// backing — works on WebGL, Android, iOS, and standalone alike). Also owns the player's
    /// chosen name. A future global board (Vercel) can layer on top via the same Submit/GetTop
    /// API with an offline fallback to this.
    public static class LeaderboardStore
    {
        private const string DataKey = "stumbleclone.leaderboard.v1";
        private const string NameKey = "stumbleclone.playername";
        private const int MaxPerMode = 50;

        private static List<LeaderboardEntry> _cache;

        // ---- player name --------------------------------------------------------

        public static string GetPlayerName()
        {
            string n = PlayerPrefs.GetString(NameKey, "");
            return string.IsNullOrWhiteSpace(n) ? "Player" : n;
        }

        public static void SetPlayerName(string name)
        {
            PlayerPrefs.SetString(NameKey, string.IsNullOrWhiteSpace(name) ? "Player" : name.Trim());
            PlayerPrefs.Save();
        }

        // ---- scores -------------------------------------------------------------

        public static List<LeaderboardEntry> GetTop(LevelMode mode, int n)
        {
            EnsureLoaded();
            return _cache.Where(e => e != null && e.mode == mode)
                         .OrderByDescending(e => e.score)
                         .Take(Mathf.Max(0, n))
                         .ToList();
        }

        /// Adds an entry, trims the mode to MaxPerMode, persists. Returns true if it is this
        /// player's new personal best for the mode.
        public static bool Submit(LeaderboardEntry entry)
        {
            if (entry == null) return false;
            EnsureLoaded();

            float prevBest = _cache.Where(e => e != null && e.mode == entry.mode && e.playerName == entry.playerName)
                                   .Select(e => e.score)
                                   .DefaultIfEmpty(float.MinValue)
                                   .Max();

            _cache.Add(entry);
            TrimMode(entry.mode);
            Save();
            return entry.score > prevBest;
        }

        public static bool IsHighScore(LevelMode mode, float score)
        {
            var top = GetTop(mode, 1);
            return top.Count == 0 || score >= top[0].score;
        }

        public static void Clear()
        {
            _cache = new List<LeaderboardEntry>();
            Save();
        }

        // ---- persistence --------------------------------------------------------

        private static void EnsureLoaded()
        {
            if (_cache != null) return;
            _cache = new List<LeaderboardEntry>();
            string json = PlayerPrefs.GetString(DataKey, "");
            if (string.IsNullOrEmpty(json)) return;
            try
            {
                var list = JsonConvert.DeserializeObject<List<LeaderboardEntry>>(json);
                if (list != null) _cache = list;
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[Leaderboard] load failed, starting fresh: {ex.Message}");
            }
        }

        private static void TrimMode(LevelMode mode)
        {
            var ranked = _cache.Where(e => e != null && e.mode == mode)
                               .OrderByDescending(e => e.score)
                               .ToList();
            if (ranked.Count <= MaxPerMode) return;
            var keep = new HashSet<LeaderboardEntry>(ranked.Take(MaxPerMode));
            _cache.RemoveAll(e => e != null && e.mode == mode && !keep.Contains(e));
        }

        private static void Save()
        {
            try
            {
                PlayerPrefs.SetString(DataKey, JsonConvert.SerializeObject(_cache));
                PlayerPrefs.Save();
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[Leaderboard] save failed: {ex.Message}");
            }
        }
    }
}
