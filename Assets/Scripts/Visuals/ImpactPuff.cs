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

        // Soft greyish-white smoke tones — slight value spread so the cloud reads as volumetric.
        private static readonly Color SmokeLight = new Color(0.92f, 0.92f, 0.94f, 1f);
        private static readonly Color SmokeDark = new Color(0.72f, 0.73f, 0.78f, 1f);

        private Vector3 _velocity;
        private Vector3 _startScale;
        private Vector3 _endScale;
        private Material _mat;          // this puff sphere's own material instance (alpha-faded)
        private float _lifetime;

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
            bool reduced = IsReducedMotion();
            if (reduced) scaleMul *= ReducedMotionScale;

            int count = Random.Range(MinPuffs, MaxPuffs + 1);
            if (reduced) count = Mathf.Max(2, count - 2); // fewer puffs in reduced-motion mode

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
                Color tone = Color.Lerp(SmokeDark, SmokeLight, Random.value);
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
                puff.Init(mat, go.transform.localScale, vel, scaleMul);
            }
        }

        private void Init(Material mat, Vector3 startScale, Vector3 velocity, float scaleMul)
        {
            _mat = mat;
            _startScale = startScale;
            _endScale = startScale * ExpandFactor;
            _velocity = velocity;
            _lifetime = Random.Range(MinLifetime, MaxLifetime);
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

                // Ease the drift: velocity damps toward zero so the cloud settles as it fades.
                _velocity = Vector3.Lerp(_velocity, Vector3.zero, Mathf.Clamp01(Drag * dt));
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
