using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace StumbleClone.UI
{
    /// Small builders for constructing UGUI entirely in code (no scene wiring), matching the
    /// pattern SpectateController/MobileControls already use. Lets TitleScreen and LeaderboardUI
    /// self-instantiate so they work without a scene rebuild.
    public static class RuntimeUI
    {
        /// A ScreenSpaceOverlay canvas scaled to 1920x1080, with a GraphicRaycaster. Ensures an
        /// EventSystem exists so buttons/inputs receive events.
        ///
        /// Returns a full-rect "SafeArea" child (carrying a <see cref="SafeAreaFitter"/>) rather
        /// than the canvas itself, so callers that parent their Bg + content onto the returned
        /// transform get device safe-area insetting (notch / Dynamic Island / hole-punch / nav bar)
        /// for free. On desktop and most WebGL the safe area equals the full screen, so the fitter
        /// resolves to anchors (0,0)-(1,1) with zero offsets — a NO-OP that leaves existing layouts
        /// byte-identical. The returned object still answers GetComponent&lt;GraphicRaycaster&gt;()
        /// (one is added to the child) and SetActive()/OverlayIntro continue to operate on it.
        public static GameObject Overlay(string name, int sortingOrder)
        {
            EnsureEventSystem();
            var go = new GameObject(name);
            var canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = sortingOrder;
            var scaler = go.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            // Balanced match: the 1920x1080 design then scales gracefully on tall phones AND on a
            // 4:3 iPad landscape instead of clipping to one axis. On 16:9 desktop/WebGL the width
            // and height scale factors are equal, so this changes nothing there.
            scaler.matchWidthOrHeight = 0.5f;
            go.AddComponent<GraphicRaycaster>();

            // Full-rect SafeArea child that everything else parents onto. The fitter drives its
            // anchors to the device safe area each time the screen/orientation/safe-area changes.
            var safeArea = new GameObject("SafeArea", typeof(RectTransform));
            var safeRt = (RectTransform)safeArea.transform;
            safeRt.SetParent(go.transform, false);
            safeRt.anchorMin = Vector2.zero;
            safeRt.anchorMax = Vector2.one;
            safeRt.offsetMin = Vector2.zero;
            safeRt.offsetMax = Vector2.zero;
            // A raycaster on the child keeps GetComponent<GraphicRaycaster>() working for callers
            // that fetch it from the returned object (e.g. HazardTell disables it to stay
            // non-interactive). The canvas keeps its own raycaster for interactive overlays.
            safeArea.AddComponent<GraphicRaycaster>();
            // The canvas was created solely to host this overlay; tying its lifetime to the
            // returned SafeArea child means callers that Destroy(returned) tear down the whole
            // overlay (canvas included) exactly as they did when Overlay returned the canvas GO —
            // no orphaned empty canvas left behind for transient toasts/modals.
            safeArea.AddComponent<SafeAreaFitter>().OwnParentCanvas();
            return safeArea;
        }

        public static void EnsureEventSystem()
        {
            if (Object.FindFirstObjectByType<EventSystem>() != null) return;
            var es = new GameObject("EventSystem");
            es.AddComponent<EventSystem>();
            // The project ships with the new Input System (activeInputHandler = Input System only).
            // A StandaloneInputModule reads via the legacy UnityEngine.Input API, which THROWS every
            // frame under that backend — and that exception stalls the EventSystem/PlayerInput update,
            // killing gameplay input (the "WASD doesn't move" bug). Use the Input System UI module,
            // matching MvpBootstrap and MobileControls. Guarded so the old backend still compiles.
#if ENABLE_INPUT_SYSTEM
            es.AddComponent<UnityEngine.InputSystem.UI.InputSystemUIInputModule>();
#else
            es.AddComponent<StandaloneInputModule>();
#endif
        }

        public static Image Panel(Transform parent, string name, Color color,
            Vector2 anchorMin, Vector2 anchorMax, Vector2 offsetMin, Vector2 offsetMax)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            rt.anchorMin = anchorMin; rt.anchorMax = anchorMax;
            rt.offsetMin = offsetMin; rt.offsetMax = offsetMax;
            var img = go.AddComponent<Image>();
            img.color = color;
            return img;
        }

        public static TMP_Text Label(Transform parent, string text, int fontSize,
            Vector2 anchor, Vector2 anchoredPos, Vector2 size, TextAlignmentOptions align = TextAlignmentOptions.Center)
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
            tmp.alignment = align;
            tmp.color = UITheme.OnSurface;
            UITheme.ApplyFont(tmp);
            return tmp;
        }

        public static Button Button(Transform parent, string label, Color color,
            Vector2 anchor, Vector2 anchoredPos, Vector2 size, UnityEngine.Events.UnityAction onClick)
        {
            var go = new GameObject(label + "Button", typeof(RectTransform));
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            rt.anchorMin = anchor; rt.anchorMax = anchor; rt.pivot = anchor;
            rt.anchoredPosition = anchoredPos;
            rt.sizeDelta = size;

            var img = go.AddComponent<Image>();
            img.color = color;
            img.sprite = UITheme.RoundedSprite();   // chunky rounded corners
            img.type = Image.Type.Sliced;

            var btn = go.AddComponent<Button>();
            btn.transition = Selectable.Transition.None; // ButtonFeedback drives the visuals
            if (onClick != null) btn.onClick.AddListener(onClick);
            go.AddComponent<ButtonFeedback>().Init(img); // press scale + hover lift

            var lbl = Label(go.transform, label, 32, new Vector2(0.5f, 0.5f), Vector2.zero, size);
            lbl.fontStyle = FontStyles.Bold;
            lbl.color = Color.white;
            return btn;
        }

        /// Minimal TMP_InputField with placeholder + text, ready to type into.
        public static TMP_InputField InputField(Transform parent, string placeholder, string value,
            Vector2 anchor, Vector2 anchoredPos, Vector2 size)
        {
            var go = new GameObject("InputField", typeof(RectTransform));
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            rt.anchorMin = anchor; rt.anchorMax = anchor; rt.pivot = anchor;
            rt.anchoredPosition = anchoredPos;
            rt.sizeDelta = size;

            var bg = go.AddComponent<Image>();
            bg.color = new Color(1f, 1f, 1f, 0.92f);
            bg.sprite = UITheme.RoundedSprite();
            bg.type = Image.Type.Sliced;
            var input = go.AddComponent<TMP_InputField>();

            var area = new GameObject("TextArea", typeof(RectTransform));
            var areaRt = (RectTransform)area.transform;
            areaRt.SetParent(go.transform, false);
            areaRt.anchorMin = Vector2.zero; areaRt.anchorMax = Vector2.one;
            areaRt.offsetMin = new Vector2(14f, 6f); areaRt.offsetMax = new Vector2(-14f, -6f);
            area.AddComponent<RectMask2D>();

            var ph = MakeChildText(areaRt, placeholder, new Color(0.3f, 0.3f, 0.3f, 0.8f));
            var txt = MakeChildText(areaRt, "", new Color(0.05f, 0.05f, 0.05f, 1f));

            input.textViewport = areaRt;
            input.textComponent = txt;
            input.placeholder = ph;
            input.characterLimit = 16;
            input.text = value;
            input.pointSize = 32f;
            return input;
        }

        private static TMP_Text MakeChildText(RectTransform parent, string text, Color color)
        {
            var go = new GameObject("Text", typeof(RectTransform));
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
            var tmp = go.AddComponent<TextMeshProUGUI>();
            tmp.text = text;
            tmp.color = color;
            tmp.fontSize = 32f;
            tmp.alignment = TextAlignmentOptions.Left;
            UITheme.ApplyFont(tmp);
            return tmp;
        }
    }
}
