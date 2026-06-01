using StumbleClone.CameraRig;
using StumbleClone.Core;
using StumbleClone.Game;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace StumbleClone.UI
{
    /// When the player is eliminated while other racers are still alive, this takes over
    /// the camera to follow a surviving racer and shows a spectate overlay (cycle targets,
    /// Leave, Restart). The simulation keeps running until one racer remains, at which
    /// point LevelEnded fires and EndScreenUI takes over (this overlay hides itself).
    ///
    /// Created at runtime by LastStandingManager. Builds its own minimal UGUI overlay so
    /// no scene wiring is required (promote to a styled prefab later).
    public sealed class SpectateController : MonoBehaviour
    {
        private ThirdPersonCamera _camera;
        private GameObject _overlay;
        private TMP_Text _label;
        private bool _spectating;
        private int _targetIndex;

        private void OnEnable()
        {
            GameEvents.RacerEliminated += HandleRacerEliminated;
            GameEvents.LevelEnded += HandleLevelEnded;
        }

        private void OnDisable()
        {
            GameEvents.RacerEliminated -= HandleRacerEliminated;
            GameEvents.LevelEnded -= HandleLevelEnded;
        }

        private void HandleRacerEliminated(IRacer racer)
        {
            if (_spectating || racer == null || !racer.IsPlayer) return;
            // Only spectate if there's still a contest to watch; otherwise the level ends.
            if (RacerRegistry.AliveCount >= 2) EnterSpectate();
        }

        private void HandleLevelEnded(IRacer winner)
        {
            _spectating = false;
            if (_overlay != null) _overlay.SetActive(false);
        }

        private void EnterSpectate()
        {
            _spectating = true;
            _camera = FindAnyObjectByType<ThirdPersonCamera>();

            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;

            if (_overlay == null) BuildOverlay();
            _overlay.SetActive(true);

            _targetIndex = 0;
            FocusLivingTarget(0);
        }

        private void Update()
        {
            if (!_spectating) return;

            // If the racer we're watching gets eliminated, advance to the next survivor.
            IRacer current = GetLiving(_targetIndex);
            if (current == null) FocusLivingTarget(0);
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

        // End the run now. Works both from the menu flow (GameManager present) and when
        // the scene was Played directly (fall back to loading the MainMenu scene, which
        // re-creates the GameManager on the way in).
        private void OnLeave()
        {
            if (GameManager.Instance != null) GameManager.Instance.ReturnToMenu();
            else SceneManager.LoadScene("MainMenu");
        }

        private void OnRestart()
        {
            if (GameManager.Instance != null)
                GameManager.Instance.LoadLevel(GameManager.Instance.currentMode);
            else
                SceneManager.LoadScene(SceneManager.GetActiveScene().name);
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

            // Top banner label.
            _label = CreateLabel(_overlay.transform, "SPECTATING", new Vector2(0.5f, 1f),
                new Vector2(0f, -70f), new Vector2(900f, 90f), 48);

            // Bottom button row.
            var row = new GameObject("Buttons", typeof(RectTransform));
            var rt = (RectTransform)row.transform;
            rt.SetParent(_overlay.transform, false);
            rt.anchorMin = new Vector2(0.5f, 0f);
            rt.anchorMax = new Vector2(0.5f, 0f);
            rt.pivot = new Vector2(0.5f, 0f);
            rt.anchoredPosition = new Vector2(0f, 60f);
            rt.sizeDelta = new Vector2(920f, 90f);
            var layout = row.AddComponent<HorizontalLayoutGroup>();
            layout.spacing = 20f;
            layout.childAlignment = TextAnchor.MiddleCenter;
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = false;

            CreateButton(rt, "< Prev", OnPrev, new Color(0.25f, 0.25f, 0.3f));
            CreateButton(rt, "Next >", OnNext, new Color(0.25f, 0.25f, 0.3f));
            CreateButton(rt, "Restart", OnRestart, new Color(0.2f, 0.5f, 0.25f));
            CreateButton(rt, "End Run", OnLeave, new Color(0.55f, 0.2f, 0.2f));
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

        private void CreateButton(Transform parent, string label, UnityEngine.Events.UnityAction onClick, Color color)
        {
            var go = new GameObject(label, typeof(RectTransform));
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            rt.sizeDelta = new Vector2(200f, 70f);

            var img = go.AddComponent<Image>();
            img.color = color;

            var btn = go.AddComponent<Button>();
            btn.onClick.AddListener(onClick);

            var le = go.AddComponent<LayoutElement>();
            le.preferredWidth = 200f;
            le.preferredHeight = 70f;

            CreateLabel(go.transform, label, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(200f, 70f), 30);
        }
    }
}
