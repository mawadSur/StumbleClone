using UnityEditor;
using UnityEngine;

namespace StumbleClone.EditorTools
{
    /// One headless entry point for a production web build: generate the UI font, fix looping
    /// animations, then run the WebGL build (which exits the editor in batch mode). Chaining
    /// these in a single Unity launch avoids three slow batch-mode startups.
    ///
    /// Headless (from WSL, GUI closed):
    ///   "<editor>/Unity.exe" -batchmode -projectPath "&lt;win path&gt;" \
    ///       -executeMethod StumbleClone.EditorTools.ProdBuild.WebGL \
    ///       -logFile "&lt;log&gt;"
    public static class ProdBuild
    {
        [MenuItem("StumbleClone/Build/Prod Web (font + anim + WebGL)")]
        public static void WebGL()
        {
            Debug.Log("[ProdBuild] Step 1/4 — UI font");
            UIFontBuilder.Run();

            Debug.Log("[ProdBuild] Step 2/4 — looping animations");
            AnimationLoopFixer.Run();

            Debug.Log("[ProdBuild] Step 3/5 — character skins");
            SkinSetup.Run();

            Debug.Log("[ProdBuild] Step 4/5 — app icon");
            IconSetup.Run();

            Debug.Log("[ProdBuild] Step 5/5 — WebGL build");
            PlatformBuilder.BuildWebGL(); // calls EditorApplication.Exit in batch mode
        }
    }
}
