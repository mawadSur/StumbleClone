using UnityEngine;
using UnityEngine.UI;
using TMPro;
using StumbleClone.Core;

namespace StumbleClone.UI
{
    /// HUD for the Last Standing / Battle Royale mode. Shows alive count and a
    /// colored indicator that fades from green to red as the play zone shrinks.
    public class LastStandHUD : MonoBehaviour
    {
        [SerializeField] private TMP_Text aliveCountText;
        [SerializeField] private Image zoneIndicator;
        [SerializeField] private float initialRadius = 50f;

        private float maxRadius;

        private void OnEnable()
        {
            GameEvents.ShrinkRadiusChanged += HandleShrinkRadiusChanged;
            GameEvents.RacerEliminated += HandleRacerEliminated;
            GameEvents.LevelStarted += HandleLevelStarted;
            maxRadius = initialRadius;
            RefreshAliveCount();
            ApplyIndicatorColor(1f);
        }

        private void OnDisable()
        {
            GameEvents.ShrinkRadiusChanged -= HandleShrinkRadiusChanged;
            GameEvents.RacerEliminated -= HandleRacerEliminated;
            GameEvents.LevelStarted -= HandleLevelStarted;
        }

        private void HandleLevelStarted(LevelMode mode)
        {
            maxRadius = initialRadius;
            ApplyIndicatorColor(1f);
            RefreshAliveCount();
        }

        private void HandleShrinkRadiusChanged(float radius)
        {
            if (radius > maxRadius) maxRadius = radius;
            float t = maxRadius > 0f ? Mathf.Clamp01(radius / maxRadius) : 0f;
            ApplyIndicatorColor(t);
        }

        private void HandleRacerEliminated(IRacer racer)
        {
            RefreshAliveCount();
        }

        private void ApplyIndicatorColor(float t)
        {
            if (zoneIndicator == null) return;
            // t=1 -> green, t=0 -> red
            Color color = Color.Lerp(Color.red, Color.green, t);
            zoneIndicator.color = color;
        }

        private void RefreshAliveCount()
        {
            if (aliveCountText == null) return;
            aliveCountText.text = "Alive: " + RacerRegistry.AliveCount;
        }
    }
}
