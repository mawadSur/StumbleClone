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
            go.AddComponent<GraphicRaycaster>();
            return go;
        }

        public static void EnsureEventSystem()
        {
            if (Object.FindFirstObjectByType<EventSystem>() != null) return;
            var es = new GameObject("EventSystem");
            es.AddComponent<EventSystem>();
            es.AddComponent<StandaloneInputModule>();
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
            tmp.color = Color.white;
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
            var btn = go.AddComponent<Button>();
            if (onClick != null) btn.onClick.AddListener(onClick);

            Label(go.transform, label, 30, new Vector2(0.5f, 0.5f), Vector2.zero, size);
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
            return tmp;
        }
    }
}
