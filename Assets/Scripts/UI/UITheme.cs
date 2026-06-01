using TMPro;
using UnityEngine;

namespace StumbleClone.UI
{
    /// Central design tokens for every runtime UGUI screen — palette, type, corner radius —
    /// so screens stop hard-coding magic colors. Party-game language (Claymorphism): vibrant
    /// pink primary, reward gold for the single main CTA, purple secondary, deep-navy surfaces.
    ///
    /// Also provides a procedurally generated rounded-rect sprite (no imported asset required,
    /// so it works headlessly) for 9-sliced chunky buttons, and a shared playful font loaded
    /// from Resources/UIFont (falls back to the TMP default if the asset isn't present yet —
    /// run "StumbleClone/Build UI Font" once to generate it).
    public static class UITheme
    {
        // ---- Palette (sRGB) -------------------------------------------------
        public static readonly Color Primary        = Hex(0xEC4899); // pink — primary CTA
        public static readonly Color Secondary      = Hex(0x8B5CF6); // purple — secondary actions
        public static readonly Color Accent         = Hex(0xF59E0B); // gold — reward / highlight
        public static readonly Color Success        = Hex(0x22C55E);
        public static readonly Color Danger         = Hex(0xEF4444);
        public static readonly Color Neutral        = Hex(0x334155); // subdued button / tab
        public static readonly Color SurfaceDeep     = Hex(0x0E1018); // full-screen background
        public static readonly Color Surface         = Hex(0x1B2030); // raised panel
        public static readonly Color OnSurface       = Hex(0xF8FAFC); // body text
        public static readonly Color OnSurfaceMuted  = Hex(0x94A3B8); // captions / subtitles
        public static readonly Color Gold            = Hex(0xFFD24D); // title text / leaderboard row

        // ---- Corner radius for the rounded sprite (source-texture pixels) ----
        public const int CornerRadius = 28;

        // ---- Shared playful font -------------------------------------------
        private static TMP_FontAsset _font;
        private static bool _fontTried;

        /// The project UI font (Fredoka). Null until Resources/UIFont.asset is generated —
        /// callers should treat null as "leave the TMP default in place".
        public static TMP_FontAsset Font
        {
            get
            {
                if (!_fontTried)
                {
                    _fontTried = true;
                    _font = Resources.Load<TMP_FontAsset>("UIFont");
                }
                return _font;
            }
        }

        /// Apply the shared font to a label if it's available (no-op otherwise).
        public static void ApplyFont(TMP_Text t)
        {
            if (t != null && Font != null) t.font = Font;
        }

        // ---- Procedural rounded-rect sprite (cached, 9-sliced) -------------
        private static Sprite _rounded;

        /// A white rounded-rect sprite with a matching 9-slice border, generated once at
        /// runtime. Tint via Image.color; use Image.Type.Sliced so corners keep their radius
        /// at any button size.
        public static Sprite RoundedSprite()
        {
            if (_rounded != null) return _rounded;
            _rounded = BuildRounded(CornerRadius);
            return _rounded;
        }

        private static Sprite BuildRounded(int radius)
        {
            int size = radius * 2 + 2;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
                name = "UIThemeRounded",
            };

            var px = new Color32[size * size];
            int hi = size - 1 - radius; // inner-rect far edge on each axis
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    // Distance from the inner rect [radius, hi]^2; only corners exceed it on both axes.
                    float dx = Mathf.Max(Mathf.Max(radius - x, x - hi), 0f);
                    float dy = Mathf.Max(Mathf.Max(radius - y, y - hi), 0f);
                    float dist = Mathf.Sqrt(dx * dx + dy * dy);
                    float a = Mathf.Clamp01(radius - dist + 0.5f); // 1px anti-aliased edge
                    px[y * size + x] = new Color32(255, 255, 255, (byte)(a * 255f));
                }
            }
            tex.SetPixels32(px);
            tex.Apply(false, false);

            var border = new Vector4(radius, radius, radius, radius);
            return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f),
                100f, 0, SpriteMeshType.FullRect, border);
        }

        private static Color Hex(uint rgb)
        {
            return new Color(((rgb >> 16) & 0xFF) / 255f, ((rgb >> 8) & 0xFF) / 255f, (rgb & 0xFF) / 255f, 1f);
        }
    }
}
