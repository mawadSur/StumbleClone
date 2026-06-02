using UnityEngine;
using StumbleClone.Game;

namespace StumbleClone.Obstacles
{
    /// A ground marker placed at a rim octant a beat before a hazard arrives, so the player
    /// can "read" the incoming direction. Pulses yellow→red over its lifetime, then removes
    /// itself exactly as the hazard spawns. Built from a thin primitive disc (opaque, no
    /// transparency setup) so it renders reliably in URP with zero art. Spawned by
    /// ObstacleSpawner; no scene wiring.
    ///
    /// Accessibility: the marker carries a non-colour cue as well as the yellow→red ramp — a
    /// bold dark outline rim plus a concentric dark ring, layered as thin stacked discs, so the
    /// disc stays distinguishable for colour-blind players (shape, not hue). When
    /// SettingsStore.HighContrastTelegraphs is on, the rim/ring go darker and thicker and the
    /// pulsing fill is brightened for a stronger, higher-contrast read.
    public sealed class TelegraphIndicator : MonoBehaviour
    {
        private float _life;
        private float _age;
        private Renderer _rend;
        private Renderer _centerRend;
        private MaterialPropertyBlock _mpb;
        private bool _highContrast;
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int EmissionId = Shader.PropertyToID("_EmissionColor");

        private static readonly Color Warn = new Color(1f, 0.85f, 0.1f);   // early: yellow
        private static readonly Color Danger = new Color(1f, 0.12f, 0.08f); // late:  red

        // Non-colour shape cue: a near-black rim/ring layered under and within the fill disc.
        private static readonly Color Outline = new Color(0.04f, 0.04f, 0.05f);

        // Fraction of the diameter occupied by the fill (inside the outer rim) and by the
        // concentric inner ring. Tuned so the dark bands read clearly from the player camera.
        private const float FillScale = 0.82f;       // fill disc vs outer rim
        private const float InnerRingScale = 0.52f;  // dark concentric ring
        private const float CenterScale = 0.34f;     // fill core inside the ring

        // High-contrast widens the dark bands (smaller fill/center) for a bolder outline.
        private const float HcFillScale = 0.74f;
        private const float HcCenterScale = 0.30f;

        public static void Spawn(Vector3 rimPoint, float groundY, float life, float diameter)
        {
            bool highContrast = SettingsStore.HighContrastTelegraphs;

            var go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            go.name = "Telegraph";
            var col = go.GetComponent<Collider>();
            if (col != null) Destroy(col); // visual only — never blocks gameplay
            // Thin disc lying flat just above the floor. This outermost disc is the dark rim;
            // the pulsing fill sits on a slightly smaller disc stacked just above it.
            go.transform.position = new Vector3(rimPoint.x, groundY + 0.05f, rimPoint.z);
            go.transform.localScale = new Vector3(diameter, 0.02f, diameter);

            // URP/Lit base for the dark outer rim (not emissive — it must stay dark to contrast).
            RuntimeMaterial.Apply(go, Outline, emissive: false);

            float fillScale = highContrast ? HcFillScale : FillScale;
            float centerScale = highContrast ? HcCenterScale : CenterScale;

            // Stacked concentric discs (top-down: dark rim → fill ring → dark ring → fill core),
            // giving a clear shape/pattern cue that survives without colour. Each sits a hair
            // higher than the last so the top-down z-order is unambiguous in URP.
            var fill = MakeDisc(go.transform, "Fill", fillScale, 1, Warn, emissive: true);
            MakeDisc(go.transform, "InnerRing", InnerRingScale, 2, Outline, emissive: false);
            var center = MakeDisc(go.transform, "Center", centerScale, 3, Warn, emissive: true);

            var ti = go.AddComponent<TelegraphIndicator>();
            ti._life = Mathf.Max(0.1f, life);
            ti._highContrast = highContrast;
            // Drive both pulsing fill layers (the rim/ring are static dark) from one property block.
            ti._rend = fill.GetComponent<Renderer>();
            ti._centerRend = center.GetComponent<Renderer>();
            ti._mpb = new MaterialPropertyBlock();
        }

        /// Build a flat child disc as a fraction of the parent's footprint, stacked at the given
        /// layer (1 = just above the rim) so concentric bands resolve top-down. Returns the disc.
        private static GameObject MakeDisc(Transform parent, string name, float scale, int layer,
            Color color, bool emissive)
        {
            var disc = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            disc.name = name;
            var col = disc.GetComponent<Collider>();
            if (col != null) Destroy(col);
            disc.transform.SetParent(parent, false);
            // Parent is already scaled (diameter, 0.02, diameter); children use local fractions.
            // A tiny per-layer Y offset keeps each band drawing above the one beneath it.
            disc.transform.localScale = new Vector3(scale, 1.2f, scale);
            disc.transform.localPosition = new Vector3(0f, layer * 0.4f, 0f);
            RuntimeMaterial.Apply(disc, color, emissive);
            return disc;
        }

        private void Update()
        {
            _age += Time.deltaTime;
            float t = Mathf.Clamp01(_age / _life);

            // Colour ramps yellow→red; brightness pulses faster as the hazard nears.
            float pulse = 0.55f + 0.45f * Mathf.Abs(Mathf.Sin(_age * Mathf.Lerp(6f, 16f, t)));
            Color c = Color.Lerp(Warn, Danger, t) * pulse;
            if (_highContrast) c *= 1.35f; // brighter fill for a higher-contrast read
            c.a = 1f;

            float emission = _highContrast ? 2.1f : 1.5f;
            ApplyFill(_rend, c, emission);
            ApplyFill(_centerRend, c, emission);

            if (_age >= _life) Destroy(gameObject);
        }

        private void ApplyFill(Renderer rend, Color c, float emission)
        {
            if (rend == null) return;
            rend.GetPropertyBlock(_mpb);
            _mpb.SetColor(BaseColorId, c);
            _mpb.SetColor(EmissionId, c * emission);
            rend.SetPropertyBlock(_mpb);
        }
    }
}
