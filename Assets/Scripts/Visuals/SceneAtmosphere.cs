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
    /// Cartoony "Stumble Guys" art direction with a NIGHT sky — a black starfield overhead,
    /// candy-saturated characters that still pop under the toon shader.
    ///  * Sky: a procedurally generated night panorama (near-black blue-black background with a
    ///    faintly-lighter band at the horizon) covered in a dense field of stars (random
    ///    white/pale-blue dots of varied size + brightness, a handful brighter) plus a soft moon
    ///    glow disc, applied as the skybox. The black-with-stars look the user asked for.
    ///  * Lighting: moonlit night — a cool dim-but-readable blue-grey ambient fill (NOT pitch
    ///    black; characters/ground stay clearly visible) plus a cool "moonlight" key light at a
    ///    lower intensity than the old warm sun.
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

        // The skybox material we build, kept so the enforce loop can re-assign it if anything
        // overrides RenderSettings.skybox after we set it.
        private Material _skyMat;
        private Texture2D _skyTex;     // the panorama, reused by the skydome backdrop
        private GameObject _skydome;   // camera-following inverted sphere — the guaranteed-visible sky
        private const float SkydomeRadius = 500f; // world units; camera far clip is bumped above this

        private void ApplySky()
        {
            _skyTex = BuildNightPanorama(1024, 512);

            Shader sh = Shader.Find("Skybox/Panoramic");
            if (sh != null)
            {
                _skyMat = new Material(sh);
                _skyMat.SetTexture("_MainTex", _skyTex);
                // Lower exposure for the night mood — the stars stay bright relative to the
                // near-black background, but the sky doesn't wash out to grey.
                if (_skyMat.HasProperty("_Exposure")) _skyMat.SetFloat("_Exposure", 0.9f);
                RenderSettings.skybox = _skyMat;
                DynamicGI.UpdateEnvironment();
            }
            else
            {
                Debug.LogWarning("[SceneAtmosphere] 'Skybox/Panoramic' not found — relying on skydome backdrop.");
            }

            // The skybox pass is unreliable under some URP camera setups (overlay/stacked cameras,
            // render-type quirks) — the symptom being a bound skybox that simply never draws. The
            // skydome is plain geometry, so it ALWAYS renders, guaranteeing a visible sky regardless.
            BuildSkydome();

            EnforceSky();
            StartCoroutine(EnforceSkyContinuously());
        }

        // A large inverted sphere textured with the panorama. Negative X scale flips the winding so
        // the (back) inner faces become front-facing and render from inside with a standard Cull-Back
        // unlit shader; the Background render queue + far depth keep it behind all scene geometry.
        private void BuildSkydome()
        {
            if (_skydome != null || _skyTex == null) return;

            // URP-NATIVE unlit shader. The legacy "Unlit/Texture" compiles but does NOT render under
            // URP — that's why the dome stayed invisible every time. Use URP Unlit, fall back to the
            // always-present Sprites/Default (also unlit + URP-safe).
            Shader unlit = Shader.Find("Universal Render Pipeline/Unlit");
            if (unlit == null) unlit = Shader.Find("Sprites/Default");
            if (unlit == null) unlit = Shader.Find("Unlit/Texture");
            if (unlit == null) return;

            _skydome = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            _skydome.name = "Skydome";
            var col = _skydome.GetComponent<Collider>();
            if (col != null) Destroy(col);

            // Positive scale + Cull Off so we see the INSIDE of the sphere from the centre, instead of
            // relying on a fragile negative-scale winding flip (the old approach that didn't render).
            _skydome.transform.localScale = Vector3.one * (SkydomeRadius * 2f);

            var rend = _skydome.GetComponent<Renderer>();
            rend.shadowCastingMode = ShadowCastingMode.Off;
            rend.receiveShadows = false;
            var mat = new Material(unlit);
            if (mat.HasProperty("_BaseMap")) mat.SetTexture("_BaseMap", _skyTex); // URP Unlit
            mat.mainTexture = _skyTex;                                            // legacy / Sprites
            if (mat.HasProperty("_Cull")) mat.SetFloat("_Cull", 0f);             // render from inside
            mat.renderQueue = (int)RenderQueue.Background; // draw before scene geometry
            rend.sharedMaterial = mat;

            var cam = Camera.main;
            _skydome.transform.position = cam != null ? cam.transform.position : Vector3.zero;
        }

        // Keep the dome centred on the camera so it reads as an infinitely distant sky and the player
        // can never reach its edge.
        private void LateUpdate()
        {
            if (_skydome == null) return;
            var cam = Camera.main;
            if (cam != null) _skydome.transform.position = cam.transform.position;
        }

        // Re-assert the skybox + camera clear flags, and make sure the far clip plane is beyond the
        // skydome so it isn't clipped away. Cheap and idempotent.
        private void EnforceSky()
        {
            if (_skyMat != null && RenderSettings.skybox != _skyMat) RenderSettings.skybox = _skyMat;
            foreach (var cam in Camera.allCameras)
            {
                if (cam.clearFlags != CameraClearFlags.Skybox) cam.clearFlags = CameraClearFlags.Skybox;
                if (cam.farClipPlane < SkydomeRadius * 1.5f) cam.farClipPlane = SkydomeRadius * 1.5f;
            }
        }

        // ApplySky used to run exactly once in Start(). A camera that becomes enabled or is created
        // AFTER that single frame (or any code that reassigns RenderSettings.skybox) would then keep
        // a flat/solid-colour clear — i.e. "no background". Re-assert every frame for a short window
        // so any late camera is caught, then log a one-line diagnostic of the final state.
        private IEnumerator EnforceSkyContinuously()
        {
            float deadline = Time.time + 2.5f;
            int maxCameras = 0;
            while (Time.time <= deadline)
            {
                EnforceSky();
                maxCameras = Mathf.Max(maxCameras, Camera.allCameras.Length);
                yield return null;
            }

            string boundShader = RenderSettings.skybox != null && RenderSettings.skybox.shader != null
                ? RenderSettings.skybox.shader.name : "<none>";
            Debug.Log($"[SceneAtmosphere] sky ready — material={(_skyMat != null ? "built" : "MISSING")}, " +
                      $"RenderSettings.skybox shader={boundShader}, cameras seen={maxCameras}");
        }

        // Equirectangular (lat-long) panorama: a NIGHT sky — near-black blue-black background with a
        // faintly-lighter band hugging the horizon, a dense starfield, and a soft moon glow disc.
        // u wraps seamlessly; v runs 0 (horizon) → 1 (zenith). "The black one with stars."
        private static Texture2D BuildNightPanorama(int w, int h)
        {
            var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
            tex.wrapModeU = TextureWrapMode.Repeat;  // horizontal wraps seamlessly
            tex.wrapModeV = TextureWrapMode.Clamp;
            tex.filterMode = FilterMode.Bilinear;

            var px = new Color[w * h];
            // Very dark blue-black overhead; the horizon band is only a touch lighter so the dome
            // still reads as a deep night sky rather than washing out to grey.
            Color zenith  = new Color(0.015f, 0.020f, 0.045f); // near-black blue-black at the zenith
            Color horizon = new Color(0.040f, 0.055f, 0.095f); // subtle slightly-lighter horizon band

            for (int y = 0; y < h; y++)
            {
                float v = y / (h - 1f);
                // Keep the horizon lift confined to the lowest slice; the bulk of the dome is black.
                float t = Mathf.Pow(v, 0.5f);
                Color baseCol = Color.Lerp(horizon, zenith, t);
                int row = y * w;
                for (int x = 0; x < w; x++) px[row + x] = baseCol;
            }

            AddMoon(px, w, h);
            AddStars(px, w, h);

            tex.SetPixels(px);
            tex.Apply(false, false);
            return tex;
        }

        // A soft moon glow disc: a bright pale-white core fading into a wide cool halo, sat high in
        // the sky off to one side. Additive toward white so it lifts the black behind it.
        private static void AddMoon(Color[] px, int w, int h)
        {
            float mx = 0.72f * (w - 1f);  // moon centre in pixels (high, off to one side)
            float my = 0.80f * (h - 1f);
            float core = 26f;             // bright disc radius
            float halo = 110f;            // soft glow radius
            Color moon = new Color(0.92f, 0.94f, 1.00f); // cool pale moonlight

            int r = Mathf.CeilToInt(halo);
            for (int dy = -r; dy <= r; dy++)
            {
                int y = (int)my + dy;
                if (y < 0 || y >= h) continue;
                for (int dx = -r; dx <= r; dx++)
                {
                    float dist = Mathf.Sqrt(dx * dx + dy * dy);
                    // Bright solid-ish core, then a smooth halo falloff that fades to nothing.
                    float a;
                    if (dist <= core) a = Mathf.Lerp(1f, 0.7f, dist / core);
                    else
                    {
                        float hd = Mathf.Clamp01(1f - (dist - core) / (halo - core));
                        a = 0.7f * hd * hd; // quadratic falloff = soft glow
                    }
                    if (a <= 0f) continue;
                    int x = (int)mx + dx;
                    if (x < 0) x += w; else if (x >= w) x -= w; // wrap horizontally
                    int idx = y * w + x;
                    px[idx] = Color.Lerp(px[idx], moon, a);
                }
            }
        }

        // A dense field of stars: a few hundred small white/pale-blue dots of varied brightness and
        // size, plus a handful of brighter "beacon" stars with a tiny glow. Each star is drawn as a
        // crisp filled dot (clearly a star, not noise) over the black background.
        private static void AddStars(Color[] px, int w, int h)
        {
            const int stars = 520; // a few hundred, dense across the whole dome
            for (int s = 0; s < stars; s++)
            {
                int cx = Random.Range(0, w);
                // Bias slightly toward the upper sky but still seed plenty near the horizon.
                int cy = (int)(Random.Range(0.04f, 1.0f) * (h - 1));

                // Most stars are dim+tiny; a handful are bright beacons with a small glow.
                bool bright = Random.value < 0.06f;          // ~30 brighter stars
                float intensity = bright ? Random.Range(0.95f, 1.0f) : Random.Range(0.45f, 0.9f);
                float radius = bright ? Random.Range(1.6f, 2.6f) : Random.Range(0.6f, 1.4f);

                // Slight white/pale-blue colour variation per star.
                Color tint = Random.value < 0.35f
                    ? new Color(0.82f, 0.88f, 1.00f)  // pale blue
                    : new Color(1.00f, 0.99f, 0.96f); // warm white
                DrawStar(px, w, h, cx, cy, radius, tint, intensity, bright);
            }
        }

        // One star: a bright dot core with a 1px soft edge so it stays crisp at panorama scale.
        // Bright beacons get an extra faint glow ring so they read as standout stars.
        private static void DrawStar(Color[] px, int w, int h, int cx, int cy, float radius, Color tint, float intensity, bool bright)
        {
            float glow = bright ? radius * 2.6f : radius + 0.8f;
            int r = Mathf.CeilToInt(glow);
            for (int dy = -r; dy <= r; dy++)
            {
                int y = cy + dy;
                if (y < 0 || y >= h) continue;
                for (int dx = -r; dx <= r; dx++)
                {
                    float dist = Mathf.Sqrt(dx * dx + dy * dy);
                    float a;
                    if (dist <= radius) a = intensity;                       // crisp filled core
                    else if (dist <= glow)                                   // soft edge / glow ring
                    {
                        float fade = 1f - (dist - radius) / (glow - radius);
                        a = intensity * fade * fade * (bright ? 0.55f : 0.6f);
                    }
                    else continue;
                    int x = cx + dx;
                    if (x < 0) x += w; else if (x >= w) x -= w; // wrap horizontally
                    int idx = y * w + x;
                    px[idx] = Color.Lerp(px[idx], tint, Mathf.Clamp01(a));
                }
            }
        }

        // ---- Lighting ----------------------------------------------------------

        private void ApplyLighting()
        {
            RenderSettings.ambientMode = AmbientMode.Flat;
            // Cool moonlit fill — dim but READABLE blue-grey (~0.42 brightness), NOT pitch black, so
            // the toon-shaded characters and ground stay clearly visible against the night sky.
            RenderSettings.ambientLight = new Color(0.36f, 0.40f, 0.50f);
            RenderSettings.fog = false;

            foreach (var l in FindObjectsByType<Light>(FindObjectsSortMode.None))
            {
                if (l.type != LightType.Directional) continue;
                l.color = new Color(0.66f, 0.74f, 0.95f);       // cool blue-white moonlight
                l.intensity = 0.85f;                            // lower than the old warm sun
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
