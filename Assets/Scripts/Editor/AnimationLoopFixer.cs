using System;
using UnityEditor;
using UnityEngine;

namespace StumbleClone.EditorTools
{
    /// The Quaternius character FBXs ship with their take animations imported but with Loop Time
    /// OFF (the meta's clipAnimations is empty, so Unity uses non-looping defaults). That makes
    /// the locomotion clips (Idle / Walk / Run) play once and freeze while the player keeps
    /// moving. This sets Loop Time + Loop Pose on those clips and reimports, so they cycle
    /// continuously inside the locomotion blend tree.
    ///
    /// Only the clip-source FBX needs fixing for the shared controller, but we also fix the
    /// player/bot source models so any future per-model use loops too.
    public static class AnimationLoopFixer
    {
        private static readonly string[] Targets =
        {
            "Assets/Art/Quaternius/Characters/BlueSoldier_Male.fbx", // shared locomotion clip source + player
            "Assets/Art/Quaternius/Characters/Casual_Male.fbx",      // bot model
        };

        // Substrings (case-insensitive) of clips that should loop while their state is held.
        private static readonly string[] LoopNames = { "Idle", "Walk", "Run" };

        [MenuItem("StumbleClone/Fix Looping Animations")]
        public static void Run()
        {
            int fixedClips = 0;
            foreach (var path in Targets)
            {
                if (!(AssetImporter.GetAtPath(path) is ModelImporter importer))
                {
                    Debug.LogWarning($"[AnimLoop] No ModelImporter at {path} — skipping.");
                    continue;
                }

                // Start from the full default clip list (preserves frame ranges) and flip loopTime.
                var clips = importer.defaultClipAnimations;
                bool changed = false;
                for (int i = 0; i < clips.Length; i++)
                {
                    if (ShouldLoop(clips[i].name) && !clips[i].loopTime)
                    {
                        clips[i].loopTime = true;
                        clips[i].loopPose = true;
                        changed = true;
                        fixedClips++;
                        Debug.Log($"[AnimLoop] loop ON: {path} :: {clips[i].name}");
                    }
                }

                if (changed)
                {
                    importer.clipAnimations = clips; // promote to explicit overrides
                    importer.SaveAndReimport();
                }
            }

            AssetDatabase.SaveAssets();
            Debug.Log($"[AnimLoop] Done — {fixedClips} clip(s) set to loop.");
        }

        private static bool ShouldLoop(string clipName)
        {
            if (string.IsNullOrEmpty(clipName)) return false;
            foreach (var n in LoopNames)
                if (clipName.IndexOf(n, StringComparison.OrdinalIgnoreCase) >= 0) return true;
            return false;
        }
    }
}
