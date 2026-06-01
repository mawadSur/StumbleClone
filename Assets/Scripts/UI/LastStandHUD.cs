using UnityEngine;
using UnityEngine.UI;
using TMPro;
using StumbleClone.Core;

namespace StumbleClone.UI
{
    /// HUD for the Last Standing knockout arena: shows the alive count and a status indicator.
    /// (The old shrinking-zone mechanic was replaced by the telegraphed hazard spawner, so the
    /// indicator now just signals "hazards active" rather than zone radius.)
    public class LastStandHUD : MonoBehaviour
    {
        [SerializeField] private TMP_Text aliveCountText;
        [SerializeField] private Image zoneIndicator;

        private void Awake()
        {
            ThemeBinder.StyleText(aliveCountText, UITheme.OnSurface);
        }

        private void OnEnable()
        {
            GameEvents.RacerEliminated += HandleRacerEliminated;
            GameEvents.LevelStarted += HandleLevelStarted;
            RefreshAliveCount();
            ApplyIndicatorColor();
        }

        private void OnDisable()
        {
            GameEvents.RacerEliminated -= HandleRacerEliminated;
            GameEvents.LevelStarted -= HandleLevelStarted;
        }

        private void HandleLevelStarted(LevelMode mode)
        {
            ApplyIndicatorColor();
            RefreshAliveCount();
        }

        private void HandleRacerEliminated(IRacer racer) => RefreshAliveCount();

        private void ApplyIndicatorColor()
        {
            if (zoneIndicator == null) return;
            zoneIndicator.color = new Color(UITheme.Danger.r, UITheme.Danger.g, UITheme.Danger.b, 0.9f); // hazards active
        }

        private void RefreshAliveCount()
        {
            if (aliveCountText == null) return;
            aliveCountText.text = "Alive: " + RacerRegistry.AliveCount;
        }
    }
}
