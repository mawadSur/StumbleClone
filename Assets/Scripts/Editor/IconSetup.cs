using UnityEditor;
using UnityEngine;

namespace StumbleClone.EditorTools
{
    /// Applies Assets/Art/Icon/AppIcon.png as the game's default app icon — the icon every
    /// platform (Standalone / Android / iOS / WebGL) uses unless it has a platform-specific
    /// override. Drop a real 1024x1024 PNG at that path to replace the placeholder; the next
    /// prod build (which calls Run) picks it up. Idempotent and safe to run repeatedly.
    public static class IconSetup
    {
        private const string IconPath = "Assets/Art/Icon/AppIcon.png";

        [MenuItem("StumbleClone/Apply App Icon")]
        public static void Run()
        {
            var icon = AssetDatabase.LoadAssetAtPath<Texture2D>(IconPath);
            if (icon == null)
            {
                Debug.LogWarning($"[IconSetup] No icon found at {IconPath} — skipping (drop a 1024x1024 PNG there).");
                return;
            }

            // The "Default Icon" (Unknown target group) is the fallback Unity scales for every
            // platform that has no explicit per-platform icon set, so one 1024 source covers all.
            PlayerSettings.SetIconsForTargetGroup(BuildTargetGroup.Unknown, new[] { icon });
            Debug.Log($"[IconSetup] Applied default app icon from {IconPath}.");
        }
    }
}
