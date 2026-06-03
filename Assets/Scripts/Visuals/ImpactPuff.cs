using System.Collections;
using StumbleClone.Game;
using StumbleClone.Obstacles;
using UnityEngine;

namespace StumbleClone.Visuals
{
    /// A cheap, self-contained "poof" of greyish-white smoke spawned when a racer is knocked out.
    ///
    /// WebGL-safe by construction: it deliberately does NOT use a ParticleSystem or any
    /// runtime-only shader/material (WebGL strips those and the effect would render invisibly or
    /// pink). Instead it mirrors the proven pattern already shipping in <see cref="Powerup"/>:
    /// a handful of primitive sphere meshes given a <see cref="RuntimeMaterial"/> (URP/Lit, which
    /// is guaranteed to be in the build because baked geometry uses it). Each puff sphere drifts
    /// outward + upward, expands slightly, and fades its material alpha to zero, then destroys
    /// itself — keeping the whole effect within the project's &lt;500 draw-call budget.
    ///
    /// Use the static <see cref="Spawn"/> entry point; there is no need to wire anything in the
    /// scene. <see cref="EliminationFx"/> calls it on every elimination.
    public sealed class ImpactPuff : MonoBehaviour
    {
        // ---- Tuning (local — effect-specific feel) ----
        private const int MinPuffs = 4;
        private const int MaxPuffs = 6;                 // inclusive; keep small for the draw-call budget
        private const float BaseScale = 0.45f;          // starting diameter of each smoke sphere
        private const float ScaleJitter = 0.25f;        // ± fraction of BaseScale per sphere
        private const float ExpandFactor = 1.8f;        // grows to this × its start scale over its life
        private const float OutwardSpeed = 1.6f;        // initial horizontal drift speed (m/s)
        private const float UpwardSpeed = 1.1f;         // initial vertical drift speed (m/s)
        private const float Drag = 2.4f;                // velocity damping per second (eased settle)
        private const float MinLifetime = 0.45f;
        private const float MaxLifetime = 0.6f;
        private const float ReducedMotionScale = 0.6f;  // smaller puff when ReducedMotion is on

        // ---- Confetti tuning (a brighter, livelier celebration variant) ----
        private const int MinConfetti = 14;
        private const int MaxConfetti = 20;             // inclusive; still tiny meshes, draw-call cheap
        private const float ConfettiScale = 0.16f;      // much smaller "bits" than smoke puffs
        private const float ConfettiScaleJitter = 0.4f; // ± fraction of ConfettiScale per bit
        private const float ConfettiSpread = 0.5f;      // initial scatter radius around the burst point
        private const float ConfettiOutward = 2.4f;     // initial horizontal launch speed (m/s)
        private const float ConfettiPop = 4.0f;         // extra upward pop on launch (m/s)
        private const float ConfettiGravity = 7.0f;     // downward accel so bits arc up then fall
        private const float ConfettiMinLifetime = 0.9f;
        private const float ConfettiMaxLifetime = 1.4f;
        private const float ConfettiReducedScale = 0.7f;

        // Soft greyish-white smoke tones — slight value spread so the cloud reads as volumetric.
        private static readonly Color SmokeLight = new Color(0.92f, 0.92f, 0.94f, 1f);
        private static readonly Color SmokeDark = new Color(0.72f, 0.73f, 0.78f, 1f);

        // Bright party palette for confetti — pulls UITheme-ish hues (pink/purple/gold/green/cyan)
        // so the shower reads as celebratory against the navy victory backdrop.
        private static readonly Color[] ConfettiColors =
        {
            new Color(0.925f, 0.282f, 0.600f, 1f), // pink   (UITheme.Primary)
            new Color(0.545f, 0.361f, 0.965f, 1f), // purple (UITheme.Secondary)
            new Color(1.000f, 0.823f, 0.302f, 1f), // gold   (UITheme.Gold)
            new Color(0.133f, 0.773f, 0.369f, 1f), // green  (UITheme.Success)
            new Color(0.150f, 0.850f, 1.000f, 1f), // cyan
        };

        private Vector3 _velocity;
        private Vector3 _startScale;
        private Vector3 _endScale;
        private Material _mat;          // this puff sphere's own material instance (alpha-faded)
        private float _lifetime;
        private float _gravity;         // 0 for smoke; >0 for confetti so bits arc up then fall

        /// Spawn a small smoke cloud at <paramref name="position"/>. Instantiates a few collider-less
        /// sphere primitives, each with its own greyish-white <see cref="RuntimeMaterial"/>, and lets
        /// them drift, expand, and fade out before destroying themselves.
        ///
        /// Honors <see cref="SettingsStore.ReducedMotion"/>: when enabled the cloud is smaller and
        /// uses fewer spheres (gentler, vestibular-friendly). The call is a no-op-safe one-liner —
        /// safe to invoke from event handlers.
        /// <param name="position">World point to puff at (e.g. slightly above the racer's feet).</param>
        /// <param name="scaleMul">Optional overall size multiplier (default 1).</param>
        public static void Spawn(Vector3 position, float scaleMul = 1f)
        {
            Spawn(position, null, scaleMul);
        }

