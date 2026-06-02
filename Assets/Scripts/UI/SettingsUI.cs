using System;
using StumbleClone.Game;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace StumbleClone.UI
{
    /// A small "SETTINGS" button in the top-right of the MainMenu that opens a themed overlay with
    /// sliders for Master / Music / SFX volume and Look Sensitivity, plus a Back button. Slider
    /// changes write straight to SettingsStore (PlayerPrefs). Self-instantiates on MainMenu load —
    /// no scene wiring/rebuild, matching LeaderboardUI / TitleScreen.
    public sealed class SettingsUI : MonoBehaviour
    {
        public static SettingsUI Instance { get; private set; }

        private const string MenuScene = "MainMenu";
        private GameObject _panel;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            SceneManager.sceneLoaded += OnSceneLoaded;
            if (SceneManager.GetActiveScene().name == MenuScene) Create();
        }

        private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (scene.name == MenuScene) Create();
        }

        private static void Create()
        {
            if (FindFirstObjectByType<SettingsUI>() != null) return;
            new GameObject("SettingsUI").AddComponent<SettingsUI>();
        }

        private void Awake() => Instance = this;
        private void OnDestroy() { if (Instance == this) Instance = null; }

        private void Start() => BuildLauncher();

        /// The corner "SETTINGS" pill that lives above the title screen and opens the panel.
        private void BuildLauncher()
        {
            var overlay = RuntimeUI.Overlay("SettingsLauncher", 120);
            RuntimeUI.Button(overlay.transform, "SETTINGS", UITheme.Neutral,
                new Vector2(0.97f, 0.95f), Vector2.zero, new Vector2(200f, 58f), Open);
        }

        /// Open (building the panel lazily on first use) and play the entrance animation.
        public void Open()
        {
            AudioManager_PlayClick();
            if (_panel == null) Build();
            _panel.SetActive(true);
            OverlayIntro.Play(_panel);
        }

        private void Close()
        {
            if (_panel != null) _panel.SetActive(false);
        }

        private void Build()
        {
            _panel = RuntimeUI.Overlay("SettingsPanel", 130);
            var bg = RuntimeUI.Panel(_panel.transform, "Bg",
                new Color(UITheme.SurfaceDeep.r, UITheme.SurfaceDeep.g, UITheme.SurfaceDeep.b, 0.97f),
                Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);

            var heading = RuntimeUI.Label(bg.transform, "SETTINGS", 72,
                new Vector2(0.5f, 0.92f), Vector2.zero, new Vector2(900f, 110f));
            heading.fontStyle = FontStyles.Bold;
            heading.color = UITheme.Gold;

            // Six rows, evenly spaced (0.10 apart) down the panel, ending clear of the Back button
            // at 0.09. Slider rows: caption + value readout + slider. Toggle rows: caption + switch.
            BuildVolumeRow(bg.transform, 0.78f, "MASTER VOLUME",
                SettingsStore.MasterVolume, v => SettingsStore.MasterVolume = v);
            BuildVolumeRow(bg.transform, 0.68f, "MUSIC VOLUME",
                SettingsStore.MusicVolume, v => SettingsStore.MusicVolume = v);
            BuildVolumeRow(bg.transform, 0.58f, "SFX VOLUME",
                SettingsStore.SfxVolume, v => SettingsStore.SfxVolume = v);
            BuildSensitivityRow(bg.transform, 0.48f, "LOOK SENSITIVITY",
                SettingsStore.LookSensitivity, v => SettingsStore.LookSensitivity = v);
            BuildToggleRow(bg.transform, 0.38f, "REDUCED MOTION",
                SettingsStore.ReducedMotion, v => SettingsStore.ReducedMotion = v);
            BuildToggleRow(bg.transform, 0.28f, "HIGH-CONTRAST HAZARDS",
                SettingsStore.HighContrastTelegraphs, v => SettingsStore.HighContrastTelegraphs = v);

            RuntimeUI.Button(bg.transform, "Back", UITheme.Neutral,
                new Vector2(0.5f, 0.09f), Vector2.zero, new Vector2(300f, 80f), Close);
        }

        // --- Rows ----------------------------------------------------------

        /// A 0..1 volume row whose readout shows a 0–100% value.
        private void BuildVolumeRow(Transform parent, float anchorY, string caption,
            float value, Action<float> onChange)
        {
            var readout = BuildRowChrome(parent, anchorY, caption, Pct(value));
            BuildSlider(parent, anchorY, 0f, 1f, value, v =>
            {
                onChange(v);
                readout.text = Pct(v);
            });
        }

        /// The look-sensitivity row (LookMin..LookMax) whose readout shows a "1.0x" multiplier.
        private void BuildSensitivityRow(Transform parent, float anchorY, string caption,
            float value, Action<float> onChange)
        {
            var readout = BuildRowChrome(parent, anchorY, caption, Mult(value));
            BuildSlider(parent, anchorY, SettingsStore.LookMin, SettingsStore.LookMax, value, v =>
            {
                onChange(v);
                readout.text = Mult(v);
            });
        }

        /// A boolean accessibility row: a left caption and a themed on/off toggle switch on the
        /// right. onChange fires (and persists) on every flip.
        private void BuildToggleRow(Transform parent, float anchorY, string caption,
            bool value, Action<bool> onChange)
        {
            var label = RuntimeUI.Label(parent, caption, 34,
                new Vector2(0.5f, anchorY), new Vector2(-260f, 0f), new Vector2(600f, 44f),
                TextAlignmentOptions.Left);
            label.color = UITheme.OnSurface;

            BuildToggle(parent, anchorY, value, onChange);
        }

        /// Caption (left) + value readout (right) for a row; returns the readout label so the
        /// slider callback can update it live.
        private TMP_Text BuildRowChrome(Transform parent, float anchorY, string caption, string readoutText)
        {
            var label = RuntimeUI.Label(parent, caption, 34,
                new Vector2(0.5f, anchorY), new Vector2(-260f, 34f), new Vector2(600f, 44f),
                TextAlignmentOptions.Left);
            label.color = UITheme.OnSurface;

            var readout = RuntimeUI.Label(parent, readoutText, 34,
                new Vector2(0.5f, anchorY), new Vector2(300f, 34f), new Vector2(160f, 44f),
                TextAlignmentOptions.Right);
            readout.color = UITheme.OnSurfaceMuted;
            return readout;
        }

        // --- Slider construction (pure code, themed) -----------------------

        /// Build a horizontal UnityEngine.UI.Slider with a themed track/fill/handle, wired to
        /// invoke onChange on every value change. Centered at (0.5, anchorY).
        private static void BuildSlider(Transform parent, float anchorY,
            float min, float max, float value, UnityEngine.Events.UnityAction<float> onChange)
        {
            const float width = 760f;
            const float trackHeight = 16f;
            const float handleSize = 40f;

            var go = new GameObject("Slider", typeof(RectTransform));
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, anchorY);
            rt.anchoredPosition = new Vector2(0f, -6f);
            rt.sizeDelta = new Vector2(width, handleSize);

            // Background track (full-width, muted).
            var bg = MakeImage(rt, "Background", UITheme.Neutral, stretch: true);
            InsetVertical((RectTransform)bg.transform, trackHeight);

            // Fill Area + Fill (primary pink, grows left->right with the value).
            var fillArea = MakeRect(rt, "Fill Area", stretch: true);
            InsetVertical(fillArea, trackHeight);
            // Leave handle-radius padding so the fill lines up under the handle at both ends.
            fillArea.offsetMin = new Vector2(handleSize * 0.5f, fillArea.offsetMin.y);
            fillArea.offsetMax = new Vector2(-handleSize * 0.5f, fillArea.offsetMax.y);
            var fill = MakeImage(fillArea, "Fill", UITheme.Primary, stretch: false);
            var fillRt = (RectTransform)fill.transform;
            fillRt.anchorMin = new Vector2(0f, 0f);
            fillRt.anchorMax = new Vector2(0f, 1f);
            fillRt.offsetMin = Vector2.zero;
            fillRt.offsetMax = Vector2.zero;
            fillRt.sizeDelta = new Vector2(handleSize, 0f);

            // Handle Slide Area + Handle (gold knob).
            var handleArea = MakeRect(rt, "Handle Slide Area", stretch: true);
            handleArea.offsetMin = new Vector2(handleSize * 0.5f, handleArea.offsetMin.y);
            handleArea.offsetMax = new Vector2(-handleSize * 0.5f, handleArea.offsetMax.y);
            var handle = MakeImage(handleArea, "Handle", UITheme.Gold, stretch: false);
            var handleRt = (RectTransform)handle.transform;
            handleRt.sizeDelta = new Vector2(handleSize, handleSize);

            var slider = go.AddComponent<Slider>();
            slider.fillRect = fillRt;
            slider.handleRect = handleRt;
            slider.targetGraphic = handle;
            slider.direction = Slider.Direction.LeftToRight;
            slider.minValue = min;
            slider.maxValue = max;
            slider.wholeNumbers = false;
            slider.SetValueWithoutNotify(value);
            slider.onValueChanged.AddListener(onChange);
        }

        // --- Toggle construction (pure code, themed pill switch) -----------

        /// Build a UnityEngine.UI.Toggle styled as a rounded pill switch (track + sliding knob),
        /// right-aligned in the row at anchorY. The track tints green when on / neutral when off,
        /// and the gold knob slides to the lit side; onChange fires (persisted) on each flip.
        private static void BuildToggle(Transform parent, float anchorY, bool value,
            Action<bool> onChange)
        {
            const float trackWidth = 120f;
            const float trackHeight = 56f;
            const float knobSize = 44f;
            const float knobInset = (trackHeight - knobSize) * 0.5f;
            float knobTravel = trackWidth - knobSize - knobInset * 2f;

            var go = new GameObject("Toggle", typeof(RectTransform));
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            // Right-aligned in the row, matching where the slider readout sits.
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, anchorY);
            rt.anchoredPosition = new Vector2(320f, 0f);
            rt.sizeDelta = new Vector2(trackWidth, trackHeight);

            // Track (the rounded pill background that recolours with state).
            var track = MakeImage(rt, "Track", UITheme.Neutral, stretch: true);

            // Knob (gold sliding circle); anchored left so we can lerp its X by state.
            var knob = MakeImage(rt, "Knob", UITheme.Gold, stretch: false);
            var knobRt = (RectTransform)knob.transform;
            knobRt.anchorMin = knobRt.anchorMax = knobRt.pivot = new Vector2(0f, 0.5f);
            knobRt.sizeDelta = new Vector2(knobSize, knobSize);

            var toggle = go.AddComponent<Toggle>();
            toggle.transition = Selectable.Transition.None;
            toggle.targetGraphic = track;
            toggle.isOn = value;

            // Apply the visual state (track colour + knob position) and persist on change.
            void ApplyVisual(bool on)
            {
                track.color = on ? UITheme.Success : UITheme.Neutral;
                float x = on ? knobInset + knobTravel : knobInset;
                knobRt.anchoredPosition = new Vector2(x, 0f);
            }

            ApplyVisual(value);
            toggle.onValueChanged.AddListener(on =>
            {
                ApplyVisual(on);
                onChange(on);
            });
        }

        private static Image MakeImage(Transform parent, string name, Color color, bool stretch)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            if (stretch)
            {
                rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
                rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
            }
            else
            {
                rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
            }
            var img = go.AddComponent<Image>();
            img.color = color;
            img.sprite = UITheme.RoundedSprite();
            img.type = Image.Type.Sliced;
            return img;
        }

        private static RectTransform MakeRect(Transform parent, string name, bool stretch)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            if (stretch)
            {
                rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
                rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
            }
            return rt;
        }

        /// Center a stretched rect vertically to the given height (a thin track inside a taller row).
        private static void InsetVertical(RectTransform rt, float height)
        {
            rt.anchorMin = new Vector2(rt.anchorMin.x, 0.5f);
            rt.anchorMax = new Vector2(rt.anchorMax.x, 0.5f);
            rt.sizeDelta = new Vector2(rt.sizeDelta.x, height);
        }

        // --- Formatting / sound --------------------------------------------

        private static string Pct(float v) => Mathf.RoundToInt(Mathf.Clamp01(v) * 100f) + "%";
        private static string Mult(float v) => v.ToString("0.0") + "x";

        private static void AudioManager_PlayClick()
        {
            StumbleClone.Audio.AudioManager.Play(StumbleClone.Audio.Sfx.UiClick);
        }
    }
}
