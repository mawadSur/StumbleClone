using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.InputSystem.OnScreen;
using TMPro;

namespace StumbleClone.UI.Mobile
{
    /// Self-instantiating on-screen touch overlay (virtual joystick + jump /
    /// push / pause buttons) for mobile and touch-capable web builds.
    ///
    /// The controls emulate a gamepad: the joystick feeds <c>&lt;Gamepad&gt;/leftStick</c>,
    /// jump feeds <c>buttonSouth</c>, push feeds <c>buttonWest</c>, pause feeds
    /// <c>start</c>. Those paths already exist in PlayerInputActions, so no
    /// gameplay code or input-action asset needs to change — knockback masking,
    /// WasPressedThisFrame, etc. all keep working through the normal Input System
    /// path.
    ///
    /// Lifecycle: a single persistent instance is created after the first scene
    /// loads (only on touch platforms). The overlay is shown during gameplay
    /// scenes (<c>Level_*</c>) and hidden in menus and while the game is paused
    /// (timeScale == 0), so the pause menu stays clickable.
    public sealed class MobileControls : MonoBehaviour
    {
        private const string GameplayScenePrefix = "Level_";

        /// Force the overlay on in the editor for layout testing without a device.
        public static bool ForceShow;

        private static MobileControls _instance;

        private Canvas _canvas;
        private GameObject _root;
        private bool _built;
        private bool _visibleState;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            if (_instance != null || !ShouldEverShow()) return;
            var go = new GameObject("MobileControls");
            DontDestroyOnLoad(go);
            _instance = go.AddComponent<MobileControls>();
        }

        private static bool ShouldEverShow()
        {
#if UNITY_ANDROID || UNITY_IOS
            return true;
#else
            // WebGL: only on an actual phone/tablet browser, never on desktop. In WebGL
            // Application.isMobilePlatform reads the browser user-agent (false on desktop, true
            // on mobile). We deliberately DON'T test Touchscreen.current — touch-capable laptops
            // and some desktop browsers report a touchscreen, which wrongly showed the overlay
            // (and clipped it) on desktop. ForceShow still lets the editor preview the layout.
            return ForceShow || Application.isMobilePlatform;
#endif
        }

        private void Awake()
        {
            EnsureEventSystem();
            Build();
            ApplyVisibility();
        }

        private void LateUpdate() => ApplyVisibility();

        private void ApplyVisibility()
        {
            var scene = SceneManager.GetActiveScene().name;
            bool inGameplay = scene != null && scene.StartsWith(GameplayScenePrefix);
            bool show = inGameplay && Time.timeScale > 0f;
            if (show == _visibleState) return;
            _visibleState = show;
            if (_root != null) _root.SetActive(show);
        }

        // --- Construction --------------------------------------------------

        private void Build()
        {
            if (_built) return;
            _built = true;

            // Root is created inactive so the On-Screen controls register their
            // (code-assigned) paths in OnEnable only after the paths are set.
            _root = new GameObject("MobileControlsCanvas");
            _root.transform.SetParent(transform, false);
            _root.SetActive(false);

            _canvas = _root.AddComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _canvas.sortingOrder = 50; // above HUD, below pause/end overlays
            var scaler = _root.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;
            _root.AddComponent<GraphicRaycaster>();

            BuildJoystick(_root.transform);
            // Right-half drag-to-look pad (camera). Created BEFORE the action buttons so the
            // buttons sit on top and intercept their own taps; drags elsewhere rotate the camera.
            BuildLookPad(_root.transform);
            BuildButton(_root.transform, "JumpButton", "JUMP", "<Gamepad>/buttonSouth",
                anchor: new Vector2(1f, 0f), anchoredPos: new Vector2(-220f, 200f),
                size: 230f, color: new Color(0.20f, 0.65f, 0.95f, 0.55f));
            BuildButton(_root.transform, "PushButton", "PUSH", "<Gamepad>/buttonWest",
                anchor: new Vector2(1f, 0f), anchoredPos: new Vector2(-470f, 330f),
                size: 180f, color: new Color(0.95f, 0.55f, 0.20f, 0.55f));
            BuildButton(_root.transform, "PauseButton", "II", "<Gamepad>/start",
                anchor: new Vector2(1f, 1f), anchoredPos: new Vector2(-90f, -90f),
                size: 110f, color: new Color(0.1f, 0.1f, 0.1f, 0.45f));
        }

