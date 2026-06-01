using System;
using UnityEngine;
using TMPro;
using StumbleClone.Core;

namespace StumbleClone.UI
{
    /// HUD for the Survival mode. Shows the number of racers still alive and
    /// the seconds remaining on the survival timer.
    public class SurvivalHUD : MonoBehaviour
    {
        [SerializeField] private TMP_Text aliveCountText;
        [SerializeField] private TMP_Text timerText;

        private void OnEnable()
        {
            GameEvents.SurvivalTimerTick += HandleTimerTick;
            GameEvents.RacerEliminated += HandleRacerEliminated;
            RefreshAliveCount();
        }

        private void OnDisable()
        {
            GameEvents.SurvivalTimerTick -= HandleTimerTick;
            GameEvents.RacerEliminated -= HandleRacerEliminated;
        }

        private void HandleTimerTick(float secondsRemaining)
        {
            if (timerText == null) return;
            if (secondsRemaining < 0f) secondsRemaining = 0f;
            timerText.text = TimeSpan.FromSeconds(secondsRemaining).ToString(@"mm\:ss");
        }

        private void HandleRacerEliminated(IRacer racer)
        {
            RefreshAliveCount();
        }

        private void RefreshAliveCount()
        {
            if (aliveCountText == null) return;
            aliveCountText.text = "Alive: " + RacerRegistry.AliveCount;
        }
    }
}
