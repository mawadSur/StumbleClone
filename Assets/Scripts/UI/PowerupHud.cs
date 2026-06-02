using StumbleClone.Core;
using StumbleClone.Player;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace StumbleClone.UI
{
    /// On-screen badges for the local player's active power-up buffs (Shield / Speed / SuperJump).
    /// Each frame it reads the buff state off <see cref="PlayerController"/> (via
    /// <see cref="RacerRegistry.Player"/>) and shows a small rounded badge per active buff:
    /// Shield = cyan, Speed = yellow, SuperJump = magenta. Timed buffs also show a shrinking
    /// countdown bar and a seconds readout; the shield (no timer) shows just its letter.
    ///
    /// Self-bootstrapping: one instance is created automatically in every gameplay scene
    /// (Level_Race / Level_Survival / Level_LastStanding), so no per-scene wiring is needed —
    /// matching SpectateController's pattern. The badges are built once and updated in place;
    /// no per-frame allocation or instantiation. Inactive buffs hide their badge.
    public sealed class PowerupHud : MonoBehaviour
    {
        // ---- Layout (1920x1080 reference; badges stack in the top-right corner) ----
        private const float BadgeWidth = 168f;
        private const float BadgeHeight = 64f;
        private const float BadgeGap = 12f;
        private const float MarginRight = -24f;   // from the right edge (negative = inward)
        private const float MarginTop = -24f;     // from the top edge (negative = downward)
        private const float BarHeight = 8f;

        // Reference durations for the countdown bar fill (the Powerup grants run 5s). The bar is a
        // visual ratio only — remaining time still drives the hide and the seconds readout, so an
        // out-of-range duration just clamps the bar rather than misreporting the buff.
        private const float SpeedDuration = 5f;
        private const float JumpDuration = 5f;

        private static PowerupHud _instance;

        private GameObject _root;
        private Badge _shield;
        private Badge _speed;
        private Badge _jump;

        // ---- self-bootstrap (zero scene wiring) ---------------------------------

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded; // guard against double-subscribe
            SceneManager.sceneLoaded += OnSceneLoaded;
            EnsureForScene(SceneManager.GetActiveScene());
        }

        private static void OnSceneLoaded(Scene scene, LoadSceneMode mode) => EnsureForScene(scene);

        private static void EnsureForScene(Scene scene)
        {
            // Gameplay scenes only (Level_Race / Level_Survival / Level_LastStanding).
            if (!scene.IsValid() || !scene.name.StartsWith("Level")) return;
            if (FindAnyObjectByType<PowerupHud>() != null) return;
            new GameObject("PowerupHud").AddComponent<PowerupHud>();
        }

        private void Awake()
        {
            // De-dupe: a manager (or a stale instance) may also have created one.
            if (_instance != null && _instance != this)
            {
                Destroy(gameObject);
                return;
            }
            _instance = this;
        }

        private void OnDisable()
        {
            if (_instance == this) _instance = null;
        }

        private void Start()
        {
            Build();
        }

        private void Update()
        {
            var player = RacerRegistry.Player as PlayerController;
            if (player == null)
            {
                // No local player (or not a PlayerController) — hide everything.
                _shield.SetActive(false);
                _speed.SetActive(false);
                _jump.SetActive(false);
                return;
            }

            float speedLeft = player.SpeedBoostRemaining;
            float jumpLeft = player.JumpBoostRemaining;

            UpdateTimed(_speed, speedLeft, SpeedDuration);
            UpdateTimed(_jump, jumpLeft, JumpDuration);
            UpdateShield(_shield, player.ShieldActive);

            // Re-stack so visible badges sit flush with no gaps left by a hidden one.
            Restack();
        }

        // ---- per-badge updates (no allocation) ----------------------------------

        private static void UpdateTimed(Badge badge, float remaining, float duration)
        {
            bool on = remaining > 0f;
            badge.SetActive(on);
            if (!on) return;

            badge.SetSeconds(remaining);
            float ratio = duration > 0.001f ? Mathf.Clamp01(remaining / duration) : 0f;
            badge.SetBarFill(ratio);
        }

        private static void UpdateShield(Badge badge, bool active)
        {
            badge.SetActive(active);
            // Shield has no timer — its readout stays its letter and the bar holds full.
        }

        // Position each visible badge top-down so hidden buffs leave no gap.
        private void Restack()
        {
            float y = MarginTop;
            y = Place(_shield, y);
            y = Place(_speed, y);
            y = Place(_jump, y);
        }

        private static float Place(Badge badge, float y)
        {
            if (!badge.IsActive) return y;
            badge.SetTop(y);
            return y - (BadgeHeight + BadgeGap);
        }

        // ---- build (once) -------------------------------------------------------

        private void Build()
        {
            _root = RuntimeUI.Overlay("PowerupHud", 60);

            // Cyan shield (one-use; no countdown bar). Yellow speed + magenta superjump are timed.
            _shield = CreateBadge("ShieldBadge", "S", new Color(0.15f, 0.85f, 1f), timed: false);
            _speed = CreateBadge("SpeedBadge", "SPD", new Color(1f, 0.92f, 0.15f), timed: true);
            _jump = CreateBadge("JumpBadge", "JMP", new Color(1f, 0.2f, 0.9f), timed: true);

            _shield.SetActive(false);
            _speed.SetActive(false);
            _jump.SetActive(false);
        }

        // Build one badge: a rounded panel anchored to the top-right, a bold label, and (for timed
        // buffs) a shrinking countdown bar pinned to the bottom edge.
        private Badge CreateBadge(string name, string letter, Color color, bool timed)
        {
            var panel = RuntimeUI.Panel(_root.transform, name, color,
                new Vector2(1f, 1f), new Vector2(1f, 1f), Vector2.zero, Vector2.zero);
            panel.sprite = UITheme.RoundedSprite();
            panel.type = Image.Type.Sliced;

            var rt = panel.rectTransform;
            rt.pivot = new Vector2(1f, 1f);     // anchor by the top-right corner
            rt.sizeDelta = new Vector2(BadgeWidth, BadgeHeight);
            rt.anchoredPosition = new Vector2(MarginRight, MarginTop);

            var label = RuntimeUI.Label(panel.transform, letter, 30,
                new Vector2(0.5f, 0.5f), new Vector2(0f, timed ? 4f : 0f),
                new Vector2(BadgeWidth, BadgeHeight - (timed ? BarHeight : 0f)));
            label.fontStyle = FontStyles.Bold;
            label.color = new Color(0.05f, 0.06f, 0.1f, 1f); // dark text reads on the bright badge

            Image bar = null;
            if (timed)
            {
                // Bottom-pinned bar; width shrinks with the remaining-time ratio via fillAmount.
                bar = RuntimeUI.Panel(panel.transform, "Bar", new Color(0.05f, 0.06f, 0.1f, 0.85f),
                    new Vector2(0f, 0f), new Vector2(1f, 0f),
                    new Vector2(8f, 6f), new Vector2(-8f, 6f + BarHeight));
                bar.type = Image.Type.Filled;
                bar.fillMethod = Image.FillMethod.Horizontal;
                bar.fillOrigin = (int)Image.OriginHorizontal.Left;
                bar.fillAmount = 1f;
            }

            return new Badge(panel, label, bar, letter);
        }

        // ---- badge handle (cached references; mutated in place) ------------------

        private readonly struct Badge
        {
            private readonly Image _panel;
            private readonly TMP_Text _label;
            private readonly Image _bar;     // null for the untimed shield
            private readonly string _letter;

            public Badge(Image panel, TMP_Text label, Image bar, string letter)
            {
                _panel = panel;
                _label = label;
                _bar = bar;
                _letter = letter;
            }

            public bool IsActive => _panel != null && _panel.gameObject.activeSelf;

            public void SetActive(bool on)
            {
                if (_panel != null && _panel.gameObject.activeSelf != on)
                    _panel.gameObject.SetActive(on);
            }

            public void SetTop(float y)
            {
                if (_panel == null) return;
                var rt = _panel.rectTransform;
                var p = rt.anchoredPosition;
                p.y = y;
                rt.anchoredPosition = p;
            }

            public void SetBarFill(float ratio)
            {
                if (_bar != null) _bar.fillAmount = ratio;
            }

            // Show whole seconds remaining (rounded up so "1" shows for the final second).
            public void SetSeconds(float seconds)
            {
                if (_label == null) return;
                int s = Mathf.Max(1, Mathf.CeilToInt(seconds));
                _label.text = _letter + " " + s.ToString();
            }
        }
    }
}
