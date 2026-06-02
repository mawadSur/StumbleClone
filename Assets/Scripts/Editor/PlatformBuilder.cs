using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace StumbleClone.EditorTools
{
    /// One-stop multi-platform build + player-settings configuration for the
    /// StumbleClone MVP. Drives WebGL, Android (APK) and iOS (Xcode project)
    /// builds, and applies landscape orientation, bundle identifiers, and
    /// per-platform scripting/compression settings.
    ///
    /// Menu:   StumbleClone > Build > ...
    /// Headless (from WSL, GUI closed):
    ///   "<editor>/Unity.exe" -batchmode -projectPath "<win path>" \
    ///       -executeMethod StumbleClone.EditorTools.PlatformBuilder.BuildWebGL \
    ///       -logFile "<log>"
    /// (Each build method calls EditorApplication.Exit in batch mode, so no
    /// -quit is needed — mirrors MvpBootstrap.)
    public static class PlatformBuilder
    {
        private const string Company = "ClaudeCodeGameStudios";
        private const string Product = "StumbleKids";
        // Placeholder reverse-DNS id. For iOS device installs / store builds,
        // change this to one that matches your Apple provisioning profile.
        private const string BundleId = "com.stumblekids.game";

        private const string OutputRoot = "Builds";

        // --- Menu items ----------------------------------------------------

        [MenuItem("StumbleClone/Build/Configure Platform Settings (no build)")]
        public static void ConfigureAllPlatforms()
        {
            ConfigureCommon();
            ConfigureWebGL();
            ConfigureAndroid();
            ConfigureIOS();
            AssetDatabase.SaveAssets();
            Debug.Log("[PlatformBuilder] Configured common + WebGL + Android + iOS player settings.");
        }

        [MenuItem("StumbleClone/Build/WebGL")]
        public static void BuildWebGL()
        {
            ConfigureCommon();
            ConfigureWebGL();
            SwitchTarget(BuildTargetGroup.WebGL, BuildTarget.WebGL);
            Run(BuildTarget.WebGL, Path.Combine(OutputRoot, "WebGL"));
        }

        [MenuItem("StumbleClone/Build/Android (APK)")]
        public static void BuildAndroid()
        {
            ConfigureCommon();
            ConfigureAndroid();
            EditorUserBuildSettings.buildAppBundle = false;
            SwitchTarget(BuildTargetGroup.Android, BuildTarget.Android);
            Run(BuildTarget.Android, Path.Combine(OutputRoot, "Android", Product + ".apk"));
        }

        [MenuItem("StumbleClone/Build/Android (AAB - Play Store)")]
        public static void BuildAndroidAab()
        {
            ConfigureCommon();
            ConfigureAndroid();
            EditorUserBuildSettings.buildAppBundle = true;
            SwitchTarget(BuildTargetGroup.Android, BuildTarget.Android);
            Run(BuildTarget.Android, Path.Combine(OutputRoot, "Android", Product + ".aab"));
        }

        [MenuItem("StumbleClone/Build/iOS (Xcode project)")]
        public static void BuildIOS()
        {
            ConfigureCommon();
            ConfigureIOS();
            SwitchTarget(BuildTargetGroup.iOS, BuildTarget.iOS);
            // iOS "build" emits an Xcode project directory; compile & sign on a Mac.
            Run(BuildTarget.iOS, Path.Combine(OutputRoot, "iOS"));
        }

        // --- Configuration -------------------------------------------------

        private static void ConfigureCommon()
        {
            PlayerSettings.companyName = Company;
            PlayerSettings.productName = Product;

            // Landscape, both directions, no portrait.
            PlayerSettings.defaultInterfaceOrientation = UIOrientation.LandscapeLeft;
            PlayerSettings.allowedAutorotateToLandscapeLeft = true;
            PlayerSettings.allowedAutorotateToLandscapeRight = true;
            PlayerSettings.allowedAutorotateToPortrait = false;
            PlayerSettings.allowedAutorotateToPortraitUpsideDown = false;
            PlayerSettings.useAnimatedAutorotation = true;
        }

        private static void ConfigureWebGL()
        {
            // Gzip + JS decompression fallback => plays on any static host,
            // including `python -m http.server` (no content-encoding headers
            // required). Switch to Brotli for production hosting that sets them.
            PlayerSettings.WebGL.compressionFormat = WebGLCompressionFormat.Gzip;
            PlayerSettings.WebGL.decompressionFallback = true;
            PlayerSettings.WebGL.dataCaching = true;
            PlayerSettings.WebGL.exceptionSupport = WebGLExceptionSupport.ExplicitlyThrownExceptionsOnly;
            // Branded, responsive wrapper (Assets/WebGLTemplates/StumbleClone) — emitted on every
            // build, so the deployed web/ chrome stays on-brand without manual post-build edits.
            PlayerSettings.WebGL.template = "PROJECT:StumbleClone";
            PlayerSettings.SetScriptingBackend(NamedBuildTarget.WebGL, ScriptingImplementation.IL2CPP);
        }

        private static void ConfigureAndroid()
        {
            PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Android, BundleId);
            PlayerSettings.SetScriptingBackend(NamedBuildTarget.Android, ScriptingImplementation.IL2CPP);
            PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
            PlayerSettings.Android.minSdkVersion = AndroidSdkVersions.AndroidApiLevel25;   // Android 7.1 (24 is now deprecated)
            PlayerSettings.Android.targetSdkVersion = AndroidSdkVersions.AndroidApiLevelAuto;
            PlayerSettings.Android.optimizedFramePacing = true;
        }

        private static void ConfigureIOS()
        {
            PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.iOS, BundleId);
            PlayerSettings.SetScriptingBackend(NamedBuildTarget.iOS, ScriptingImplementation.IL2CPP);
            PlayerSettings.iOS.targetOSVersionString = "13.0";
            PlayerSettings.iOS.targetDevice = iOSTargetDevice.iPhoneAndiPad;
            // The game uses neither camera nor microphone, so we deliberately do NOT set usage
            // descriptions — adding empty NSCamera/NSMicrophone usage strings injects Info.plist
            // keys with no purpose string, which App Store review rejects. Leave them unset.
            // BundleId ("com.stumbleclone.game") is a placeholder — replace with the App ID
            // registered in your Apple Developer account before an App Store submission.
        }

        // --- Build driver --------------------------------------------------

        private static void SwitchTarget(BuildTargetGroup group, BuildTarget target)
        {
            if (EditorUserBuildSettings.activeBuildTarget != target)
            {
                EditorUserBuildSettings.SwitchActiveBuildTarget(group, target);
            }
        }

        private static void Run(BuildTarget target, string locationPathName)
        {
            string[] scenes = EditorBuildSettings.scenes
                .Where(s => s.enabled)
                .Select(s => s.path)
                .ToArray();

            if (scenes.Length == 0)
            {
                Fail($"No enabled scenes in Build Settings — cannot build {target}.");
                return;
            }

            string dir = Path.GetDirectoryName(locationPathName);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            var options = new BuildPlayerOptions
            {
                scenes = scenes,
                locationPathName = locationPathName,
                target = target,
                options = BuildOptions.None,
            };

            Debug.Log($"[PlatformBuilder] Building {target} -> {locationPathName} ({scenes.Length} scenes)");
            BuildReport report = BuildPipeline.BuildPlayer(options);
            BuildSummary summary = report.summary;

            if (summary.result == BuildResult.Succeeded)
            {
                Debug.Log($"[PlatformBuilder] {target} build SUCCEEDED: {summary.totalSize} bytes, " +
                          $"{summary.totalTime.TotalSeconds:F0}s -> {summary.outputPath}");
                Exit(0);
            }
            else
            {
                Fail($"{target} build {summary.result}: {summary.totalErrors} error(s).");
            }
        }

        private static void Fail(string message)
        {
            Debug.LogError("[PlatformBuilder] " + message);
            Exit(1);
        }

        private static void Exit(int code)
        {
            if (Application.isBatchMode)
            {
                EditorApplication.Exit(code);
            }
        }
    }
}