        /// Tinted overload: same drifting/fading smoke cloud, but each sphere is colored toward
        /// <paramref name="tint"/> (lerped between a darker and lighter shade of it) instead of
        /// greyish-white. Used for the power-up collect poof so the puff matches the pickup's color.
        /// Passing a null tint reproduces the default smoke look.
        /// <param name="position">World point to puff at.</param>
        /// <param name="tint">Optional color the cloud should read as (null = greyish smoke).</param>
        /// <param name="scaleMul">Optional overall size multiplier (default 1).</param>
        public static void Spawn(Vector3 position, Color? tint, float scaleMul = 1f)
        {
            bool reduced = IsReducedMotion();
            if (reduced) scaleMul *= ReducedMotionScale;

            int count = Random.Range(MinPuffs, MaxPuffs + 1);
            if (reduced) count = Mathf.Max(2, count - 2); // fewer puffs in reduced-motion mode

            // Build the light/dark tone pair once: either the smoke greys or shades of the tint.
            Color toneDark = SmokeDark, toneLight = SmokeLight;
            if (tint.HasValue)
            {
                Color c = tint.Value;
                toneDark = Color.Lerp(c, Color.black, 0.25f); toneDark.a = 1f;
                toneLight = Color.Lerp(c, Color.white, 0.35f); toneLight.a = 1f;
            }

            for (int i = 0; i < count; i++)
            {
                var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                go.name = "ImpactPuff";

                // Strip the auto-added collider — this is pure decoration, never physical.
                var col = go.GetComponent<Collider>();
                if (col != null) Destroy(col);

                // A tiny initial spread around the spawn point so the spheres don't all stack.
                Vector3 offset = Random.insideUnitSphere * (BaseScale * 0.4f * scaleMul);
                offset.y = Mathf.Abs(offset.y) * 0.5f; // bias slightly upward, never below feet
                go.transform.position = position + offset;

                float startDiameter = BaseScale * scaleMul * (1f + Random.Range(-ScaleJitter, ScaleJitter));
                go.transform.localScale = Vector3.one * Mathf.Max(0.05f, startDiameter);

                // Own material instance per sphere — slight light/dark mix + per-sphere alpha fade.
                Color tone = Color.Lerp(toneDark, toneLight, Random.value);
                var mat = RuntimeMaterial.Make(tone);
                MakeTransparent(mat); // so the alpha fade is actually visible in URP/Lit
                var r = go.GetComponent<Renderer>();
                if (r != null) r.sharedMaterial = mat;

                // Outward+upward launch velocity: a fanned cone away from the impact point.
                Vector2 dir = Random.insideUnitCircle.normalized;
                if (dir == Vector2.zero) dir = Vector2.up;
                Vector3 vel = new Vector3(dir.x, 0f, dir.y) * (OutwardSpeed * scaleMul)
                              + Vector3.up * (UpwardSpeed * scaleMul * Random.Range(0.7f, 1.2f));

                var puff = go.AddComponent<ImpactPuff>();
                puff.Init(mat, go.transform.localScale, vel, ExpandFactor, 0f,
                          Random.Range(MinLifetime, MaxLifetime));
            }
        }