        // A near-invisible Image covering the right half of the screen. The OnScreenStick lives
        // on it with a DYNAMIC ORIGIN, so a touch anywhere in the area becomes the look origin and
        // dragging steers the third-person camera (feeds <Gamepad>/rightStick -> the Look action,
        // which ThirdPersonCamera consumes). It's added first, so the JUMP/PUSH/PAUSE buttons
        // (added afterwards) render on top and swallow their own touches.
        private void BuildLookPad(Transform parent)
        {
            var go = new GameObject("LookPad", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            rt.anchorMin = new Vector2(0.5f, 0f); // right half of the screen
            rt.anchorMax = new Vector2(1f, 1f);
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;

            var img = go.GetComponent<Image>();
            img.color = new Color(0f, 0f, 0f, 0.001f); // effectively invisible, still raycastable
            img.raycastTarget = true;

            var stick = go.AddComponent<TouchOnScreenStick>();
            stick.movementRange = 120f; // ~120px drag = full look speed
            stick.SetBehaviour(OnScreenStick.Behaviour.ExactPositionWithDynamicOrigin);
            stick.SetControlPath("<Gamepad>/rightStick");
        }

        private void BuildJoystick(Transform parent)
        {
            var bgSprite = BuiltinSprite("UI/Skin/Knob.psd");

            // Background ring (non-interactive — does not eat touches).
            var bg = CreateImage(parent, "JoystickBackground", bgSprite,
                new Color(0f, 0f, 0f, 0.35f), 320f);
            var bgRt = (RectTransform)bg.transform;
            bgRt.anchorMin = bgRt.anchorMax = Vector2.zero; // bottom-left
            bgRt.pivot = new Vector2(0.5f, 0.5f);
            bgRt.anchoredPosition = new Vector2(260f, 240f);
            bg.raycastTarget = false;

            // Knob (interactive — carries the OnScreenStick).
            var knob = CreateImage(bgRt, "JoystickKnob", bgSprite,
                new Color(1f, 1f, 1f, 0.75f), 150f);
            var knobRt = (RectTransform)knob.transform;
            knobRt.anchorMin = knobRt.anchorMax = new Vector2(0.5f, 0.5f);
            knobRt.pivot = new Vector2(0.5f, 0.5f);
            knobRt.anchoredPosition = Vector2.zero;
            knob.raycastTarget = true;

            var stick = knob.gameObject.AddComponent<TouchOnScreenStick>();
            stick.movementRange = 95f;
            stick.SetControlPath("<Gamepad>/leftStick");
        }

        private void BuildButton(Transform parent, string name, string label, string controlPath,
            Vector2 anchor, Vector2 anchoredPos, float size, Color color)
        {
            var img = CreateImage(parent, name, BuiltinSprite("UI/Skin/Knob.psd"), color, size);
            var rt = (RectTransform)img.transform;
            rt.anchorMin = rt.anchorMax = anchor;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = anchoredPos;
            img.raycastTarget = true;

            var btn = img.gameObject.AddComponent<TouchOnScreenButton>();
            btn.SetControlPath(controlPath);

            var textGo = new GameObject("Label", typeof(RectTransform));
            textGo.transform.SetParent(rt, false);
            var trt = (RectTransform)textGo.transform;
            trt.anchorMin = Vector2.zero;
            trt.anchorMax = Vector2.one;
            trt.offsetMin = trt.offsetMax = Vector2.zero;
            var tmp = textGo.AddComponent<TextMeshProUGUI>();
            tmp.text = label;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.fontSize = size * 0.28f;
            tmp.fontStyle = FontStyles.Bold;
            tmp.color = new Color(1f, 1f, 1f, 0.9f);
            tmp.raycastTarget = false;
        }

        // --- Helpers -------------------------------------------------------

        private static Image CreateImage(Transform parent, string name, Sprite sprite, Color color, float size)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            rt.sizeDelta = new Vector2(size, size);
            var img = go.GetComponent<Image>();
            img.sprite = sprite;
            img.color = color;
            return img;
        }

        private static Sprite BuiltinSprite(string path)
        {
            // Returns null gracefully if the builtin resource is unavailable;
            // the Image then renders as a plain coloured quad.
            return Resources.GetBuiltinResource<Sprite>(path);
        }

        private static void EnsureEventSystem()
        {
            if (FindFirstObjectByType<EventSystem>() != null) return;
            var go = new GameObject("EventSystem");
            DontDestroyOnLoad(go);
            go.AddComponent<EventSystem>();
            go.AddComponent<InputSystemUIInputModule>();
        }
    }
}
