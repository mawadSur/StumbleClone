#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace StumbleClone.EditorTools
{
    /// Headless importer for TextMeshPro Essential Resources. The normal flow is the
    /// interactive "Window > TextMeshPro > Import TMP Essential Resources" menu; this
    /// drives the same import silently and waits for completion so it works in -batchmode.
    /// Without the essentials, all TextMeshPro UI (menu, HUD) renders with no font.
    public static class TmpEssentialsImporter
    {
        private static double _start;

        public static void Run()
        {
            var existing = AssetDatabase.FindAssets("t:TMP_Settings");
            if (existing != null && existing.Length > 0)
            {
                Debug.Log("[TMPImport] TMP_Settings already present — nothing to do.");
                if (Application.isBatchMode) EditorApplication.Exit(0);
                return;
            }

            AssetDatabase.importPackageCompleted += OnDone;
            AssetDatabase.importPackageFailed += OnFail;
            EditorApplication.update += Watchdog;
            _start = EditorApplication.timeSinceStartup;

            Debug.Log("[TMPImport] Importing TMP Essential Resources (silent)...");
            TMPro.TMP_PackageResourceImporter.ImportResources(true, false, false);
        }

        private static void OnDone(string packageName)
        {
            Debug.Log($"[TMPImport] Import completed: {packageName}");
            Cleanup();
            AssetDatabase.Refresh();
            if (Application.isBatchMode) EditorApplication.Exit(0);
        }

        private static void OnFail(string packageName, string error)
        {
            Debug.LogError($"[TMPImport] Import FAILED: {packageName} => {error}");
            Cleanup();
            if (Application.isBatchMode) EditorApplication.Exit(1);
        }

        private static void Watchdog()
        {
            if (EditorApplication.timeSinceStartup - _start > 180.0)
            {
                Debug.LogError("[TMPImport] Timed out waiting for TMP import to complete.");
                Cleanup();
                if (Application.isBatchMode) EditorApplication.Exit(3);
            }
        }

        private static void Cleanup()
        {
            AssetDatabase.importPackageCompleted -= OnDone;
            AssetDatabase.importPackageFailed -= OnFail;
            EditorApplication.update -= Watchdog;
        }
    }
}
#endif
