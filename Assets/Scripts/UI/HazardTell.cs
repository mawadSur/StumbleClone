using StumbleClone.Audio;
using StumbleClone.Core;
using StumbleClone.Game;
using StumbleClone.Obstacles;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace StumbleClone.UI
{
    /// Self-bootstrapping "tell" for incoming hazard waves. Subscribes to
    /// <see cref="GameEvents.WaveTelegraphed"/> and, on each telegraph, plays a short warning cue
    /// and flashes a glowing amber→red bar at the SCREEN EDGE matching the wave's rim octant, so
    /// players get a readable directional warning before hazards arrive.
    ///
    /// No scene wiring: it bootstraps on "Level" scenes via the same
    /// <see cref="RuntimeInitializeOnLoadMethod"/> + EnsureForScene pattern as SceneAtmosphere.
    /// A single full-screen overlay + one indicator bar are built once and reused (pooled) — no
    /// per-event canvas allocation. Rapid telegraphs simply restart the flash on the new edge.
    public sealed class HazardTell : MonoBehaviour
    {
        private static HazardTell _instance;

        // ---- Tuning --------------------------------------------------------
        private const float FlashDuration = 0.8f;  // total on-screen time per telegraph
        private const float SlideDistance = 90f;    // px the bar slides in from off-edge (full motion)
        private const int   SortingOrder  = 5000;   // above gameplay HUD, below modal popups
        private const int   BarThickness  = 120;     // px short-axis of the edge bar (ref-res space)
        private const int   BarLength     = 720;     // px long-axis of the edge bar (ref-res space)

        // ---- Pooled UI (built once, reused every event) --------------------
        private RectTransform _bar;     // the glowing indicator, re-anchored per direction
        private Image _barImage;

        // ---- Active flash state --------------------------------------------
        private bool _flashing;
        private float _elapsed;
        private Vector2 _restPos;       // settled anchored position (edge-aligned)
        private Vector2 _fromPos;       // off-edge start position for the slide-in
        private Color _flashColor;      // amber→red tint for the current direction's hazard

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            SceneManager.sceneLoaded += OnSceneLoaded;
            EnsureForScene(SceneManager.GetActiveScene());
        }

        private static void OnSceneLoaded(Scene s, LoadSceneMode m) => EnsureForScene(s);

        private static void EnsureForScene(Scene scene)
        {
            if (!scene.IsValid() || !scene.name.StartsWith("Level")) return;
            if (_instance != null) return;
            _instance = new GameObject("HazardTell").AddComponent<HazardTell>();
        }

        private void Awake()
        {
            if (_instance != null && _instance != this) { Destroy(gameObject); return; }
            _instance = this;
            BuildOverlay();
            GameEvents.WaveTelegraphed += OnWaveTelegraphed;
        }

        private void OnDestroy()
        {
            GameEvents.WaveTelegraphed -= OnWaveTelegraphed;
            if (_instance == this) _instance = null;
        }

        // ---- Event handling ------------------------------------------------

        private void OnWaveTelegraphed(string patternName, SpawnDirection dir)
        {
            // Warning cue — reuse an existing Sfx (Start) at a slightly higher pitch so it reads as
            // an alert rather than a level-start chime.
            AudioManager.Play(Sfx.Start, volumeScale: 0.85f, pitch: 1.35f);

            PlaceBar(dir);
            _flashing = true;
            _elapsed = 0f; // restart the flash even if one is mid-fade (honor rapid telegraphs)
        }

        // ---- Per-frame flash animation -------------------------------------

        private void Update()
        {
            if (!_flashing) return;

            _elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(_elapsed / FlashDuration);

            // Brightness envelope: a fast punch-in then a smooth fade to nothing.
            float alpha = t < 0.18f
                ? Mathf.Clamp01(t / 0.18f)          // ~0.14s rise
                : 1f - Mathf.SmoothStep(0f, 1f, (t - 0.18f) / 0.82f);

            var c = _flashColor;
            c.a *= alpha;
            _barImage.color = c;

            if (!SettingsStore.ReducedMotion)
            {
                // Slide the bar in from just off the edge, easing to rest as it brightens.
                float slide = 1f - (1f - Mathf.Min(t / 0.25f, 1f));
                _bar.anchoredPosition = Vector2.Lerp(_fromPos, _restPos, Mathf.SmoothStep(0f, 1f, slide));
            }
            else
            {
                _bar.anchoredPosition = _restPos; // fade only — no sliding/scaling
            }

            if (t >= 1f)
            {
                _flashing = false;
                var hidden = _barImage.color; hidden.a = 0f;
                _barImage.color = hidden;
            }
        }

        // ---- Direction → screen-edge mapping -------------------------------

        /// Maps a rim <see cref="SpawnDirection"/> octant to the matching screen edge/corner and
        /// orients the bar along that edge.
        ///
        /// The enum is clockwise from North with N=+Z (into the screen), E=+X (right), S=-Z
        /// (toward camera), W=-X (left). Rendered top-down (minimap convention): N→TOP edge,
        /// E→RIGHT edge, S→BOTTOM edge, W→LEFT edge, and the diagonals to the matching corners.
        /// Anchors use RectTransform space where (0,0)=bottom-left and (1,1)=top-right, so +Y is up.
        private void PlaceBar(SpawnDirection dir)
        {
            Vector2 anchor;   // edge/corner anchor in [0,1]^2
            float rotationZ;  // bar long-axis orientation (0 = horizontal)
            Vector2 inward;   // unit screen direction the bar slides in FROM the edge

            switch (dir)
            {
                case SpawnDirection.N:  anchor = new Vector2(0.5f, 1f); rotationZ = 0f;   inward = Vector2.down;  break;
                case SpawnDirection.NE: anchor = new Vector2(1f,   1f); rotationZ = -45f; inward = new Vector2(-1f, -1f).normalized; break;
                case SpawnDirection.E:  anchor = new Vector2(1f,   0.5f); rotationZ = 90f;  inward = Vector2.left;  break;
                case SpawnDirection.SE: anchor = new Vector2(1f,   0f); rotationZ = 45f;  inward = new Vector2(-1f,  1f).normalized; break;
                case SpawnDirection.S:  anchor = new Vector2(0.5f, 0f); rotationZ = 0f;   inward = Vector2.up;    break;
                case SpawnDirection.SW: anchor = new Vector2(0f,   0f); rotationZ = -45f; inward = new Vector2( 1f,  1f).normalized; break;
                case SpawnDirection.W:  anchor = new Vector2(0f,   0.5f); rotationZ = 90f;  inward = Vector2.right; break;
                case SpawnDirection.NW: anchor = new Vector2(0f,   1f); rotationZ = 45f;  inward = new Vector2( 1f, -1f).normalized; break;
                default: anchor = new Vector2(0.5f, 1f); rotationZ = 0f; inward = Vector2.down; break;
            }

            _bar.anchorMin = _bar.anchorMax = _bar.pivot = anchor;
            _bar.localRotation = Quaternion.Euler(0f, 0f, rotationZ);

            // Rest position: sit flush against the edge with no offset (anchored to the edge itself).
            _restPos = Vector2.zero;
            _fromPos = _restPos + inward * -SlideDistance; // start further OUT, slide inward

            _flashColor = HazardColor(dir);

            // Make the bar visible immediately at its start pose so the first frame is correct.
            _bar.anchoredPosition = SettingsStore.ReducedMotion ? _restPos : _fromPos;
        }

        /// Amber→red warning tint. Diagonals lean a touch hotter (more red) than the cardinals to
        /// add subtle variety while staying inside the amber/red "danger" band.
        private static Color HazardColor(SpawnDirection dir)
        {
            bool diagonal = ((int)dir & 1) == 1; // odd octants (NE, SE, SW, NW)
            // Accent is gold/amber; Danger is red. Lerp gives the readable warning gradient.
            return Color.Lerp(UITheme.Accent, UITheme.Danger, diagonal ? 0.6f : 0.35f);
        }

        // ---- Overlay construction (once) -----------------------------------

        private void BuildOverlay()
        {
            var overlay = RuntimeUI.Overlay("HazardTellOverlay", SortingOrder);
            DontDestroyOnLoad(overlay); // survive level reloads alongside this controller
            // Purely decorative — never intercept gameplay input.
            var raycaster = overlay.GetComponent<GraphicRaycaster>();
            if (raycaster != null) raycaster.enabled = false;

            var barGo = new GameObject("HazardBar", typeof(RectTransform));
            _bar = (RectTransform)barGo.transform;
            _bar.SetParent(overlay.transform, false);
            _bar.sizeDelta = new Vector2(BarLength, BarThickness);

            _barImage = barGo.AddComponent<Image>();
            _barImage.sprite = UITheme.RoundedSprite(); // soft, glowing rounded bar
            _barImage.type = Image.Type.Sliced;
            _barImage.raycastTarget = false;
            var start = UITheme.Accent; start.a = 0f;
            _barImage.color = start; // hidden until the first telegraph
        }
    }
}
