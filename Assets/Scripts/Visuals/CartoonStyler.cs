using System.Collections.Generic;
using StumbleClone.Core;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace StumbleClone.Visuals
{
    /// Nudges every racer's character toward a flatter, poppier cartoon read at runtime: it kills
    /// the plasticky PBR specular (full matte) and bumps colour saturation/brightness a touch, so
    /// the low-poly models look like bright cartoon characters rather than semi-realistic figures.
    ///
    /// Safe by construction — it only adjusts properties that already exist on the model's
    /// materials (URP Lit's _Metallic/_Smoothness/_BaseColor, or legacy _Glossiness/_Color). Setting
    /// a property a material doesn't have is guarded by HasProperty, so nothing breaks and nothing
    /// renders magenta. (A full cel/outline shader is a heavier, separate pass.)
    ///
    /// Self-bootstrapping per gameplay scene (mirrors SceneAtmosphere/PowerupHud); styles each racer
    /// exactly once, after CharacterSkin (-500) has swapped in the chosen model.
    public sealed class CartoonStyler : MonoBehaviour
    {
        private static CartoonStyler _instance;
        private readonly HashSet<int> _styled = new HashSet<int>();

        private static readonly int MetallicId   = Shader.PropertyToID("_Metallic");
        private static readonly int SmoothnessId  = Shader.PropertyToID("_Smoothness");
        private static readonly int GlossinessId  = Shader.PropertyToID("_Glossiness");
        private static readonly int BaseColorId   = Shader.PropertyToID("_BaseColor");
        private static readonly int LegacyColorId = Shader.PropertyToID("_Color");

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            SceneManager.sceneLoaded += OnSceneLoaded;
            EnsureForScene(SceneManager.GetActiveScene());
        }

        private static void OnSceneLoaded(Scene s, LoadSceneMode m) => EnsureForScene(s);

        private static void EnsureForScene(Scene scene)
        {
            if (!scene.IsValid() || !scene.name.StartsWith("Level")) return;
            if (_instance != null) return;
            _instance = new GameObject("CartoonStyler").AddComponent<CartoonStyler>();
        }

        private void Awake()
        {
            if (_instance != null && _instance != this) { Destroy(gameObject); return; }
            _instance = this;
        }

        private void OnDestroy() { if (_instance == this) _instance = null; }

        private void Update()
        {
            var all = RacerRegistry.All;
            for (int i = 0; i < all.Count; i++)
            {
                IRacer r = all[i];
                if (r == null || r.Transform == null) continue;
                int id = r.Transform.GetInstanceID();
                if (!_styled.Add(id)) continue; // style each racer once
                Stylize(r.Transform);
            }
        }

        private static void Stylize(Transform root)
        {
            var renderers = root.GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < renderers.Length; i++)
            {
                var mats = renderers[i].materials; // instances — safe to mutate per-renderer
                for (int m = 0; m < mats.Length; m++)
                {
                    Material mat = mats[m];
                    if (mat == null) continue;

                    // Matte: drop the realistic specular so lighting reads flat/cartoon.
                    if (mat.HasProperty(MetallicId)) mat.SetFloat(MetallicId, 0f);
                    if (mat.HasProperty(SmoothnessId)) mat.SetFloat(SmoothnessId, 0.08f);
                    if (mat.HasProperty(GlossinessId)) mat.SetFloat(GlossinessId, 0.08f);

                    // Pop: nudge the tint's saturation/brightness up for a brighter toy-like palette.
                    Saturate(mat, BaseColorId);
                    Saturate(mat, LegacyColorId);
                }
            }
        }

        private static void Saturate(Material mat, int prop)
        {
            if (!mat.HasProperty(prop)) return;
            Color c = mat.GetColor(prop);
            Color.RGBToHSV(c, out float h, out float s, out float v);
            s = Mathf.Clamp01(s * 1.25f + 0.04f);
            v = Mathf.Clamp01(v * 1.05f);
            Color boosted = Color.HSVToRGB(h, s, v);
            boosted.a = c.a;
            mat.SetColor(prop, boosted);
        }
    }
}
