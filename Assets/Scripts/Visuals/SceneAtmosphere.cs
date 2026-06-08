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
    /// Per-level biomes: Race = Candy Sunset, Survival = Arctic Storm, LastStanding = Night (unchanged).
    public sealed class SceneAtmosphere : MonoBehaviour
    {
        // ---- Biome definition --------------------------------------------------

        private readonly struct BiomeParams
        {
            public readonly Color ZenithColor;
            public readonly Color HorizonColor;
            public readonly Color AmbientColor;
            public readonly Color DirectionalColor;
            public readonly float DirectionalIntensity;
            public readonly Color GroundTint;
            public readonly int StarCount;
            public readonly bool HasMoon;

            public BiomeParams(Color zenith, Color horizon, Color ambient,
                Color directional, float directionalIntensity,
                Color groundTint, int starCount, bool hasMoon)
            {
                ZenithColor = zenith; HorizonColor = horizon; AmbientColor = ambient;
                DirectionalColor = directional; DirectionalIntensity = directionalIntensity;
                GroundTint = groundTint; StarCount = starCount; HasMoon = hasMoon;
            }
        }

        private static BiomeParams For(LevelMode mode)
        {
            switch (mode)
            {
                case LevelMode.Race:
                    return new BiomeParams(
                        new Color(0.96f, 0.55f, 0.35f), new Color(1.00f, 0.82f, 0.60f),
                        new Color(0.72f, 0.58f, 0.52f),
                        new Color(1.00f, 0.95f, 0.85f), 1.1f,
                        new Color(0.95f, 0.75f, 0.60f), 0, false);
                case LevelMode.Survival:
                    return new BiomeParams(
                        new Color(0.25f, 0.40f, 0.62f), new Color(0.72f, 0.82f, 0.90f),
                        new Color(0.45f, 0.52f, 0.65f),
                        new Color(0.85f, 0.90f, 1.00f), 0.75f,
                        new Color(0.75f, 0.82f, 0.90f), 0, false);
                default: // LastStanding — matches existing ApplyLighting/BuildNightPanorama values
                    return new BiomeParams(
                        new Color(0.015f, 0.020f, 0.045f), new Color(0.040f, 0.055f, 0.095f),
                        new Color(0.36f, 0.40f, 0.50f),
                        new Color(0.66f, 0.74f, 0.95f), 0.85f,
                        new Color(0.36f, 0.40f, 0.50f), 520, true);
            }
        }

        // ---- Bootstrap ---------------------------------------------------------

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

        private static LevelMode ModeForScene(string sceneName)
        {
            if (sceneName.EndsWith("Race")) return LevelMode.Race;
            if (sceneName.EndsWith("Survival")) return LevelMode.Survival;
            return LevelMode.LastStanding;
        }

        // ---- Instance lifecycle ------------------------------------------------

        private LevelMode _mode;
        private BiomeParams _biome;

        private void Awake()
        {
            if (_instance != null && _instance != this) { Destroy(gameObject); return; }
            _instance = this;
            _mode = ModeForScene(gameObject.scene.name.Length > 0
                ? gameObject.scene.name : SceneManager.GetActiveScene().name);
            _biome = For(_mode);
            ApplyLightingFromBiome(_biome); // early — prevents one-dark-frame bug
        }

        private void OnDestroy() { if (_instance == this) _instance = null; }

        private void Start()
        {
            ApplyBiomeInternal(_mode);
            StartCoroutine(RestyleRacersContinuously());
        }

        /// Apply the art pass for the given level mode. Safe to call from external code.
        public static void ApplyBiome(LevelMode mode)
        {
            if (_instance == null) return;
            _instance.ApplyBiomeInternal(mode);
        }

        private void ApplyBiomeInternal(LevelMode mode)
        {
            _mode = mode;
            _biome = For(mode);
            ApplyLightingFromBiome(_biome);
            BrightenStaticGround();

            if (mode == LevelMode.LastStanding)
                ApplyNightSky();
            else
            {
                BuildDomeMaterial(_biome.HorizonColor, _biome.ZenithColor);
                EnforceSky();
                StartCoroutine(EnforceSkyContinuously());
            }
        }

        // ---- Sky ---------------------------------------------------------------

        private Material _skyMat;
        private Texture2D _skyTex;
        private GameObject _skydome;
        private const float SkydomeRadius = 500f;

        // Night path: procedural panorama + skybox material + skydome.
        private void ApplyNightSky()
        {
            _skyTex = BuildNightPanorama(1024, 512);
            Shader sh = Shader.Find("Skybox/Panoramic");
            if (sh != null)
            {
                _skyMat = new Material(sh);
                _skyMat.SetTexture("_MainTex", _skyTex);
                if (_skyMat.HasProperty("_Exposure")) _skyMat.SetFloat("_Exposure", 0.9f);
                RenderSettings.skybox = _skyMat;
                DynamicGI.UpdateEnvironment();
            }
            else Debug.LogWarning("[SceneAtmosphere] 'Skybox/Panoramic' not found — relying on skydome.");
            BuildDomeMaterial(Color.clear, Color.clear, useNightTex: true);
            EnforceSky();
            StartCoroutine(EnforceSkyContinuously());
        }

        // Shared skydome builder: night uses the panorama texture; daytime builds a 1x256 gradient.
        private void BuildDomeMaterial(Color horizon, Color zenith, bool useNightTex = false)
        {
            if (_skydome != null) return;
            Shader unlit = Shader.Find("Universal Render Pipeline/Unlit")
                ?? Shader.Find("Sprites/Default")
                ?? Shader.Find("Unlit/Texture");
            if (unlit == null) return;

            _skydome = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            _skydome.name = "Skydome";
            var col = _skydome.GetComponent<Collider>();
            if (col != null) Destroy(col);
            _skydome.transform.localScale = Vector3.one * (SkydomeRadius * 2f);

            Texture2D tex;
            if (useNightTex)
            {
                tex = _skyTex;
            }
            else
            {
                // 1x256 gradient: V=0 (bottom UV) = horizon, V=1 (top UV) = zenith.
                const int H = 256;
                tex = new Texture2D(1, H, TextureFormat.RGB24, false);
                tex.wrapMode = TextureWrapMode.Clamp;
                tex.filterMode = FilterMode.Bilinear;
                var gpx = new Color[H];
                for (int i = 0; i < H; i++)
                    gpx[i] = Color.Lerp(horizon, zenith, Mathf.Pow(i / (H - 1f), 0.6f));
                tex.SetPixels(gpx);
                tex.Apply(false, false);
            }

            var rend = _skydome.GetComponent<Renderer>();
            rend.shadowCastingMode = ShadowCastingMode.Off;
            rend.receiveShadows = false;
            var mat = new Material(unlit);
            if (mat.HasProperty("_BaseMap")) mat.SetTexture("_BaseMap", tex);
            mat.mainTexture = tex;
            if (mat.HasProperty("_Cull")) mat.SetFloat("_Cull", 0f);
            mat.renderQueue = (int)RenderQueue.Background;
            rend.sharedMaterial = mat;

            var cam = Camera.main;
            _skydome.transform.position = cam != null ? cam.transform.position : Vector3.zero;
        }

        private void LateUpdate()
        {
            if (_skydome == null) return;
            var cam = Camera.main;
            if (cam != null) _skydome.transform.position = cam.transform.position;
        }

        private void EnforceSky()
        {
            if (_skyMat != null && RenderSettings.skybox != _skyMat) RenderSettings.skybox = _skyMat;
            foreach (var cam in Camera.allCameras)
            {
                if (cam.clearFlags != CameraClearFlags.Skybox) cam.clearFlags = CameraClearFlags.Skybox;
                if (cam.farClipPlane < SkydomeRadius * 1.5f) cam.farClipPlane = SkydomeRadius * 1.5f;
            }
        }

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
            string sh = RenderSettings.skybox != null && RenderSettings.skybox.shader != null
                ? RenderSettings.skybox.shader.name : "<none>";
            Debug.Log($"[SceneAtmosphere] sky ready — biome={_mode}, " +
                $"material={(_skyMat != null ? "built" : "dome-only")}, skybox={sh}, cams={maxCameras}");
        }

        // ---- Night panorama (LastStanding — unchanged) -------------------------

        private static Texture2D BuildNightPanorama(int w, int h)
        {
            var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
            tex.wrapModeU = TextureWrapMode.Repeat;
            tex.wrapModeV = TextureWrapMode.Clamp;
            tex.filterMode = FilterMode.Bilinear;
            var px = new Color[w * h];
            Color zenith  = new Color(0.015f, 0.020f, 0.045f);
            Color horizon = new Color(0.040f, 0.055f, 0.095f);
            for (int y = 0; y < h; y++)
            {
                float t = Mathf.Pow(y / (h - 1f), 0.5f);
                Color c = Color.Lerp(horizon, zenith, t);
                int row = y * w;
                for (int x = 0; x < w; x++) px[row + x] = c;
            }
            AddMoon(px, w, h);
            AddStars(px, w, h);
            tex.SetPixels(px);
            tex.Apply(false, false);
            return tex;
        }

        private static void AddMoon(Color[] px, int w, int h)
        {
            float mx = 0.72f * (w - 1f), my = 0.80f * (h - 1f);
            float core = 26f, halo = 110f;
            Color moon = new Color(0.92f, 0.94f, 1.00f);
            int r = Mathf.CeilToInt(halo);
            for (int dy = -r; dy <= r; dy++)
            {
                int y = (int)my + dy;
                if (y < 0 || y >= h) continue;
                for (int dx = -r; dx <= r; dx++)
                {
                    float dist = Mathf.Sqrt(dx * dx + dy * dy);
                    float a = dist <= core
                        ? Mathf.Lerp(1f, 0.7f, dist / core)
                        : 0.7f * Mathf.Pow(Mathf.Clamp01(1f - (dist - core) / (halo - core)), 2f);
                    if (a <= 0f) continue;
                    int x = (int)mx + dx;
                    if (x < 0) x += w; else if (x >= w) x -= w;
                    px[y * w + x] = Color.Lerp(px[y * w + x], moon, a);
                }
            }
        }

        private static void AddStars(Color[] px, int w, int h)
        {
            const int stars = 520;
            for (int s = 0; s < stars; s++)
            {
                int cx = Random.Range(0, w);
                int cy = (int)(Random.Range(0.04f, 1.0f) * (h - 1));
                bool bright = Random.value < 0.06f;
                float intensity = bright ? Random.Range(0.95f, 1.0f) : Random.Range(0.45f, 0.9f);
                float radius = bright ? Random.Range(1.6f, 2.6f) : Random.Range(0.6f, 1.4f);
                Color tint = Random.value < 0.35f ? new Color(0.82f, 0.88f, 1.00f) : new Color(1.00f, 0.99f, 0.96f);
                DrawStar(px, w, h, cx, cy, radius, tint, intensity, bright);
            }
        }

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
                    if (dist <= radius) a = intensity;
                    else if (dist <= glow)
                    {
                        float fade = 1f - (dist - radius) / (glow - radius);
                        a = intensity * fade * fade * (bright ? 0.55f : 0.6f);
                    }
                    else continue;
                    int x = cx + dx;
                    if (x < 0) x += w; else if (x >= w) x -= w;
                    px[y * w + x] = Color.Lerp(px[y * w + x], tint, Mathf.Clamp01(a));
                }
            }
        }

        // ---- Lighting ----------------------------------------------------------

        private static void ApplyLightingFromBiome(BiomeParams b)
        {
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = b.AmbientColor;
            RenderSettings.fog = false;
            foreach (var l in FindObjectsByType<Light>(FindObjectsSortMode.None))
            {
                if (l.type != LightType.Directional) continue;
                l.color = b.DirectionalColor;
                l.intensity = b.DirectionalIntensity;
                break;
            }
        }

        // ---- Ground / platform candy lift -------------------------------------

        private void BrightenStaticGround()
        {
            foreach (var rend in FindObjectsByType<Renderer>(FindObjectsSortMode.None))
            {
                var go = rend.gameObject;
                if (go.layer != GameConstants.LayerGround) continue;
                if (!IsSafeGroundTag(go.tag)) continue;
                var mats = rend.materials;
                for (int i = 0; i < mats.Length; i++)
                {
                    var m = mats[i];
                    if (m == null) continue;
                    if (m.HasProperty("_Metallic"))   m.SetFloat("_Metallic", 0f);
                    if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", 0.15f);
                    if (m.HasProperty("_Glossiness")) m.SetFloat("_Glossiness", 0.15f);
                    if (m.HasProperty("_BaseColor"))  m.SetColor("_BaseColor", BrightenGround(m.GetColor("_BaseColor")));
                    else if (m.HasProperty("_Color")) m.SetColor("_Color",     BrightenGround(m.GetColor("_Color")));
                }
                rend.materials = mats;
            }
        }

        private static bool IsSafeGroundTag(string tag) =>
            tag != GameConstants.TagKillzone && tag != GameConstants.TagPushPad
            && tag != GameConstants.TagPlayer && tag != GameConstants.TagBot;

        private static Color BrightenGround(Color c)
        {
            Color.RGBToHSV(c, out float h, out float s, out float v);
            s = Mathf.Clamp01(s * 1.25f + 0.06f);
            v = Mathf.Clamp01(v * 1.10f + 0.12f);
            Color vivid = Color.HSVToRGB(h, s, v); vivid.a = c.a; return vivid;
        }

        // ---- Character restyle -------------------------------------------------

        private readonly System.Collections.Generic.HashSet<GameObject> _styled =
            new System.Collections.Generic.HashSet<GameObject>();

        private IEnumerator RestyleRacersContinuously()
        {
            float deadline = Time.time + 1.5f;
            while (Time.time <= deadline)
            {
                foreach (var p in FindObjectsByType<PlayerController>(FindObjectsSortMode.None))
                    if (_styled.Add(p.gameObject)) Flatten(p.gameObject, 1.1f);
                foreach (var b in FindObjectsByType<BotController>(FindObjectsSortMode.None))
                    if (_styled.Add(b.gameObject)) Flatten(b.gameObject, 1.08f);
                yield return null;
            }
        }

        private static Shader _toon;
        private static bool _toonResolved;

        // Target human skin tone (warm light) the dark Quaternius skin material is lifted to.
        // The Quaternius characters bake their palette into per-material _BaseColor, and the SKIN
        // material ships near-black & grayscale (~0.013, 0.013, 0.013) — even the toon gamma-lift
        // (pow(0.013, 0.45) ~= 0.11) can't pull it off black. We detect that one dark, low-saturation
        // material per model and retint it to this tone; the COLOURFUL clothing materials (navy,
        // brown, green, etc.) are higher luminance / saturation and are left untouched.
        private static readonly Color SkinTone = new Color(0.95f, 0.80f, 0.68f, 1f);

        // A material reads as "skin" when it's a dark, near-grayscale tone in the narrow band the
        // Quaternius body material ships in (~0.013). Coloured clothing (navy/brown/green) is
        // saturated and skipped; pure-black outfits (ninja ~0.008) sit below the floor and dark
        // armour (knight ~0.07) sits above the ceiling, so neither gets mistaken for skin.
        private const float SkinLumaMin = 0.009f; // below this is an intentional black outfit, not skin
        private const float SkinLumaMax = 0.05f;  // above this is dark armour/clothing, not skin
        private const float SkinSatMax  = 0.30f;  // grayscale gate — coloured darks (navy) are spared

        private static void Flatten(GameObject root, float animatorSpeed)
        {
            if (!_toonResolved) { _toon = Shader.Find("StumbleClone/Toon"); _toonResolved = true; }
            var renderers = root.GetComponentsInChildren<Renderer>(true);
            for (int r = 0; r < renderers.Length; r++)
            {
                var mats = renderers[r].materials;
                for (int i = 0; i < mats.Length; i++)
                {
                    var m = mats[i];
                    if (m == null) continue;

                    // Swap to the toon shader first, then lift the dark/grayscale skin material so
                    // ApplySkinLift can see the toon shader is active and pre-compensate its gamma
                    // lift. _BaseColor carries across the swap, so reading the original dark skin
                    // tint after the swap is fine.
                    if (_toon != null) { m.shader = _toon; ApplySkinLift(m); continue; }

                    ApplySkinLift(m);
                    if (m.HasProperty("_Metallic"))   m.SetFloat("_Metallic", 0f);
                    if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", 0.12f);
                    if (m.HasProperty("_Glossiness")) m.SetFloat("_Glossiness", 0.12f);
                    if (m.HasProperty("_BaseColor"))  m.SetColor("_BaseColor", Brighten(m.GetColor("_BaseColor")));
                    else if (m.HasProperty("_Color")) m.SetColor("_Color",     Brighten(m.GetColor("_Color")));
                }
                renderers[r].materials = mats;
            }
            var anim = root.GetComponentInChildren<Animator>();
            if (anim != null) anim.speed = animatorSpeed;
        }

        /// If the material's base colour reads as skin (very dark AND near-grayscale), retint it to
        /// the light human skin tone. No-op for colourful clothing materials. Runs after the toon
        /// shader swap; when the toon shader gamma-lifts the albedo we pre-compensate the tone so the
        /// rendered skin matches SkinTone rather than washing out.
        private static void ApplySkinLift(Material m)
        {
            bool hasBase  = m.HasProperty("_BaseColor");
            bool hasColor = !hasBase && m.HasProperty("_Color");
            if (!hasBase && !hasColor) return;

            Color c = hasBase ? m.GetColor("_BaseColor") : m.GetColor("_Color");
            if (!IsSkinTone(c)) return;

            Color target = SkinTone;
            target.a = c.a;

            // The toon shader (StumbleClone/Toon) does pow(albedo, _BrightGamma) on the base colour.
            // Pre-raise the target by the inverse gamma so the post-pow result equals SkinTone.
            if (_toon != null && m.shader == _toon && m.HasProperty("_BrightGamma"))
            {
                float gamma = Mathf.Max(0.01f, m.GetFloat("_BrightGamma"));
                float invGamma = 1f / gamma;
                target.r = Mathf.Pow(Mathf.Clamp01(target.r), invGamma);
                target.g = Mathf.Pow(Mathf.Clamp01(target.g), invGamma);
                target.b = Mathf.Pow(Mathf.Clamp01(target.b), invGamma);
            }

            if (hasBase) m.SetColor("_BaseColor", target);
            else         m.SetColor("_Color", target);
        }

        /// Skin = the dark, near-grayscale material the Quaternius models bake the body in. Coloured
        /// clothing (navy/brown/green) is either brighter or saturated, so it fails this test.
        private static bool IsSkinTone(Color c)
        {
            float luma = c.r * 0.299f + c.g * 0.587f + c.b * 0.114f;
            if (luma < SkinLumaMin || luma > SkinLumaMax) return false;
            Color.RGBToHSV(c, out _, out float s, out _);
            return s <= SkinSatMax;
        }

        private static Color Brighten(Color c)
        {
            Color.RGBToHSV(c, out float h, out float s, out float v);
            s = Mathf.Clamp01(s * 1.45f + 0.12f);
            v = Mathf.Clamp01(v * 1.12f + 0.18f);
            Color vivid = Color.HSVToRGB(h, s, v);
            Color lifted = Color.Lerp(vivid, Color.white, 0.06f);
            lifted.a = c.a;
            return lifted;
        }
    }
}
