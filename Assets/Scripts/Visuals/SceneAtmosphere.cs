using System.Collections;
using StumbleClone.Bots;
using StumbleClone.Core;
using StumbleClone.Player;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace StumbleClone.Visuals
{
    /// Self-bootstrapping art pass — no scene wiring, works on the already-baked binary scenes.
    ///
    ///  * Sky: a procedurally generated dark blue→purple gradient panorama with scattered stars
    ///    and a soft nebula glow, applied as the skybox.
    ///  * Lighting: bright flat ambient so everything reads "lighter", plus a cool key light.
    ///  * Characters: flattens the player + bots to a matte, brightened (toon-ish) look while
    ///    keeping their textures and rigged animations; bumps animator speed slightly for a
    ///    lighter, peppier feel.
    public sealed class SceneAtmosphere : MonoBehaviour
    {
        private static SceneAtmosphere _instance;

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
            _instance = new GameObject("SceneAtmosphere").AddComponent<SceneAtmosphere>();
        }

        private void Awake()
        {
            if (_instance != null && _instance != this) { Destroy(gameObject); return; }
            _instance = this;
            // Set the bright flat ambient as early as possible so characters never render a frame
            // under the scene's dark default ambient (the "dark for the first second" bug).
            ApplyLighting();
        }

        private void OnDestroy() { if (_instance == this) _instance = null; }

        private void Start()
        {
            ApplySky();
            ApplyLighting();
            StartCoroutine(RestyleRacersContinuously());
        }

        // ---- Sky ---------------------------------------------------------------

        private void ApplySky()
        {
            Shader sh = Shader.Find("Skybox/Panoramic");
            if (sh != null)
            {
                Texture2D tex = BuildStarPanorama(1024, 512);
                var mat = new Material(sh);
                mat.SetTexture("_MainTex", tex);
                if (mat.HasProperty("_Exposure")) mat.SetFloat("_Exposure", 1.1f);
                RenderSettings.skybox = mat;
                DynamicGI.UpdateEnvironment();
            }

            foreach (var cam in Camera.allCameras)
                cam.clearFlags = CameraClearFlags.Skybox;
        }

        // Equirectangular (lat-long) panorama: vertical gradient + nebula glow + stars.
        private static Texture2D BuildStarPanorama(int w, int h)
        {
            var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
            tex.wrapModeU = TextureWrapMode.Repeat;  // horizontal wraps seamlessly
            tex.wrapModeV = TextureWrapMode.Clamp;
            tex.filterMode = FilterMode.Bilinear;

            var px = new Color[w * h];
            Color bottom = new Color(0.020f, 0.025f, 0.060f); // deep navy at the horizon
            Color top    = new Color(0.070f, 0.035f, 0.130f); // dark purple overhead
            Color glow   = new Color(0.22f, 0.12f, 0.34f);    // nebula tint

            const float gx = 0.62f, gy = 0.60f; // glow centre in uv
            for (int y = 0; y < h; y++)
            {
                float v = y / (h - 1f);
                Color baseCol = Color.Lerp(bottom, top, v);
                for (int x = 0; x < w; x++)
                {
                    float du = (x / (w - 1f)) - gx;
                    float dv = v - gy;
                    float d = Mathf.Sqrt(du * du * 3.5f + dv * dv); // horizontally stretched blob
                    float g = Mathf.Clamp01(1f - d / 0.55f);
                    g *= g;
                    px[y * w + x] = baseCol + glow * (g * 0.85f);
                }
            }

            // Stars — most scattered, a fifth drawn as a brighter 5px twinkle.
            int stars = 1500;
            for (int i = 0; i < stars; i++)
            {
                int x = Random.Range(0, w);
                int y = Random.Range(0, h);
                float b = Random.Range(0.45f, 1f);
                var c = new Color(b, b, Mathf.Min(1f, b * 1.08f));
                AddStar(px, w, h, x, y, c);
                if (Random.value < 0.2f)
                {
                    var dim = c * 0.55f;
                    AddStar(px, w, h, x + 1, y, dim);
                    AddStar(px, w, h, x - 1, y, dim);
                    AddStar(px, w, h, x, y + 1, dim);
                    AddStar(px, w, h, x, y - 1, dim);
                }
            }

            tex.SetPixels(px);
            tex.Apply(false, false);
            return tex;
        }

        private static void AddStar(Color[] px, int w, int h, int x, int y, Color add)
        {
            if (x < 0) x += w; else if (x >= w) x -= w; // wrap horizontally
            if (y < 0 || y >= h) return;
            int idx = y * w + x;
            Color c = px[idx] + add;
            px[idx] = new Color(Mathf.Min(c.r, 1f), Mathf.Min(c.g, 1f), Mathf.Min(c.b, 1f));
        }

        // ---- Lighting ----------------------------------------------------------

        private void ApplyLighting()
        {
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.55f, 0.56f, 0.66f); // bright flat fill
            RenderSettings.fog = false;

            foreach (var l in FindObjectsByType<Light>(FindObjectsSortMode.None))
            {
                if (l.type != LightType.Directional) continue;
                l.color = new Color(0.96f, 0.96f, 1f);
                l.intensity = Mathf.Max(l.intensity, 1.15f);
                break;
            }
        }

        // ---- Character restyle -------------------------------------------------

        // Flatten/brighten every racer the moment it exists — the player on frame 1, and each bot
        // as it spawns — instead of waiting a fixed 0.4s (which left them dark at the start). A
        // HashSet keeps it idempotent; we sweep for ~1.5s to catch any late spawns, then stop.
        private readonly System.Collections.Generic.HashSet<GameObject> _styled = new System.Collections.Generic.HashSet<GameObject>();

        private IEnumerator RestyleRacersContinuously()
        {
            float deadline = Time.time + 1.5f;
            while (Time.time <= deadline)
            {
                foreach (var p in FindObjectsByType<PlayerController>(FindObjectsSortMode.None))
                    if (_styled.Add(p.gameObject)) Flatten(p.gameObject, animatorSpeed: 1.1f);
                foreach (var b in FindObjectsByType<BotController>(FindObjectsSortMode.None))
                    if (_styled.Add(b.gameObject)) Flatten(b.gameObject, animatorSpeed: 1.08f);
                yield return null;
            }
        }

        // Matte, brightened (toon-ish) look without losing the texture; keeps the rig + clips.
        private static void Flatten(GameObject root, float animatorSpeed)
        {
            var renderers = root.GetComponentsInChildren<Renderer>(true);
            for (int r = 0; r < renderers.Length; r++)
            {
                var mats = renderers[r].materials; // instances — never touches shared assets
                for (int i = 0; i < mats.Length; i++)
                {
                    var m = mats[i];
                    if (m == null) continue;
                    if (m.HasProperty("_Metallic")) m.SetFloat("_Metallic", 0f);
                    if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", 0.12f);
                    if (m.HasProperty("_Glossiness")) m.SetFloat("_Glossiness", 0.12f);
                    if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", Brighten(m.GetColor("_BaseColor")));
                    else if (m.HasProperty("_Color")) m.SetColor("_Color", Brighten(m.GetColor("_Color")));
                }
                renderers[r].materials = mats;
            }

            var anim = root.GetComponentInChildren<Animator>();
            if (anim != null) anim.speed = animatorSpeed; // peppier = "lighter"
        }

        private static Color Brighten(Color c)
        {
            // Lift toward white for a brighter, flatter cartoon palette (keep alpha).
            Color lifted = Color.Lerp(c, Color.white, 0.18f);
            lifted.a = c.a;
            return lifted;
        }
    }
}
