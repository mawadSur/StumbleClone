using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using StumbleClone.Core;
using StumbleClone.Game;
using Unity.Services.Authentication;
using Unity.Services.CloudSave;
using Unity.Services.Core;
using Unity.Services.Leaderboards;
using UnityEngine;

namespace StumbleClone.Net
{
    /// Server-authoritative-ish backend over Unity Gaming Services (UGS).
    ///
    /// Responsibilities:
    ///   - Anonymous auth + one-time UnityServices init (EnsureSignedInAsync).
    ///   - Cloud Save round-trip of local progress (tokens, owned skins, unlocked modes,
    ///     season tier) so it survives device swaps and PlayerPrefs / cache wipes.
    ///   - Leaderboard score submit + top-N fetch.
    ///
    /// Design notes:
    ///   - PlayerPrefs stays the single source of truth at runtime; this class only mirrors it
    ///     up/down. The game works fully OFFLINE — nothing here is on the gameplay hot path.
    ///   - Merge policy on Load is "cloud wins if newer", decided by a UTC "savedAtUtc" stamp
    ///     written alongside the payload. A fresh cloud account (no stamp) never clobbers local.
    ///   - DORMANT: nothing in the project calls these yet. Single-player is unaffected until the
    ///     title screen / results flow is wired to call SaveAsync / SubmitScoreAsync.
    ///   - Every cloud call is wrapped so a missing/disabled service degrades to a warning, never
    ///     an exception that reaches gameplay. The warning names the exact dashboard toggles.
    ///
    /// REQUIRES (one-time, in the Unity Cloud dashboard for this linked project):
    ///   Authentication (Anonymous sign-in) + Cloud Save + Leaderboards all enabled.
    public static class BackendService
    {
        // ---- Cloud Save keys (single JSON blob keeps the request count to one) ----
        private const string SaveKey = "stumble_progress_v1";

        // ---- PlayerPrefs keys this service reads/writes. These MUST mirror the keys used by
        // TokenWallet / SkinInventory / LevelProgress so a cloud restore actually re-grants state. ----
        private const string TokensPref = "stumbleclone.tokens";
        private const string SkinOwnedPrefix = "stumbleclone.skinowned.";   // + skin id
        private const string ModeUnlockedPrefix = "stumbleclone.modeunlocked."; // + (int)LevelMode
        private const string SeasonTierPref = "stumbleclone.seasontier";    // forward-compat; 0 until a season system writes it
        private const string LastSavedAtPref = "stumbleclone.cloud.savedatutc"; // local stamp for merge

        private static bool s_initStarted;
        private static Task s_initTask;

        private const string DashboardHint =
            "Enable Cloud Save + Leaderboards + Authentication(Anonymous) in the Unity Cloud dashboard.";

        /// True once an anonymous session is established. Cheap to poll before a cloud call.
        public static bool IsSignedIn
        {
            get
            {
                try { return AuthenticationService.Instance != null && AuthenticationService.Instance.IsSignedIn; }
                catch { return false; }
            }
        }

        /// Initialize UnityServices once and sign in anonymously. Safe to call repeatedly /
        /// concurrently — the init Task is cached and double sign-in is guarded.
        /// Returns true if signed in afterward, false if UGS is unavailable (offline / disabled).
        public static async Task<bool> EnsureSignedInAsync()
        {
            try
            {
                // Guard double-init: reuse the in-flight / completed init Task.
                if (s_initTask == null && !s_initStarted)
                {
                    s_initStarted = true;
                    s_initTask = InitializeOnceAsync();
                }
                if (s_initTask != null) await s_initTask;

                if (!AuthenticationService.Instance.IsSignedIn)
                    await AuthenticationService.Instance.SignInAnonymouslyAsync();

                return AuthenticationService.Instance.IsSignedIn;
            }
            catch (Exception e)
            {
                // Reset so a later call can retry a transient failure.
                s_initStarted = false;
                s_initTask = null;
                Debug.LogWarning($"[BackendService] Sign-in failed ({e.GetType().Name}: {e.Message}). {DashboardHint}");
                return false;
            }
        }

