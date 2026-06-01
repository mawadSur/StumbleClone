using StumbleClone.CameraRig;
using StumbleClone.Core;
using StumbleClone.Game;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace StumbleClone.UI
{
    /// When the human player is eliminated, this takes over the camera to follow a surviving
    /// racer and shows a "You're out — Play Again?" popup. The player gets one life: there is no
    /// respawn (see KillZone / PlayerController). The simulation keeps running for the bots; when
    /// one racer remains, LevelEnded fires and EndScreenUI takes over (this overlay hides itself).
    ///
    /// Self-bootstrapping: one instance is created automatically in every gameplay scene, so no
    /// per-scene wiring is required and the flow works in all three modes (Race / Survival /
    /// LastStanding) whether the level is entered via the menu or Played directly in the editor.
    public sealed class SpectateController : MonoBehaviour
    {
        private static SpectateController _instance;

        private ThirdPersonCamera _camera;
        private GameObject _overlay;
        private TMP_Text _label;
        private GameObject _prevNextRow;
        private bool _dead;
        private int _targetIndex;

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
            if (FindAnyObjectByType<SpectateController>() != null) return;
            new GameObject("SpectateController").AddComponent<SpectateController>();
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

        private void OnEnable()
        {
            GameEvents.RacerEliminated += HandleRacerEliminated;
            GameEvents.LevelEnded += HandleLevelEnded;
        }

        private void OnDisable()
        {
            GameEvents.RacerEliminated -= HandleRacerEliminated;
            GameEvents.LevelEnded -= HandleLevelEnded;
            if (_instance == this) _instance = null;
        }

        private void HandleRacerEliminated(IRacer racer)
        {
            if (_dead || racer == null || !racer.IsPlayer) return;
            EnterDeadState();
        }

        private void HandleLevelEnded(IRacer winner)
        {
            // The level resolved — EndScreenUI takes over. Hide our popup.
            _dead = false;
            if (_overlay != null) _overlay.SetActive(false);
        }

        private void EnterDeadState()
        {
            _dead = true;
            _camera = FindAnyObjectByType<ThirdPersonCamera>();

            // The popup needs a clickable cursor.
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;

            if (_overlay == null) BuildOverlay();
            _overlay.SetActive(true);

            _targetIndex = 0;
            RefreshSpectate();
        }

        private void Update()
        {
            if (!_dead) return;

            // If the racer we're watching gets eliminated, advance to the next survivor.
            IRacer current = GetLiving(_targetIndex);
            if (current == null) RefreshSpectate();
        }

        // Point the camera at a living racer (if any) and update the banner.
        private void RefreshSpectate()
        {
            bool anyAlive = CountLiving() > 0;
            if (_prevNextRow != null) _prevNextRow.SetActive(anyAlive);

            if (!anyAlive)
            {
                if (_label != null) _label.text = "";
                return;
            }
            FocusLivingTarget(0);
        }

        private void FocusLivingTarget(int direction)
        {
            int alive = CountLiving();
            if (alive <= 0) return;

            if (direction != 0)
                _targetIndex += direction;

            // Wrap and skip to a living racer.
            for (int guard = 0; guard < 64; guard++)
            {
                IRacer r = GetLiving(_targetIndex);
                if (r != null)
                {
                    if (_camera != null) _camera.SetTarget(r.Transform);
                    if (_label != null) _label.text = "SPECTATING: " + r.DisplayName;
                    return;
                }
                _targetIndex++;
            }
        }

        private int CountLiving()
        {
            var all = RacerRegistry.All;
            int n = 0;
            for (int i = 0; i < all.Count; i++)
                if (all[i] != null && all[i].IsAlive) n++;
            return n;
        }

        // Returns the i-th living racer (wrapping), or null if none.
        private IRacer GetLiving(int i)
        {
            var all = RacerRegistry.All;
            int alive = CountLiving();
            if (alive <= 0) return null;

            int idx = ((i % alive) + alive) % alive;
            int seen = 0;
            for (int k = 0; k < all.Count; k++)
            {
                if (all[k] == null || !all[k].IsAlive) continue;
                if (seen == idx) return all[k];
                seen++;
            }
            return null;
        }

        // ---- button handlers ----------------------------------------------------

        private void OnNext() => FocusLivingTarget(1);
        private void OnPrev() => FocusLivingTarget(-1);

        // Play Again — restart the current level. Works both from the menu flow (GameManager
        // present) and when the scene was Played directly (reload the active scene).
        private void OnPlayAgain()
        {
            if (GameManager.Instance != null)
                GameManager.Instance.LoadLevel(GameManager.Instance.currentMode);
            else
                SceneManager.LoadScene(SceneManager.GetActiveScene().name);
        }

        // Main Menu — leave the run.
        private void OnLeave()
        {
            if (GameManager.Instance != null) GameManager.Instance.ReturnToMenu();
            else SceneManager.LoadScene("MainMenu");
        }

        // ---- runtime UGUI overlay ------------------------------------------------

        private void BuildOverlay()
        {
            _overlay = new GameObject("SpectateOverlay");
            var canvas = _overlay.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 50;
            var scaler = _overlay.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            _overlay.AddComponent<GraphicRaycaster>();

            // Top banner label + spectate cycle buttons.
            _label = CreateLabel(_overlay.transform, "SPECTATING", new Vector2(0.5f, 1f),
                new Vector2(0f, -70f), new Vector2(900f, 90f), 48);

            _prevNextRow = new GameObject("CycleButtons", typeof(RectTransform));
            var crt = (RectTransform)_prevNextRow.transform;
            crt.SetParent(_overlay.transform, false);
            crt.anchorMin = new Vector2(0.5f, 1f);
            crt.anchorMax = new Vector2(0.5f, 1f);
            crt.pivot = new Vector2(0.5f, 1f);
            crt.anchoredPosition = new Vector2(0f, -170f);
            crt.sizeDelta = new Vector2(440f, 70f);
            var clayout = _prevNextRow.AddComponent<HorizontalLayoutGroup>();
            clayout.spacing = 20f;
            clayout.childAlignment = TextAnchor.MiddleCenter;
            clayout.childForceExpandWidth = false;
            clayout.childForceExpandHeight = false;
            CreateButton(crt, "< Prev", OnPrev, new Color(0.25f, 0.25f, 0.3f));
            CreateButton(crt, "Next >", OnNext, new Color(0.25f, 0.25f, 0.3f));

            // Center "Play Again?" popup. Compact so the spectated action stays visible behind it.
            BuildPlayAgainPopup(_overlay.transform);
        }

        private void BuildPlayAgainPopup(Transform parent)
        {
            var panel = new GameObject("PlayAgainPopup", typeof(RectTransform));
            var prt = (RectTransform)panel.transform;
            prt.SetParent(parent, false);
            prt.anchorMin = new Vector2(0.5f, 0.5f);
            prt.anchorMax = new Vector2(0.5f, 0.5f);
            prt.pivot = new Vector2(0.5f, 0.5f);
            prt.anchoredPosition = Vector2.zero;
            prt.sizeDelta = new Vector2(640f, 380f);
            var bg = panel.AddComponent<Image>();
            bg.color = new Color(0.06f, 0.07f, 0.12f, 0.92f);

            CreateLabel(prt, "YOU'RE OUT!", new Vector2(0.5f, 1f),
                new Vector2(0f, -70f), new Vector2(600f, 90f), 64);
            CreateLabel(prt, "No respawns this round. Play again?", new Vector2(0.5f, 1f),
                new Vector2(0f, -160f), new Vector2(600f, 60f), 32);

            var row = new GameObject("PopupButtons", typeof(RectTransform));
            var rrt = (RectTransform)row.transform;
            rrt.SetParent(prt, false);
            rrt.anchorMin = new Vector2(0.5f, 0f);
            rrt.anchorMax = new Vector2(0.5f, 0f);
            rrt.pivot = new Vector2(0.5f, 0f);
            rrt.anchoredPosition = new Vector2(0f, 50f);
            rrt.sizeDelta = new Vector2(560f, 90f);
            var layout = row.AddComponent<HorizontalLayoutGroup>();
            layout.spacing = 24f;
            layout.childAlignment = TextAnchor.MiddleCenter;
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = false;

            CreateButton(rrt, "PLAY AGAIN", OnPlayAgain, new Color(0.2f, 0.55f, 0.3f), 250f);
            CreateButton(rrt, "MAIN MENU", OnLeave, new Color(0.55f, 0.2f, 0.2f), 250f);
        }

        private static TMP_Text CreateLabel(Transform parent, string text, Vector2 anchor,
            Vector2 anchoredPos, Vector2 size, int fontSize)
        {
            var go = new GameObject("Label", typeof(RectTransform));
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            rt.anchorMin = anchor; rt.anchorMax = anchor; rt.pivot = anchor;
            rt.anchoredPosition = anchoredPos;
            rt.sizeDelta = size;

            var tmp = go.AddComponent<TextMeshProUGUI>();
            tmp.text = text;
            tmp.fontSize = fontSize;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.color = Color.white;
            tmp.fontStyle = FontStyles.Bold;
            return tmp;
        }

        private void CreateButton(Transform parent, string label, UnityEngine.Events.UnityAction onClick,
            Color color, float width = 200f)
        {
            var go = new GameObject(label, typeof(RectTransform));
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            rt.sizeDelta = new Vector2(width, 70f);

            var img = go.AddComponent<Image>();
            img.color = color;

            var btn = go.AddComponent<Button>();
            btn.onClick.AddListener(onClick);

            var le = go.AddComponent<LayoutElement>();
            le.preferredWidth = width;
            le.preferredHeight = 70f;

            CreateLabel(go.transform, label, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(width, 70f), 30);
        }
    }
}