        /// Fire a colorful confetti burst at <paramref name="position"/> — a celebratory variant of
        /// <see cref="Spawn"/>. Launches many smaller, multi-colored bits with a bigger upward pop
        /// that then arc back down under a faux gravity, all using the same WebGL-safe
        /// <see cref="RuntimeMaterial"/> + alpha-fade pattern (no ParticleSystem, no runtime shaders).
        ///
        /// Honors <see cref="SettingsStore.ReducedMotion"/>: fewer, gentler bits when enabled.
        /// Keeps within the &lt;500 draw-call budget — even the high end is ~20 tiny meshes that
        /// destroy themselves within ~1.4s.
        /// <param name="position">World point to burst from (e.g. above the winner's head).</param>
        /// <param name="scaleMul">Optional overall size multiplier (default 1).</param>
        public static void Confetti(Vector3 position, float scaleMul = 1f)
        {
            bool reduced = IsReducedMotion();
            if (reduced) scaleMul *= ConfettiReducedScale;

            int count = Random.Range(MinConfetti, MaxConfetti + 1);
            if (reduced) count = Mathf.Max(6, count / 2); // fewer bits in reduced-motion mode

            for (int i = 0; i < count; i++)
            {
                var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                go.name = "Confetti";

                // Strip the auto-added collider — pure decoration, never physical.
                var col = go.GetComponent<Collider>();
                if (col != null) Destroy(col);

                // Tight scatter around the burst point so the bits launch as a cluster.
                Vector3 offset = Random.insideUnitSphere * (ConfettiSpread * scaleMul);
                go.transform.position = position + offset;

                float bit = ConfettiScale * scaleMul * (1f + Random.Range(-ConfettiScaleJitter, ConfettiScaleJitter));
                go.transform.localScale = Vector3.one * Mathf.Max(0.03f, bit);

                // Bright, varied per-bit color — own transparent material instance for the fade.
                Color tone = ConfettiColors[Random.Range(0, ConfettiColors.Length)];
                var mat = RuntimeMaterial.Make(tone);
                MakeTransparent(mat);
                var r = go.GetComponent<Renderer>();
                if (r != null) r.sharedMaterial = mat;

                // Fountain launch: fanned outward + a strong upward pop; gravity (below) pulls it back.
                Vector2 dir = Random.insideUnitCircle.normalized;
                if (dir == Vector2.zero) dir = Vector2.up;
                Vector3 vel = new Vector3(dir.x, 0f, dir.y) * (ConfettiOutward * scaleMul * Random.Range(0.5f, 1f))
                              + Vector3.up * (ConfettiPop * scaleMul * Random.Range(0.7f, 1.2f));

                // Confetti bits don't expand (1× = constant size) and fall under faux gravity.
                var puff = go.AddComponent<ImpactPuff>();
                puff.Init(mat, go.transform.localScale, vel, 1f, ConfettiGravity * scaleMul,
                          Random.Range(ConfettiMinLifetime, ConfettiMaxLifetime));
            }
        }

        private void Init(Material mat, Vector3 startScale, Vector3 velocity, float expandFactor,
                          float gravity, float lifetime)
        {
            _mat = mat;
            _startScale = startScale;
            _endScale = startScale * expandFactor;
            _velocity = velocity;
            _gravity = gravity;
            _lifetime = lifetime;
            StartCoroutine(Animate());
        }

        // Drift + expand + fade over its randomized lifetime, then clean up the GameObject and its
        // material. All cheap: no per-frame allocations, one coroutine per sphere.
        private IEnumerator Animate()
        {
            float t = 0f;
            Transform tr = transform;
            while (t < _lifetime)
            {
                float dt = Time.deltaTime;
                t += dt;
                float k = Mathf.Clamp01(t / _lifetime);

                if (_gravity > 0f)
                {
                    // Confetti: arc up then fall — apply faux gravity, no settle damping.
                    _velocity.y -= _gravity * dt;
                }
                else
                {
                    // Smoke: ease the drift so the cloud settles toward zero as it fades.
                    _velocity = Vector3.Lerp(_velocity, Vector3.zero, Mathf.Clamp01(Drag * dt));
                }
                tr.position += _velocity * dt;

                // Gentle expansion (smoothstep so it puffs fast then eases).
                float e = k * k * (3f - 2f * k);
                tr.localScale = Vector3.Lerp(_startScale, _endScale, e);

                // Fade alpha to 0 — mirrors Powerup's collect-fade (drives _BaseColor.a).
                if (_mat != null && _mat.HasProperty("_BaseColor"))
                {
                    Color c = _mat.GetColor("_BaseColor");
                    c.a = 1f - k;
                    _mat.SetColor("_BaseColor", c);
                }

                yield return null;
            }

            if (_mat != null) Destroy(_mat);
            Destroy(gameObject);
        }

        // Switch a URP/Lit material to its Transparent surface mode so reducing _BaseColor.a
        // actually fades the sphere out (the default Opaque mode ignores alpha). Property names
        // match URP/Lit's surface setup; each is guarded so a Standard fallback won't throw.
        private static void MakeTransparent(Material m)
        {
            if (m == null) return;
            if (m.HasProperty("_Surface")) m.SetFloat("_Surface", 1f); // 1 = Transparent
            if (m.HasProperty("_Blend")) m.SetFloat("_Blend", 0f);     // 0 = Alpha blend
            if (m.HasProperty("_SrcBlend")) m.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            if (m.HasProperty("_DstBlend")) m.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            if (m.HasProperty("_ZWrite")) m.SetFloat("_ZWrite", 0f);
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            m.DisableKeyword("_ALPHATEST_ON");
            m.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            m.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
        }

        // Read ReducedMotion defensively. The setting lives in StumbleClone.Game.SettingsStore,
        // which is referenced directly; the try/catch is belt-and-suspenders in case the static
        // initializer is ever unavailable (e.g. in a stripped test build).
        private static bool IsReducedMotion()
        {
            try { return SettingsStore.ReducedMotion; }
            catch { return false; }
        }
    }
}
