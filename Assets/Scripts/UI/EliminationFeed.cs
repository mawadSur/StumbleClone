using StumbleClone.Core;
using StumbleClone.Game;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace StumbleClone.UI
{
    /// Lightweight top-center "kill feed" so eliminations actually read on screen. Today the only
    /// elimination feedback is the "Alive: N" counter and the bots carry flavorful names nobody
    /// ever sees — this surfaces both: a brief "&lt;Name&gt; eliminated" toast plus an "N left"
    /// count whenever <see cref="GameEvents.RacerEliminated"/> fires. The human player's own
    /// knockout gets a stronger red "You were knocked out!" toast.
    ///
    /// Self-bootstrapping (zero scene wiring) in gameplay scenes only (scene name starts with
    /// "Level"), mirroring <see cref="SpectateController"/>. Toasts stack newest-on-top, cap at a
    /// few visible, and fade out after a short hold. Allocation-light: a fixed pool of reusable
    /// rows, no per-frame allocations, manual fade on unscaled time.
    ///
    /// Respects the Reduced-Motion accessibility setting: when SettingsStore.ReducedMotion is on,
    /// rows appear at full alpha immediately (no slide/scale-in) and the fade-out collapses to a
    /// quick cross-fade rather than a long drift.
    public sealed class EliminationFeed : MonoBehaviour
    {
        // ---- tuning -------------------------------------------------------------
        private const int MaxVisible = 4;        // cap on simultaneously shown toasts
        private const float HoldSeconds = 2f;    // full-opacity dwell before fading
        private const float FadeSeconds = 0.45f; // fade-out tail (standard motion)
        private const float ReducedFade = 0.18f; // fade-out tail (reduced motion)
        private const float RiseSeconds = 0.18f; // slide-up + fade-in (standard motion)
        private const float RowHeight = 64f;     // vertical pitch between stacked rows
        private const float RowWidth = 760f;
        private const float TopMargin = 120f;    // below the survival/race HUD header
        private const int FontSize = 30;

        private static EliminationFeed _instance;

        private GameObject _overlay;
        private RectTransform _container;
        private readonly Toast[] _toasts = new Toast[MaxVisible];

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
            if (FindAnyObjectByType<EliminationFeed>() != null) return;
            new GameObject("EliminationFeed").AddComponent<EliminationFeed>();
        }

        private void Awake()
        {
            // De-dupe: a stale instance may survive a scene transition edge case.
            if (_instance != null && _instance != this)
            {
                Destroy(gameObject);
                return;
            }
            _instance = this;
        }

        private void OnEnable()
        {
            // Re-subscribe safely across scenes: unsub before sub so a re-enable never double-fires.
            GameEvents.RacerEliminated -= HandleRacerEliminated;
            GameEvents.RacerEliminated += HandleRacerEliminated;
        }

        private void OnDisable()
        {
            GameEvents.RacerEliminated -= HandleRacerEliminated;
            if (_instance == this) _instance = null;
        }

        // ---- event handling -----------------------------------------------------

        private void HandleRacerEliminated(IRacer racer)
        {
            // Guard against null / destroyed racers (Unity-null aware via UnityEngine.Object check).
            if (racer == null) return;

            bool isPlayer = false;
            string name = null;
            try
            {
                isPlayer = racer.IsPlayer;
                name = racer.DisplayName;
            }
            catch (MissingReferenceException)
            {
                // Underlying object was destroyed mid-dispatch — skip silently.
                return;
            }
            if (string.IsNullOrEmpty(name)) name = "Racer";

            string message = isPlayer ? "You were knocked out!" : name + " eliminated";

            // "N left" count from the registry (cheap O(n) walk; only on elimination, not per-frame).
            int alive = RacerRegistry.AliveCount;
            if (alive < 0) alive = 0;
            string count = alive == 1 ? "1 left" : alive + " left";

            ShowToast(message, count, isPlayer);
        }

        // ---- toast stack --------------------------------------------------------

        private void ShowToast(string message, string count, bool emphatic)
        {
            if (_overlay == null) BuildOverlay();

            // Newest on top: shift everyone down one slot, evicting the oldest (last) toast.
            Toast last = _toasts[MaxVisible - 1];
            for (int i = MaxVisible - 1; i > 0; i--)
                _toasts[i] = _toasts[i - 1];

            // Reuse the evicted row if present, else build a fresh one for slot 0.
            Toast slot = last ?? BuildToast();
            _toasts[0] = slot;

            slot.Set(message, count, emphatic);

            // Re-stack: position every live row by its slot index (top = slot 0).
            for (int i = 0; i < MaxVisible; i++)
            {
                Toast t = _toasts[i];
                if (t == null) continue;
                t.SlotIndex = i;
                t.Reposition(RowHeight);
            }
        }

        private void Update()
        {
            float dt = Time.unscaledDeltaTime;
            bool reduced = SettingsStore.ReducedMotion;
            for (int i = 0; i < MaxVisible; i++)
            {
                Toast t = _toasts[i];
                if (t == null || !t.Active) continue;
                t.Tick(dt, reduced, RowHeight);
            }
        }

        // ---- runtime UGUI -------------------------------------------------------

        private void BuildOverlay()
        {
            // SafeArea-aware overlay (sits above HUD ~10, below spectate popup at 50).
            Transform safe = RuntimeUI.Overlay("EliminationFeedOverlay", 30).transform;
            _overlay = safe.gameObject;

            // Non-interactive: the feed must never eat clicks meant for HUD/popups.
            var raycaster = _overlay.GetComponent<GraphicRaycaster>();
            if (raycaster != null) raycaster.enabled = false;

            // A top-center container the toasts anchor into (each row offsets downward by slot).
            var go = new GameObject("FeedContainer", typeof(RectTransform));
            _container = (RectTransform)go.transform;
            _container.SetParent(safe, false);
            _container.anchorMin = new Vector2(0.5f, 1f);
            _container.anchorMax = new Vector2(0.5f, 1f);
            _container.pivot = new Vector2(0.5f, 1f);
            _container.anchoredPosition = new Vector2(0f, -TopMargin);
            _container.sizeDelta = new Vector2(RowWidth, RowHeight * MaxVisible);
        }

        private Toast BuildToast()
        {
            var go = new GameObject("Toast", typeof(RectTransform));
            var rt = (RectTransform)go.transform;
            rt.SetParent(_container, false);
            rt.anchorMin = new Vector2(0.5f, 1f);
            rt.anchorMax = new Vector2(0.5f, 1f);
            rt.pivot = new Vector2(0.5f, 1f);
            rt.sizeDelta = new Vector2(RowWidth, RowHeight - 10f);

            var cg = go.AddComponent<CanvasGroup>();
            cg.alpha = 0f;

            var bg = go.AddComponent<Image>();
            bg.sprite = UITheme.RoundedSprite();
            bg.type = Image.Type.Sliced;

            // Two stacked labels: bold message + a muted "N left" count, both theme-fonted via Label.
            var msg = RuntimeUI.Label(rt, "", FontSize, new Vector2(0f, 0.5f),
                new Vector2(24f, 0f), new Vector2(RowWidth - 200f, RowHeight - 14f),
                TextAlignmentOptions.MidlineLeft);
            msg.fontStyle = FontStyles.Bold;

            var cnt = RuntimeUI.Label(rt, "", FontSize - 6, new Vector2(1f, 0.5f),
                new Vector2(-24f, 0f), new Vector2(160f, RowHeight - 14f),
                TextAlignmentOptions.MidlineRight);
            cnt.color = UITheme.OnSurfaceMuted;

            return new Toast(go, rt, cg, bg, msg, cnt);
        }

        /// One reusable feed row. Holds its own fade state machine so the parent only ticks it.
        /// Reference type so the pooled rows in <c>_toasts</c> mutate in place when ticked/restacked
        /// (a struct would mutate a throwaway copy and lose its state every frame).
        private sealed class Toast
        {
            public readonly GameObject Root;
            public readonly RectTransform Rt;
            public readonly CanvasGroup Group;
            public readonly Image Bg;
            public readonly TMP_Text Message;
            public readonly TMP_Text Count;

            public bool Active;
            public int SlotIndex;
            private float _age;       // seconds since shown

            // Surface tints — emphatic (player KO) reads red; normal eliminations use the panel surface.
            private static readonly Color NormalBg = new Color(
                UITheme.Surface.r, UITheme.Surface.g, UITheme.Surface.b, 0.9f);
            private static readonly Color EmphaticBg = new Color(
                UITheme.Danger.r, UITheme.Danger.g, UITheme.Danger.b, 0.95f);

            public Toast(GameObject root, RectTransform rt, CanvasGroup group, Image bg,
                TMP_Text message, TMP_Text count)
            {
                Root = root;
                Rt = rt;
                Group = group;
                Bg = bg;
                Message = message;
                Count = count;
                Active = false;
                SlotIndex = 0;
                _age = 0f;
            }

            public void Set(string message, string count, bool emphatic)
            {
                _age = 0f;
                Active = true;

                Message.text = message;
                Message.color = emphatic ? Color.white : UITheme.OnSurface;
                Count.text = count;
                Bg.color = emphatic ? EmphaticBg : NormalBg;

                // Start hidden; Tick fades/slides it in. Set alpha here so a reused row never
                // flashes its previous frame before the first Tick runs.
                Group.alpha = 0f;
                Root.SetActive(true);
            }

            // Anchor the row at its slot's vertical pitch. Called on (re)stack and as a fade-in base.
            public void Reposition(float rowHeight)
            {
                Rt.anchoredPosition = new Vector2(0f, -SlotIndex * rowHeight);
            }

            public void Tick(float dt, bool reduced, float rowHeight)
            {
                _age += dt;

                float rise = reduced ? 0f : RiseSeconds;
                float fade = reduced ? ReducedFade : FadeSeconds;
                float baseY = -SlotIndex * rowHeight;

                if (rise > 0f && _age < rise)
                {
                    // Slide up from slightly below + fade in.
                    float k = Mathf.Clamp01(_age / rise);
                    float e = 1f - Mathf.Pow(1f - k, 3f); // ease-out cubic
                    Group.alpha = e;
                    Rt.anchoredPosition = new Vector2(0f, baseY - (1f - e) * 18f);
                    return;
                }

                float fadeStart = HoldSeconds + rise;
                if (_age < fadeStart)
                {
                    Group.alpha = 1f;
                    Rt.anchoredPosition = new Vector2(0f, baseY);
                    return;
                }

                float ft = (_age - fadeStart) / fade;
                if (ft >= 1f)
                {
                    Group.alpha = 0f;
                    Active = false;
                    Root.SetActive(false); // park it; it stays pooled for reuse
                    return;
                }
                Group.alpha = 1f - ft;
                Rt.anchoredPosition = new Vector2(0f, baseY);
            }
        }
    }
}
