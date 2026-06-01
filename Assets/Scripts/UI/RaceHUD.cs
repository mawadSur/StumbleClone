using System;
using UnityEngine;
using TMPro;
using StumbleClone.Core;

namespace StumbleClone.UI
{
    /// HUD for the Race mode. Shows the player's current rank and an elapsed
    /// timer that starts on LevelStarted.
    public class RaceHUD : MonoBehaviour
    {
        [SerializeField] private TMP_Text rankText;
        [SerializeField] private TMP_Text timerText;

        private float levelStartTime;
        private bool timerRunning;
        private int currentRank = 1;

        private void OnEnable()
        {
            GameEvents.LevelStarted += HandleLevelStarted;
            GameEvents.LevelEnded += HandleLevelEnded;
            GameEvents.RacerRankChanged += HandleRankChanged;
            RefreshRankText();
        }

        private void OnDisable()
        {
            GameEvents.LevelStarted -= HandleLevelStarted;
            GameEvents.LevelEnded -= HandleLevelEnded;
            GameEvents.RacerRankChanged -= HandleRankChanged;
        }

        private void Update()
        {
            if (!timerRunning || timerText == null) return;
            float elapsed = Time.time - levelStartTime;
            if (elapsed < 0f) elapsed = 0f;
            timerText.text = TimeSpan.FromSeconds(elapsed).ToString(@"mm\:ss");
        }

        private void HandleLevelStarted(LevelMode mode)
        {
            levelStartTime = Time.time;
            timerRunning = true;
            RefreshRankText();
        }

        private void HandleLevelEnded(IRacer winner)
        {
            timerRunning = false;
        }

        private void HandleRankChanged(IRacer racer, int rank)
        {
            if (racer == null || !racer.IsPlayer) return;
            currentRank = rank;
            RefreshRankText();
        }

        private void RefreshRankText()
        {
            if (rankText == null) return;
            int total = RacerRegistry.All.Count;
            if (total <= 0) total = 1;
            rankText.text = currentRank + " / " + total;
        }
    }
}
