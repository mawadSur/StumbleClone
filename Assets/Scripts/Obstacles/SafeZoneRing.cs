using StumbleClone.Core;
using UnityEngine;

namespace StumbleClone.Obstacles
{
    /// Cheap procedural visual for the shrinking safe zone: a flattened translucent
    /// cylinder marking the safe ground plus a crisp LineRenderer circle at the edge.
    /// Both scale to the current safe radius and pulse toward red as the ring closes,
    /// so the squeeze reads at a glance. Built entirely in code with URP-compatible
    /// materials (URP/Unlit transparent) — no prefabs, no scene wiring.
    ///
    /// Owned and driven by <see cref="ArenaShrinker"/>; it never reads gameplay state
    /// itself so it stays a dumb, pooled visual.
    [DisallowMultipleComponent]
    public sealed class SafeZoneRing : MonoBehaviour
    {
        private const int Segments = 64;       // circle resolution
        private const float EdgeWidth = 0.35f; // LineRenderer width
        private const float FillHeight = 0.04f; // flattened cylinder thickness
        private const float GroundLift = 0.03f; // sit just above the floor to avoid z-fight
        private const float PulseSpeed = 3.2f;  // pulse rate when the ring is small

        // Calm → danger palette. Fill is heavily transparent; edge is brighter.
        private static readonly Color SafeColor = new Color(0.25f, 0.7f, 1f);   // cool blue
        private static readonly Color DangerColor = new Color(1f, 0.25f, 0.18f); // hot red

        private Vector3 _center;
        private float _fullRadius;

        private Transform _fill;        // flattened cylinder
        private Material _fillMat;
        private LineRenderer _edge;
        private Material _edgeMat;
        private readonly Vector3[] _circle = new Vector3[Segments];

        /// Set the centre + full radius once at round start. Safe to call again on reset.
        public void Configure(Vector3 center, float fullRadius)
        {
            _center = center;
            _fullRadius = Mathf.Max(0.01f, fullRadius);
            EnsureBuilt();
            transform.position = new Vector3(_center.x, _center.y + GroundLift, _center.z);
            UpdateVisual(_fullRadius, _fullRadius);
        }

        /// Drive each frame with the current and full radius. Scales the geometry and
        /// blends/pulses the colour from safe (full) to danger (closed).
        public void UpdateVisual(float currentRadius, float fullRadius)
        {
            if (_fill == null || _edge == null) return;
            float full = Mathf.Max(0.01f, fullRadius);
            float r = Mathf.Max(0.01f, currentRadius);

            // 0 at full radius, 1 when fully shrunk → drives the danger tint + pulse.
            float danger = Mathf.Clamp01(1f - (r / full));

            // Pulse only matters as it gets small; at full it's effectively off.
            float pulse = 0.5f + 0.5f * Mathf.Sin(Time.time * PulseSpeed);
            float tint = Mathf.Clamp01(danger + danger * pulse * 0.35f);
            Color baseCol = Color.Lerp(SafeColor, DangerColor, tint);

            // Fill: keep it subtle so the arena stays readable; nudge alpha up with danger.
            Color fillCol = baseCol;
            fillCol.a = Mathf.Lerp(0.10f, 0.22f, danger);
            SetColor(_fillMat, fillCol);

            // Edge: bold and opaque-ish so the boundary is unmistakable.
            Color edgeCol = baseCol;
            edgeCol.a = Mathf.Lerp(0.65f, 1f, danger);
            SetColor(_edgeMat, edgeCol);
            _edge.startColor = edgeCol;
            _edge.endColor = edgeCol;
            _edge.widthMultiplier = Mathf.Lerp(EdgeWidth, EdgeWidth * 1.6f, danger);

            // Scale the flattened cylinder to the current radius (primitive cylinder
            // has radius 0.5 at unit scale, so diameter = scale).
            _fill.localScale = new Vector3(r * 2f, FillHeight, r * 2f);

            // Rebuild the edge circle at the current radius (cheap: 64 points).
            BuildCircle(r);
            _edge.SetPositions(_circle);
        }

        private void EnsureBuilt()
        {
            if (_fill == null) BuildFill();
            if (_edge == null) BuildEdge();
        }

        private void BuildFill()
        {
            var cyl = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            cyl.name = "SafeZoneFill";
            // No physics — this is a pure visual marker.
            var col = cyl.GetComponent<Collider>();
            if (col != null) Destroy(col);

            _fill = cyl.transform;
            _fill.SetParent(transform, false);
            _fill.localPosition = Vector3.zero;

            _fillMat = MakeTransparent(SafeColor);
            var rend = cyl.GetComponent<Renderer>();
            rend.sharedMaterial = _fillMat;
            rend.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            rend.receiveShadows = false;
        }

        private void BuildEdge()
        {
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

        // URP/Unlit configured for alpha blending. Unlit keeps the marker a flat,
        // self-lit colour (independent of scene lighting) and avoids URP/Lit's more
        // involved transparency keyword dance. RuntimeMaterial covers opaque URP/Lit;
        // this is the transparent-overlay cousin.
        private static Material MakeTransparent(Color color)
        {
            Shader sh = Shader.Find("Universal Render Pipeline/Unlit");
            if (sh == null) sh = Shader.Find("Unlit/Color");
            if (sh == null) sh = Shader.Find("Sprites/Default"); // last-ditch transparent fallback

            var m = new Material(sh);
            // URP transparent surface setup (Surface=Transparent, Blend=Alpha).
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
            if (_fillMat != null) Destroy(_fillMat);
            if (_edgeMat != null) Destroy(_edgeMat);
        }
    }
}
