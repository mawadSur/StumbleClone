using System;
using System.Collections;
using StumbleClone.Core;
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
            bool playerWon = winner != null && player != null && ReferenceEquals(winner, player);
            int playerRank = ComputePlayerRank(player);
            float duration = Time.time - _levelStartTime;

            lastResult = new LevelResult(currentMode, winner, playerWon, playerRank, duration);

            // Token reward — only for runs the human player took part in. Winning pays the full
            // purse; merely surviving to the end pays a consolation. Spent in the title-screen shop.
            if (player != null)
            {
                TokenWallet.Add(playerWon ? GameConstants.TokensForWin : GameConstants.TokensForFinish);
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

            StartCoroutine(RaiseLevelStartedNextFrame());
        }

        private IEnumerator RaiseLevelStartedNextFrame()
        {
            yield return null;
            _levelStartTime = Time.time;
            GameEvents.RaiseLevelStarted(currentMode);
        }
    }
}
