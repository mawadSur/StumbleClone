using System.IO;
using StumbleClone.Game;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace StumbleClone.EditorTools
{
    /// Build-time + preview helpers for the cartoon look.
    ///
    /// 1. <see cref="EnsureAlwaysIncludedShaders"/> forces the runtime-`Shader.Find` shaders into
    ///    the player build. WebGL strips any shader not referenced by a material/scene, which is
    ///    exactly why the procedural sky (Skybox/Panoramic, built at runtime in SceneAtmosphere)
    ///    and our StumbleClone/Toon shader vanish in the web build — the "backdrop not showing"
    ///    bug. Run as ProdBuild step 0.
    /// 2. <see cref="CaptureToonPreview"/> renders one character with the toon shader to a PNG so
    ///    the look can be verified headlessly before a full WebGL build (no more shipping shaders
    ///    blind). Run with a GRAPHICS batch (no -nographics).
    public static class ToonTools
    {
        private static readonly string[] RequiredShaders =
        {
            "Skybox/Panoramic",         // procedural daytime sky (SceneAtmosphere) — the backdrop
            "StumbleClone/Toon",        // character cel shader + outline
            "Unlit/Texture",            // skydome backdrop sphere (SceneAtmosphere) — built at runtime
        };

        public static void EnsureAlwaysIncludedShaders()
        {
            var settings = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/GraphicsSettings.asset");
            if (settings == null || settings.Length == 0) { Debug.LogWarning("[ToonTools] GraphicsSettings not found."); return; }

            var so = new SerializedObject(settings[0]);
            var arr = so.FindProperty("m_AlwaysIncludedShaders");
            if (arr == null) { Debug.LogWarning("[ToonTools] m_AlwaysIncludedShaders missing."); return; }

            foreach (string name in RequiredShaders)
            {
                Shader shader = Shader.Find(name);
                if (shader == null) { Debug.LogWarning($"[ToonTools] shader not found (skipped): {name}"); continue; }

                bool present = false;
                for (int i = 0; i < arr.arraySize; i++)
                {
                    if (arr.GetArrayElementAtIndex(i).objectReferenceValue == shader) { present = true; break; }
                }
                if (present) continue;

                int idx = arr.arraySize;
                arr.InsertArrayElementAtIndex(idx);
                arr.GetArrayElementAtIndex(idx).objectReferenceValue = shader;
                Debug.Log($"[ToonTools] Added always-included shader: {name}");
            }

            so.ApplyModifiedProperties();
            AssetDatabase.SaveAssets();
        }

        /// Render the default character with the toon shader to <project>/toon_preview.png.
        [MenuItem("StumbleClone/Build/Capture Toon Preview")]
        public static void CaptureToonPreview()
        {
            EnsureAlwaysIncludedShaders();
            Shader toon = Shader.Find("StumbleClone/Toon");
            if (toon == null) { Debug.LogError("[ToonTools] StumbleClone/Toon not found."); ExitIfBatch(1); return; }

            // Preview a colourful skin so the cel bands + texture read clearly (the in-game look
            // uses the same shader on whatever skin is selected).
            string modelPath = "Assets/Art/Quaternius/Characters/Casual_Male.fbx";
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(modelPath);
            if (prefab == null) { Debug.LogError($"[ToonTools] model not found: {modelPath}"); ExitIfBatch(1); return; }

            var character = Object.Instantiate(prefab);
            character.transform.position = Vector3.zero;
            character.transform.rotation = Quaternion.Euler(0f, 180f, 0f); // face the camera

            // Swap each material's shader to toon, keeping the imported texture/colour.
            foreach (var r in character.GetComponentsInChildren<Renderer>(true))
            {
                var mats = r.sharedMaterials;
                for (int i = 0; i < mats.Length; i++)
                {
                    if (mats[i] == null) continue;
                    var src = mats[i];
                    string baseMap = src.HasProperty("_BaseMap") && src.GetTexture("_BaseMap") != null ? "BaseMap" : "-";
                    string mainTex = src.HasProperty("_MainTex") && src.GetTexture("_MainTex") != null ? "MainTex" : "-";
                    Color bc = src.HasProperty("_BaseColor") ? src.GetColor("_BaseColor")
                             : src.HasProperty("_Color") ? src.GetColor("_Color") : Color.magenta;
                    Debug.Log($"[ToonTools] MAT '{src.name}' shader={src.shader.name} tex={baseMap}/{mainTex} baseColor={bc}");
                    var m = new Material(src) { shader = toon };
                    mats[i] = m;
                }
                r.sharedMaterials = mats;
            }

            // Sunny flat ambient + a warm key light (matches SceneAtmosphere).
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.78f, 0.80f, 0.74f);
            var sun = new GameObject("Sun").AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.color = new Color(1f, 0.97f, 0.88f);
            sun.intensity = 1.45f;
            sun.transform.rotation = Quaternion.Euler(35f, 200f, 0f); // from behind/above the camera, lights the front

            int W = 540, H = 720;
            var camGo = new GameObject("PreviewCam");
            var cam = camGo.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.36f, 0.66f, 0.97f); // sky blue, like the in-game backdrop
            cam.fieldOfView = 35f;
            camGo.transform.position = new Vector3(0f, 1.0f, 3.1f);
            camGo.transform.LookAt(new Vector3(0f, 0.95f, 0f));

            var rt = new RenderTexture(W, H, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 };
            cam.targetTexture = rt;
            cam.Render();

            RenderTexture.active = rt;
            var tex = new Texture2D(W, H, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, W, H), 0, 0);
            tex.Apply();
            RenderTexture.active = null;
            cam.targetTexture = null;

            string outPath = Path.Combine(Directory.GetCurrentDirectory(), "toon_preview.png");
            File.WriteAllBytes(outPath, tex.EncodeToPNG());
            Debug.Log($"[ToonTools] wrote {outPath}");

            Object.DestroyImmediate(rt);
            Object.DestroyImmediate(character);
            ExitIfBatch(0);
        }

        private static void ExitIfBatch(int code)
        {
            if (Application.isBatchMode) EditorApplication.Exit(code);
        }
    }
}
