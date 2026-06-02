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
    /// Bright, cartoony "Stumble Guys" art direction — sunny daytime, candy-saturated colours.
    ///  * Sky: a procedurally generated daytime gradient panorama (light sky-blue overhead →
    ///    pale near-white at the horizon) with soft fluffy white clouds and a gentle sun glow,
    ///    applied as the skybox. No stars, nothing dark.
    ///  * Lighting: warm, sunny daylight — a bright slightly-warm ambient fill plus a warm-white
    ///    key light at higher intensity (a sunny afternoon, not a night club).
    ///  * Characters: flattens the player + bots to a matte, vivid candy-toy look while keeping
    ///    their textures and rigged animations; boosts colour saturation/brightness and bumps
    ///    animator speed slightly for a lighter, peppier feel.
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
            BrightenStaticGround();
            StartCoroutine(RestyleRacersContinuously());
        }

        // ---- Sky ---------------------------------------------------------------

        private void ApplySky()
        {
            Shader sh = Shader.Find("Skybox/Panoramic");
            if (sh != null)
            {
                Texture2D tex = BuildDaytimePanorama(1024, 512);
                var mat = new Material(sh);
                mat.SetTexture("_MainTex", tex);
                if (mat.HasProperty("_Exposure")) mat.SetFloat("_Exposure", 1.25f);
                RenderSettings.skybox = mat;
                DynamicGI.UpdateEnvironment();
            }

            foreach (var cam in Camera.allCameras)
                cam.clearFlags = CameraClearFlags.Skybox;
        }

        // Equirectangular (lat-long) panorama: bright daytime gradient + sun glow + fluffy clouds.
        // v runs 0 (horizon) → 1 (overhead). No stars, nothing dark.
        private static Texture2D BuildDaytimePanorama(int w, int h)
        {
            var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
            tex.wrapModeU = TextureWrapMode.Repeat;  // horizontal wraps seamlessly
            tex.wrapModeV = TextureWrapMode.Clamp;
            tex.filterMode = FilterMode.Bilinear;

            var px = new Color[w * h];
            Color horizon = new Color(0.93f, 0.96f, 0.99f); // pale near-white at the horizon
            Color zenith  = new Color(0.36f, 0.66f, 0.97f); // cheerful light sky-blue overhead
            Color sun     = new Color(1.00f, 0.98f, 0.86f); // soft warm sun glow

            const float sx = 0.70f, sy = 0.78f; // sun centre in uv (high, off to one side)
            for (int y = 0; y < h; y++)
            {
                float v = y / (h - 1f);
                // Ease the gradient so most of the dome is blue and the pale band hugs the horizon.
                float t = Mathf.Pow(v, 0.65f);
                Color baseCol = Color.Lerp(horizon, zenith, t);
                for (int x = 0; x < w; x++)
                {
                    float du = (x / (w - 1f)) - sx;
                    float dv = v - sy;
                    float d = Mathf.Sqrt(du * du * 2.2f + dv * dv); // horizontally stretched glow
                    float g = Mathf.Clamp01(1f - d / 0.45f);
                    g *= g;
                    Color c = baseCol + sun * (g * 0.55f);
                    px[y * w + x] = new Color(Mathf.Min(c.r, 1f), Mathf.Min(c.g, 1f), Mathf.Min(c.b, 1f));
                }
            }

            AddClouds(px, w, h);

            tex.SetPixels(px);
            tex.Apply(false, false);
            return tex;
        }

        // Soft fluffy white clouds: scattered blobs, each a cluster of overlapping radial puffs.
        // Clouds sit in the lower-to-mid sky band so they read against the blue without crowding
        // the zenith.
        private static void AddClouds(Color[] px, int w, int h)
        {
            const int clouds = 26;
            for (int c = 0; c < clouds; c++)
            {
                int cx = Random.Range(0, w);
                int cy = (int)(Random.Range(0.20f, 0.62f) * (h - 1)); // low/mid sky band
                int puffs = Random.Range(4, 8);
                float scale = Random.Range(0.7f, 1.5f);
                for (int p = 0; p < puffs; p++)
                {
                    int ox = cx + (int)(Random.Range(-34f, 34f) * scale);
                    int oy = cy + (int)(Random.Range(-10f, 10f) * scale);
                    float radius = Random.Range(16f, 30f) * scale;
                    AddPuff(px, w, h, ox, oy, radius);
                }
            }
        }

        // One soft white radial puff, blended additively-toward-white with a smooth falloff.
        private static void AddPuff(Color[] px, int w, int h, int cx, int cy, float radius)
        {
            int r = Mathf.CeilToInt(radius);
            float inv = 1f / radius;
            for (int dy = -r; dy <= r; dy++)
            {
                int y = cy + dy;
                if (y < 0 || y >= h) continue;
                for (int dx = -r; dx <= r; dx++)
                {
                    float dist = Mathf.Sqrt(dx * dx + dy * dy) * inv;
                    if (dist >= 1f) continue;
                    float a = 1f - dist;
                    a = a * a * 0.9f; // smooth, soft edge
                    int x = cx + dx;
                    if (x < 0) x += w; else if (x >= w) x -= w; // wrap horizontally
                    int idx = y * w + x;
                    px[idx] = Color.Lerp(px[idx], Color.white, a);
                }
            }
        }

        // ---- Lighting ----------------------------------------------------------

        private void ApplyLighting()
        {
            RenderSettings.ambientMode = AmbientMode.Flat;
            // Bright, slightly-warm fill — a sunny afternoon sky bounce, not a dim default.
            RenderSettings.ambientLight = new Color(0.78f, 0.80f, 0.74f);
            RenderSettings.fog = false;

            foreach (var l in FindObjectsByType<Light>(FindObjectsSortMode.None))
            {
                if (l.type != LightType.Directional) continue;
                l.color = new Color(1f, 0.97f, 0.88f);          // warm-white sunlight
                l.intensity = Mathf.Max(l.intensity, 1.45f);    // bright key light
                break;
            }
        }

        // ---- Ground / platform candy lift -------------------------------------

        // Once, lift the ground + static platforms toward a brighter candy palette to support the
        // Stumble-Guys look. Strictly opt-IN by the Ground layer and explicitly skips anything on
        // an Obstacle / Killzone layer or carrying a Player/Bot/PushPad/Killzone tag, so it never
        // touches obstacle, telegraph, hazard, push-pad, or racer objects.
        private void BrightenStaticGround()
        {
            foreach (var rend in FindObjectsByType<Renderer>(FindObjectsSortMode.None))
            {
                var go = rend.gameObject;
                if (go.layer != GameConstants.LayerGround) continue;   // only floor/platform surfaces
                if (!IsSafeGroundTag(go.tag)) continue;                // skip hazard/racer tags defensively

                var mats = rend.materials; // instances — never touches shared assets
                for (int i = 0; i < mats.Length; i++)
                {
                    var m = mats[i];
                    if (m == null) continue;
                    if (m.HasProperty("_Metallic")) m.SetFloat("_Metallic", 0f);
                    if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", 0.15f);
                    if (m.HasProperty("_Glossiness")) m.SetFloat("_Glossiness", 0.15f);
                    if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", BrightenGround(m.GetColor("_BaseColor")));
                    else if (m.HasProperty("_Color")) m.SetColor("_Color", BrightenGround(m.GetColor("_Color")));
                }
                rend.materials = mats;
            }
        }

        // Reject the tags used by hazards / racers; everything else on the Ground layer is fair game.
        private static bool IsSafeGroundTag(string tag)
        {
            return tag != GameConstants.TagKillzone
                && tag != GameConstants.TagPushPad
                && tag != GameConstants.TagPlayer
                && tag != GameConstants.TagBot;
        }

        // Gentler than the racer lift — saturate and brighten the floor without making it glaring.
        private static Color BrightenGround(Color c)
        {
            Color.RGBToHSV(c, out float hue, out float sat, out float val);
            sat = Mathf.Clamp01(sat * 1.25f + 0.06f);
            val = Mathf.Clamp01(val * 1.10f + 0.12f);
            Color vivid = Color.HSVToRGB(hue, sat, val);
            vivid.a = c.a;
            return vivid;
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

        // Resolved once: the cel/outline shader. Null only if it was stripped from the build
        // (ToonTools.EnsureAlwaysIncludedShaders prevents that), in which case we fall back to the
        // old matte+brighten so characters still look styled rather than broken.
        private static Shader _toon;
        private static bool _toonResolved;

        // Full cartoon pass: swap each character material to the cel+outline toon shader (which
        // also brightens/saturates the model's dark palette in-shader), keeping the rig + clips.
        private static void Flatten(GameObject root, float animatorSpeed)
        {
            if (!_toonResolved) { _toon = Shader.Find("StumbleClone/Toon"); _toonResolved = true; }

            var renderers = root.GetComponentsInChildren<Renderer>(true);
            for (int r = 0; r < renderers.Length; r++)
            {
                var mats = renderers[r].materials; // instances — never touches shared assets
                for (int i = 0; i < mats.Length; i++)
                {
                    var m = mats[i];
                    if (m == null) continue;

                    if (_toon != null)
                    {
                        // Cel shading + black outline + brighten/saturate, all in-shader. Keep the
                        // material's _BaseColor (the toon shader pops the dark palette itself).
                        m.shader = _toon;
                        continue;
                    }

                    // Fallback: matte + brighten on the stock shader.
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
            // Push toward a vivid candy-toy palette: boost saturation and value far more than the
            // old subtle white-lift, but only nudge toward white so colours stay saturated, not pastel.
            Color.RGBToHSV(c, out float hue, out float sat, out float val);
            sat = Mathf.Clamp01(sat * 1.45f + 0.12f); // vivid, candy-saturated
            val = Mathf.Clamp01(val * 1.12f + 0.18f); // bright, but keep the hue's punch
            Color vivid = Color.HSVToRGB(hue, sat, val);
            Color lifted = Color.Lerp(vivid, Color.white, 0.06f); // tiny lift only
            lifted.a = c.a;
            return lifted;
        }
    }
}
