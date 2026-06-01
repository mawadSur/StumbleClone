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
            Debug.Log("[ProdBuild] Step 1/3 — UI font");
            UIFontBuilder.Run();

            Debug.Log("[ProdBuild] Step 2/3 — looping animations");
            AnimationLoopFixer.Run();

            Debug.Log("[ProdBuild] Step 3/3 — WebGL build");
            PlatformBuilder.BuildWebGL(); // calls EditorApplication.Exit in batch mode
        }
    }
}
