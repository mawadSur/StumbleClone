using System;
using System.Collections;
using StumbleClone.Core;
using StumbleClone.UI;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace StumbleClone.Game
{
    public class GameManager : MonoBehaviour
    {
        public static GameManager Instance { get; private set; }

        public LevelMode currentMode;
        public LevelResult lastResult;
        public bool gameOver;

        private float _levelStartTime;

        /// The in-flight round-intro coroutine (countdown + LevelStarted raise), if any. Tracked so
        /// a fresh scene load can cancel a stale one and avoid two overlapping countdowns / a stuck
        /// Time.timeScale.
        private Coroutine _levelStartRoutine;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;
            DontDestroyOnLoad(gameObject);

            GameEvents.LevelEnded += HandleLevelEnded;
            SceneManager.sceneLoaded += HandleSceneLoaded;
        }

        private void OnDestroy()
        {
            if (Instance == this)
            {
                GameEvents.LevelEnded -= HandleLevelEnded;
                SceneManager.sceneLoaded -= HandleSceneLoaded;
                Instance = null;
            }
        }

        public void LoadLevel(LevelMode mode)
        {
            gameOver = false;
            lastResult = null;
            LevelManager.Instance.Load(mode);
        }

        public void ReturnToMenu()
        {
            gameOver = false;
            LevelManager.Instance.ReturnToMenu();
        }

        public void Quit()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        private void HandleLevelEnded(IRacer winner)
        {
            gameOver = true;
            var player = RacerRegistry.Player;
            bool playerWon = RoundOutcome.PlayerWon(winner); // shared definition — keeps QuestSystem in lockstep
            int playerRank = ComputePlayerRank(player);
            float duration = Time.time - _levelStartTime;

            lastResult = new LevelResult(currentMode, winner, playerWon, playerRank, duration);

            // Token reward — only for runs the human player took part in. Winning pays the full
            // purse; otherwise the consolation is rank-scaled off the player's placement so a near
            // miss (2nd ~52) pays far better than finishing last (~10, the floor). A Token Doubler
            // consumable (if the player owns a charge) doubles a win. Spent in the title-screen shop.
            if (player != null)
            {
                int reward = playerWon
                    ? GameConstants.TokensForWin
                    : Mathf.Max(10, 60 - (playerRank - 2) * 8);
                bool doublerUsed = playerWon && AbilityStore.ConsumeDoubler();
                if (doublerUsed) reward *= 2;
                TokenWallet.Add(reward);

                // Record the actual payout so the end screens can show it (read from lastResult).
                lastResult.tokensAwarded = reward;
                lastResult.doublerUsed = doublerUsed;
            }

            // Record to the local leaderboard — only for runs the human player took part in.
            if (player != null)
            {
                LeaderboardStore.Submit(new LeaderboardEntry
                {
                    playerName = LeaderboardStore.GetPlayerName(),
                    mode = currentMode,
                    score = lastResult.score,
                    duration = duration,
                    rank = playerRank,
                    unixTimeUtc = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                });
            }
        }

        private int ComputePlayerRank(IRacer player)
        {
            if (player == null) return -1;
            var all = RacerRegistry.All;
            int rank = 1;
            for (int i = 0; i < all.Count; i++)
            {
                if (ReferenceEquals(all[i], player)) continue;
                if (all[i].IsFinished) rank++;
            }
            return rank;
        }

        private void HandleSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (scene.name == "MainMenu") return;

            // Do NOT clear the registry or Reset() the event bus here. Per-scene racers, managers
            // (LastStanding/Race/Survival) and HUDs register/subscribe in their OnEnable, which
            // runs BEFORE this sceneLoaded callback — clearing/resetting now would wipe those fresh
            // registrations, so LastStandingManager never gets LevelStarted (no hazards spawn) and
            // the player is missing from RacerRegistry (broken win/AliveCount). Every object already
            // unregisters/unsubscribes in OnDisable on scene unload, so this cleanup is redundant.
            // GameManager's own LevelEnded subscription is made once in Awake and persists.

            // Cancel any stale intro from a previous round and clear its freeze, so reloading a
            // level (or jumping between modes) never leaves two countdowns running or timeScale
            // pinned at 0. A fresh countdown will re-freeze below.
            if (_levelStartRoutine != null)
            {
                StopCoroutine(_levelStartRoutine);
                _levelStartRoutine = null;
                Time.timeScale = 1f;
            }

            _levelStartRoutine = StartCoroutine(RaiseLevelStartedNextFrame());
        }

        private IEnumerator RaiseLevelStartedNextFrame()
        {
            // Wait one frame so per-scene racers/managers/HUDs finish their OnEnable registration
            // (see HandleSceneLoaded) before the round begins.
            yield return null;

            // Freeze the simulation so nothing acts during the title card / countdown. timeScale 0
            // pauses physics (player + bots) and FixedUpdate for free — no need to touch any other
            // script. The countdown overlay runs on UNSCALED time, so it animates while frozen.
            Time.timeScale = 0f;

            bool released = false;
            void Release()
            {
                if (released) return;
                released = true;

                // GO! — unfreeze and start the round on the same frame, so input and hazards
                // resume exactly when LevelStarted fires. Keep _levelStartTime pinned to GO so run
                // durations exclude the countdown.
                Time.timeScale = 1f;
                _levelStartTime = Time.time;
                GameEvents.RaiseLevelStarted(currentMode);
            }

            RoundIntro.Show(currentMode, Release);

            // Safety net: if the overlay can't run for any reason (e.g. destroyed before GO), make
            // sure the round still starts and the freeze is lifted rather than hanging the game.
            float guard = 0f;
            const float guardTimeout = 6f; // longer than the longest countdown (3..2..1..GO! + holds)
            while (!released && guard < guardTimeout)
            {
                guard += Time.unscaledDeltaTime;
                yield return null;
            }
            Release();
            _levelStartRoutine = null;
        }
    }
}
