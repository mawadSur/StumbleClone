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

            RacerRegistry.Clear();
            GameEvents.Reset();

            GameEvents.LevelEnded += HandleLevelEnded;

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