        private static async Task InitializeOnceAsync()
        {
            if (UnityServices.State != ServicesInitializationState.Initialized)
                await UnityServices.InitializeAsync();
        }

        // -------------------------------------------------------------------------------------
        // Cloud Save
        // -------------------------------------------------------------------------------------

        /// Push local progress to Cloud Save. No-op (warning) if UGS is unavailable. Offline-safe.
        public static async Task<bool> SaveAsync()
        {
            if (!await EnsureSignedInAsync()) return false;

            try
            {
                string stampUtc = DateTime.UtcNow.ToString("o");
                PlayerPrefs.SetString(LastSavedAtPref, stampUtc);
                PlayerPrefs.Save();

                var data = new Dictionary<string, object> { { SaveKey, BuildLocalSnapshotJson(stampUtc) } };
                await CloudSaveService.Instance.Data.Player.SaveAsync(data);
                return true;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[BackendService] Cloud Save write failed ({e.GetType().Name}: {e.Message}). {DashboardHint}");
                return false;
            }
        }

        /// Pull cloud progress and merge into local with a "cloud wins if newer" policy.
        /// Returns true if cloud data was applied; false if there was nothing newer, no cloud
        /// save yet, or UGS is unavailable (in which case local is left untouched).
        public static async Task<bool> LoadAsync()
        {
            if (!await EnsureSignedInAsync()) return false;

            try
            {
                var keys = new HashSet<string> { SaveKey };
                Dictionary<string, Unity.Services.CloudSave.Models.Item> loaded =
                    await CloudSaveService.Instance.Data.Player.LoadAsync(keys);

                if (loaded == null || !loaded.TryGetValue(SaveKey, out var item) || item == null)
                    return false; // fresh cloud account — keep local as-is

                string json = item.Value.GetAs<string>();
                if (string.IsNullOrEmpty(json)) return false;

                var snap = JsonUtility.FromJson<ProgressSnapshot>(json);
                if (snap == null) return false;

                // Merge: only overwrite local when the cloud copy is strictly newer.
                if (!IsCloudNewer(snap.savedAtUtc)) return false;

                ApplySnapshotToLocal(snap);
                return true;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[BackendService] Cloud Save read failed ({e.GetType().Name}: {e.Message}). {DashboardHint}");
                return false;
            }
        }

        // -------------------------------------------------------------------------------------
        // Leaderboards
        // -------------------------------------------------------------------------------------

        /// Submit a score to the given leaderboard. Returns true on success. Offline-safe (warning).
        public static async Task<bool> SubmitScoreAsync(string leaderboardId, double score)
        {
            if (string.IsNullOrEmpty(leaderboardId)) return false;
            if (!await EnsureSignedInAsync()) return false;

            try
            {
                await LeaderboardsService.Instance.AddPlayerScoreAsync(leaderboardId, score);
                return true;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[BackendService] Leaderboard submit failed ({e.GetType().Name}: {e.Message}). {DashboardHint}");
                return false;
            }
        }

        /// Fetch the top <paramref name="count"/> entries for a leaderboard. Returns the raw UGS
        /// entries, or an empty list if UGS is unavailable. Offline-safe (warning).
        public static async Task<List<Unity.Services.Leaderboards.Models.LeaderboardEntry>> GetTopScoresAsync(
            string leaderboardId, int count)
        {
            var empty = new List<Unity.Services.Leaderboards.Models.LeaderboardEntry>();
            if (string.IsNullOrEmpty(leaderboardId)) return empty;
            if (count <= 0) count = 10;
            if (!await EnsureSignedInAsync()) return empty;

            try
            {
                var options = new GetScoresOptions { Offset = 0, Limit = count };
                var response = await LeaderboardsService.Instance.GetScoresAsync(leaderboardId, options);
                return response?.Results ?? empty;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[BackendService] Leaderboard fetch failed ({e.GetType().Name}: {e.Message}). {DashboardHint}");
                return empty;
            }
        }

