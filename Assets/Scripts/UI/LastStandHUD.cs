using UnityEngine;
using UnityEngine.UI;
using TMPro;
using StumbleClone.Core;
using StumbleClone.Game;
using StumbleClone.Obstacles;

namespace StumbleClone.UI
{
    /// HUD for the Last Standing (Knockout) arena. Knockout runs TWO pressure systems at once:
    /// a telegraphed hazard wave spawner AND a shrinking safe zone (ArenaShrinker) that eliminates
    /// racers stranded outside CurrentSafeRadius. This HUD communicates both: the alive count plus
    /// a LIVE safe-zone readout — the indicator pulses toward danger as the ring closes, a label
    /// shows the current radius, and a bold 'GET INSIDE!' warning fires whenever the player is
    /// outside the ring. Reads ArenaShrinker.Active/Center/CurrentSafeRadius and
    /// RacerRegistry.Player each frame.
    public class LastStandHUD : MonoBehaviour
    {
        [SerializeField] private TMP_Text aliveCountText;
        [SerializeField] private Image zoneIndicator;

        // Runtime-built (not scene-wired) so the live zone read-out works without a scene rebuild.
        private TMP_Text _zoneLabel;     // "SAFE ZONE  12m" — current radius
        private TMP_Text _warningLabel;  // "GET INSIDE!" — only while the player is outside the ring
        private bool _extrasBuilt;

        private void Awake()
        {
            ThemeBinder.StyleText(aliveCountText, UITheme.OnSurface);
        }

        private void OnEnable()
        {
            GameEvents.RacerEliminated += HandleRacerEliminated;
            GameEvents.LevelStarted += HandleLevelStarted;
            RefreshAliveCount();
        }

        private void OnDisable()
        {
            GameEvents.RacerEliminated -= HandleRacerEliminated;
            GameEvents.LevelStarted -= HandleLevelStarted;
        }

        private void HandleLevelStarted(LevelMode mode)
        {
            RefreshAliveCount();
        }

        private void HandleRacerEliminated(IRacer racer) => RefreshAliveCount();

        private void Update()
        {
            UpdateZoneIndicator();
        }

        /// Live safe-zone feedback. While the shrinker is active: tint the indicator from safe
        /// (gold) toward danger (red) as the ring closes, show the current radius, and flash a
        /// 'GET INSIDE!' warning whenever the player is outside CurrentSafeRadius. When inactive
        /// (e.g. before the round begins), keep the read-out quiet.
        private void UpdateZoneIndicator()
        {
            EnsureExtras();

            if (!ArenaShrinker.Active)
            {
                if (zoneIndicator != null)
                    zoneIndicator.color = new Color(UITheme.OnSurfaceMuted.r, UITheme.OnSurfaceMuted.g, UITheme.OnSurfaceMuted.b, 0.6f);
                if (_zoneLabel != null) _zoneLabel.text = "";
                if (_warningLabel != null) _warningLabel.gameObject.SetActive(false);
                return;
            }

            float radius = ArenaShrinker.CurrentSafeRadius;

            // Player distance from the ring centre (XZ plane).
            bool playerOutside = false;
            IRacer player = RacerRegistry.Player;
            if (player != null && player.IsAlive && !player.IsFinished && player.Transform != null)
            {
                Vector3 c = ArenaShrinker.Center;
                Vector3 pos = player.Transform.position;
                float dx = pos.x - c.x;
                float dz = pos.z - c.z;
                playerOutside = (dx * dx + dz * dz) > radius * radius;
            }

            // Indicator colour: outside → solid danger; inside → gold→red as the ring tightens.
            if (zoneIndicator != null)
            {
                Color col;
                if (playerOutside)
                {
                    // Pulse so an outside player can't miss it (gated behind Reduced Motion).
                    float a = SettingsStore.ReducedMotion
                        ? 1f
                        : 0.6f + 0.4f * Mathf.Abs(Mathf.Sin(Time.unscaledTime * 6f));
                    col = new Color(UITheme.Danger.r, UITheme.Danger.g, UITheme.Danger.b, a);
                }
                else
                {
                    col = Color.Lerp(UITheme.Gold, UITheme.Danger, 0.5f);
                    col.a = 0.9f;
                }
                zoneIndicator.color = col;
            }

            if (_zoneLabel != null)
                _zoneLabel.text = "SAFE ZONE  " + Mathf.RoundToInt(radius) + "m";

            if (_warningLabel != null)
            {
                _warningLabel.gameObject.SetActive(playerOutside);
                if (playerOutside && !SettingsStore.ReducedMotion)
                {
                    // Pulse the warning's alpha so it reads as urgent. Reduced Motion shows it steady.
                    float a = 0.55f + 0.45f * Mathf.Abs(Mathf.Sin(Time.unscaledTime * 6f));
                    Color w = _warningLabel.color; w.a = a; _warningLabel.color = w;
                }
                else if (playerOutside)
                {
                    Color w = _warningLabel.color; w.a = 1f; _warningLabel.color = w;
                }
            }
        }

        /// Lazily build the zone radius label (next to the indicator) and the centered
        /// 'GET INSIDE!' warning. Parented under the alive-count's canvas so it shares the HUD
        /// layer; no-op if there's nowhere to parent or already built.
        private void EnsureExtras()
        {
            if (_extrasBuilt) return;

            Transform parent = ResolveHudParent();
            if (parent == null) return;
            _extrasBuilt = true;

            // Radius read-out sits just below the indicator dot / alive count, top-left of screen.
            _zoneLabel = RuntimeUI.Label(parent, "", 30,
                new Vector2(0f, 1f), new Vector2(40f, -110f), new Vector2(360f, 44f),
                TextAlignmentOptions.Left);
            _zoneLabel.color = UITheme.Gold;

            // Big centered warning, near the top so it doesn't cover the action.
            _warningLabel = RuntimeUI.Label(parent, "GET INSIDE!", 70,
                new Vector2(0.5f, 1f), new Vector2(0f, -160f), new Vector2(900f, 100f));
            _warningLabel.fontStyle = FontStyles.Bold;
            _warningLabel.color = UITheme.Danger;
            _warningLabel.gameObject.SetActive(false);
        }

        /// Prefer the indicator's RectTransform parent (so the read-out shares the HUD canvas);
        /// fall back to the alive-count label's parent, then this object's transform.
        private Transform ResolveHudParent()
        {
            if (zoneIndicator != null && zoneIndicator.transform.parent != null)
                return zoneIndicator.transform.parent;
            if (aliveCountText != null && aliveCountText.transform.parent != null)
                return aliveCountText.transform.parent;
            return transform;
        }

        private void RefreshAliveCount()
        {
            if (aliveCountText == null) return;
            aliveCountText.text = "Alive: " + RacerRegistry.AliveCount;
        }
    }
}
