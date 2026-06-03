using UnityEngine;

namespace StumbleClone.Obstacles
{
    /// Thin bright RIM highlight drawn at the CURRENT platform edge. Since the floor itself
    /// now visibly shrinks (see <see cref="ArenaShrinker"/>), this is no longer a translucent
    /// "safe zone" disc — that would imply an invisible boundary different from the real floor,
    /// the exact confusion the rework removes. Instead it is a single crisp LineRenderer circle
    /// that hugs the live platform edge and pulses toward red as the platform closes, so the
    /// squeeze reads at a glance and players can see where the lip is.
    ///
    /// Built entirely in code with a URP-compatible transparent material (URP/Unlit) — no
    /// prefabs, no scene wiring. Owned and driven by <see cref="ArenaShrinker"/>; it never reads
    /// gameplay state itself so it stays a dumb visual.
    [DisallowMultipleComponent]
    public sealed class SafeZoneRing : MonoBehaviour
    {
        private const int Segments = 64;        // circle resolution
        private const float EdgeWidth = 0.45f;  // LineRenderer width (a touch bolder w/o the fill)
        private const float GroundLift = 0.06f; // sit just above the floor to avoid z-fight
        private const float PulseSpeed = 3.2f;  // pulse rate when the rim is closing

        // Calm → danger palette. Edge only now (no fill).
        private static readonly Color SafeColor = new Color(0.25f, 0.7f, 1f);    // cool blue
        private static readonly Color DangerColor = new Color(1f, 0.25f, 0.18f); // hot red

        private Vector3 _center;
        private float _fullRadius;

        private LineRenderer _edge;
        private Material _edgeMat;
        private readonly Vector3[] _circle = new Vector3[Segments];

        /// Set the centre + full radius. Safe to call again every frame as the platform tracks.
        public void Configure(Vector3 center, float fullRadius)
        {
            _center = center;
            _fullRadius = Mathf.Max(0.01f, fullRadius);
            EnsureBuilt();
            transform.position = new Vector3(_center.x, _center.y + GroundLift, _center.z);
        }

        /// Drive each frame with the current and full radius. Scales the rim to the live edge
        /// and blends/pulses the colour from safe (full) to danger (closed).
        public void UpdateVisual(float currentRadius, float fullRadius)
        {
            if (_edge == null) return;
            float full = Mathf.Max(0.01f, fullRadius);
            float r = Mathf.Max(0.01f, currentRadius);

            // 0 at full radius, 1 when fully shrunk → drives the danger tint + pulse.
            float danger = Mathf.Clamp01(1f - (r / full));

            float pulse = 0.5f + 0.5f * Mathf.Sin(Time.time * PulseSpeed);
            float tint = Mathf.Clamp01(danger + danger * pulse * 0.35f);
            Color edgeCol = Color.Lerp(SafeColor, DangerColor, tint);
            edgeCol.a = Mathf.Lerp(0.7f, 1f, danger);

            SetColor(_edgeMat, edgeCol);
            _edge.startColor = edgeCol;
            _edge.endColor = edgeCol;
            _edge.widthMultiplier = Mathf.Lerp(EdgeWidth, EdgeWidth * 1.5f, danger);

            // Rebuild the edge circle at the current radius (cheap: 64 points).
            BuildCircle(r);
            _edge.SetPositions(_circle);
        }

        private void EnsureBuilt()
        {
            if (_edge != null) return;

            var go = new GameObject("SafeZoneEdge");
            go.transform.SetParent(transform, false);
            go.transform.localPosition = Vector3.zero;

            _edge = go.AddComponent<LineRenderer>();
            _edge.useWorldSpace = false;
            _edge.loop = true;
            _edge.positionCount = Segments;
            _edge.numCornerVertices = 2;
            _edge.alignment = LineAlignment.View;
            _edge.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _edge.receiveShadows = false;

            _edgeMat = MakeTransparent(SafeColor);
            _edge.sharedMaterial = _edgeMat;
        }

        // Lay out the circle in the XZ plane (local space, parent sits at centre).
        private void BuildCircle(float radius)
        {
            const float tau = Mathf.PI * 2f;
            for (int i = 0; i < Segments; i++)
            {
                float a = tau * (i / (float)Segments);
                _circle[i] = new Vector3(Mathf.Cos(a) * radius, 0f, Mathf.Sin(a) * radius);
            }
        }

        // ---- URP transparent material ------------------------------------------

        // URP/Unlit configured for alpha blending. Unlit keeps the rim a flat, self-lit colour
        // (independent of scene lighting) and avoids URP/Lit's transparency keyword dance.
        private static Material MakeTransparent(Color color)
        {
            Shader sh = Shader.Find("Universal Render Pipeline/Unlit");
            if (sh == null) sh = Shader.Find("Unlit/Color");
            if (sh == null) sh = Shader.Find("Sprites/Default"); // last-ditch transparent fallback

            var m = new Material(sh);
            if (m.HasProperty("_Surface")) m.SetFloat("_Surface", 1f);
            if (m.HasProperty("_Blend")) m.SetFloat("_Blend", 0f);
            if (m.HasProperty("_SrcBlend")) m.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            if (m.HasProperty("_DstBlend")) m.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            if (m.HasProperty("_ZWrite")) m.SetFloat("_ZWrite", 0f);
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            m.DisableKeyword("_ALPHATEST_ON");
            m.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;

            SetColor(m, color);
            return m;
        }

        private static void SetColor(Material m, Color c)
        {
            if (m == null) return;
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
            if (m.HasProperty("_Color")) m.SetColor("_Color", c);
        }

        private void OnDestroy()
        {
            if (_edgeMat != null) Destroy(_edgeMat);
        }
    }
}