        // -------------------------------------------------------------------------------------
        // Local <-> snapshot mapping
        // -------------------------------------------------------------------------------------

        /// Serializable shape persisted as the Cloud Save JSON blob. Lists are parallel-free
        /// (ids only) so the catalog can grow without breaking old saves.
        [Serializable]
        private class ProgressSnapshot
        {
            public string savedAtUtc;          // ISO-8601 round-trip ("o"); empty = unknown/oldest
            public int tokens;
            public int seasonTier;
            public List<string> ownedSkinIds = new List<string>();
            public List<int> unlockedModes = new List<int>(); // (int)LevelMode values
        }

        private static string BuildLocalSnapshotJson(string stampUtc)
        {
            var snap = new ProgressSnapshot
            {
                savedAtUtc = stampUtc,
                tokens = TokenWallet.Balance,
                seasonTier = PlayerPrefs.GetInt(SeasonTierPref, 0),
            };

            foreach (string id in SkinCatalog.Ids)
                if (SkinInventory.IsOwned(id))
                    snap.ownedSkinIds.Add(id);

            foreach (LevelMode mode in (LevelMode[])Enum.GetValues(typeof(LevelMode)))
                if (LevelProgress.IsUnlocked(mode))
                    snap.unlockedModes.Add((int)mode);

            return JsonUtility.ToJson(snap);
        }

        private static bool IsCloudNewer(string cloudStampUtc)
        {
            if (string.IsNullOrEmpty(cloudStampUtc)) return false;
            if (!DateTime.TryParse(
                    cloudStampUtc, null,
                    System.Globalization.DateTimeStyles.RoundtripKind, out DateTime cloudUtc))
                return false;

            string localStr = PlayerPrefs.GetString(LastSavedAtPref, string.Empty);
            if (string.IsNullOrEmpty(localStr)) return true; // local never synced -> trust cloud
            if (!DateTime.TryParse(
                    localStr, null,
                    System.Globalization.DateTimeStyles.RoundtripKind, out DateTime localUtc))
                return true;

            return cloudUtc > localUtc;
        }

        /// Apply a cloud snapshot to local PlayerPrefs. We write the same keys that TokenWallet /
        /// SkinInventory / LevelProgress read, so ownership/unlocks actually take effect. Tokens are
        /// max()'d to avoid a stale cloud copy ever reducing a balance the merge already let through;
        /// ownership/unlocks are additive (a cloud restore only grants, never revokes).
        private static void ApplySnapshotToLocal(ProgressSnapshot snap)
        {
            int mergedTokens = Mathf.Max(TokenWallet.Balance, Mathf.Max(0, snap.tokens));
            PlayerPrefs.SetInt(TokensPref, mergedTokens);

            PlayerPrefs.SetInt(SeasonTierPref, Mathf.Max(PlayerPrefs.GetInt(SeasonTierPref, 0), snap.seasonTier));

            if (snap.ownedSkinIds != null)
                foreach (string id in snap.ownedSkinIds)
                    if (!string.IsNullOrEmpty(id) && SkinCatalog.IndexOf(id) > 0) // index 0 is the free default
                        PlayerPrefs.SetInt(SkinOwnedPrefix + id, 1);

            if (snap.unlockedModes != null)
                foreach (int modeValue in snap.unlockedModes)
                    PlayerPrefs.SetInt(ModeUnlockedPrefix + modeValue, 1);

            // Record that local is now in sync with this cloud stamp.
            if (!string.IsNullOrEmpty(snap.savedAtUtc))
                PlayerPrefs.SetString(LastSavedAtPref, snap.savedAtUtc);

            PlayerPrefs.Save();

            // Note: TokenWallet / SkinInventory expose no setters and no "refresh" event we can fire
            // without changing balance, so their Changed events do not re-emit here. That's fine — UI
            // reads the (now-updated) PlayerPrefs the next time a screen opens. Callers that want an
            // immediate refresh should LoadAsync() before the title/shop screen is shown.
        }
    }
}
