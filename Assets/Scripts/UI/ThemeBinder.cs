using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace StumbleClone.UI
{
    /// Applies the UITheme to scene-wired (serialized) UI at runtime — rounded, tactile buttons,
    /// the shared playful font on labels, and themed surfaces — so screens authored in scenes or
    /// prefabs match the code-built ones (Title/Victory/Leaderboard) without hand-editing those
    /// assets. Every method is null-safe, so callers can fire them on optional serialized refs.
    public static class ThemeBinder
    {
        /// Round + recolor a button, give it press/hover feedback, and style its label white-bold.
        public static void StyleButton(Button btn, Color color, bool bold = true)
        {
            if (btn == null) return;

            var img = btn.image != null ? btn.image : btn.GetComponent<Image>();
            if (img != null)
            {
                img.color = color;
                img.sprite = UITheme.RoundedSprite();
                img.type = Image.Type.Sliced;
            }

            btn.transition = Selectable.Transition.None; // ButtonFeedback owns the visual states
            if (btn.GetComponent<ButtonFeedback>() == null)
                btn.gameObject.AddComponent<ButtonFeedback>().Init(img);

            var lbl = btn.GetComponentInChildren<TMP_Text>();
            if (lbl != null)
            {
                UITheme.ApplyFont(lbl);
                if (bold) lbl.fontStyle |= FontStyles.Bold;
                lbl.color = Color.white;
            }
        }

        /// Apply the shared font (and optionally a color) to a label.
        public static void StyleText(TMP_Text t, Color? color = null)
        {
            if (t == null) return;
            UITheme.ApplyFont(t);
            if (color.HasValue) t.color = color.Value;
        }

        /// Turn a panel's backing image into a readable modal scrim (deep-navy, semi-opaque).
        public static void StyleScrim(GameObject panel, float alpha = 0.86f)
        {
            if (panel == null) return;
            var img = panel.GetComponent<Image>();
            if (img != null)
                img.color = new Color(UITheme.SurfaceDeep.r, UITheme.SurfaceDeep.g, UITheme.SurfaceDeep.b, alpha);
        }
    }
}
