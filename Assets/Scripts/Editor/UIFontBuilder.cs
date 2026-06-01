using System.IO;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.TextCore.LowLevel;

namespace StumbleClone.EditorTools
{
    /// Generates the shared playful UI font (TMP_FontAsset) from Assets/Fonts/Fredoka.ttf and
    /// saves it to Assets/Resources/UIFont.asset, where UITheme.Font loads it at runtime. Common
    /// ASCII glyphs are pre-rendered into the atlas at generation time so text renders reliably
    /// in WebGL builds without relying on runtime atlas population.
    ///
    /// Idempotent: if UIFont.asset already exists it does nothing (delete it to regenerate).
    public static class UIFontBuilder
    {
        private const string TtfPath = "Assets/Fonts/Fredoka.ttf";
        private const string OutDir = "Assets/Resources";
        private const string OutPath = "Assets/Resources/UIFont.asset";

        private const string Ascii =
            "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789 " +
            ".,!?:;'\"()[]{}/\\|@#$%^&*-+=_<>~`—’";

        [MenuItem("StumbleClone/Build UI Font")]
        public static void Run()
        {
            if (AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(OutPath) != null)
            {
                Debug.Log("[UIFont] Resources/UIFont.asset already exists — skipping.");
                return;
            }

            var font = AssetDatabase.LoadAssetAtPath<Font>(TtfPath);
            if (font == null)
            {
                Debug.LogError($"[UIFont] No TTF found at {TtfPath}.");
                return;
            }

            Directory.CreateDirectory(OutDir);

            // 90px sampling, 9px padding, SDF atlas, 1024² dynamic atlas (so we can add glyphs now).
            var fa = TMP_FontAsset.CreateFontAsset(
                font, 90, 9, GlyphRenderMode.SDFAA, 1024, 1024,
                AtlasPopulationMode.Dynamic, enableMultiAtlasSupport: true);

            if (fa == null)
            {
                Debug.LogError("[UIFont] CreateFontAsset returned null.");
                return;
            }

            fa.name = "UIFont";
            AssetDatabase.CreateAsset(fa, OutPath);

            // Bake the common glyphs into the atlas now so they ship inside the asset.
            fa.TryAddCharacters(Ascii, out string missing);
            if (!string.IsNullOrEmpty(missing))
                Debug.LogWarning($"[UIFont] Glyphs not in font, skipped: {missing}");

            // Persist the material + atlas texture as sub-assets of UIFont.asset.
            if (fa.material != null)
            {
                fa.material.name = "UIFont Material";
                AssetDatabase.AddObjectToAsset(fa.material, fa);
            }
            if (fa.atlasTextures != null)
            {
                foreach (var atlas in fa.atlasTextures)
                {
                    if (atlas != null && !AssetDatabase.Contains(atlas))
                        AssetDatabase.AddObjectToAsset(atlas, fa);
                }
            }

            EditorUtility.SetDirty(fa);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log($"[UIFont] Created {OutPath} from {TtfPath}.");
        }
    }
}
