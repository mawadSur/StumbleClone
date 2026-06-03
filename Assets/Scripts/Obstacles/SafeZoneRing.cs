using StumbleClone.Game;
using UnityEngine;

namespace StumbleClone.Obstacles
{
    /// The Knockout arena's CLOSING-EDGE telegraph. Since the floor itself now visibly shrinks
    /// (see <see cref="ArenaShrinker"/>), this draws two things glued to the live platform edge:
    ///
    ///   1. A translucent RED DANGER BAND — a flat annulus laid on the ground plane covering the
    ///      OUTER strip of floor that is ABOUT to disappear (from a warn-ahead inner radius out to
    ///      the current lip). Its alpha PULSES (blinks) so players read the closing edge and move
    ///      inward. The inner edge is fed the floor's projected position ~2-3s ahead, so the band
    ///      widens its warning before that ground actually vanishes.
    ///   2. A crisp bright rim LINE hugging the exact current edge so the lip itself reads clearly.
    ///
    /// Built entirely in code with URP-safe transparent materials ("Universal Render Pipeline/Unlit"
    /// → "Sprites/Default" fallback — NEVER legacy Unlit/Texture or Standard, which render pink/invisible
    /// under URP). No prefabs, no scene wiring. Owned and driven by <see cref="ArenaShrinker"/>; it never
    /// reads gameplay state itself (only the ReducedMotion accessibility flag) so it stays a dumb visual.
    ///
    /// Honors <see cref="SettingsStore.ReducedMotion"/>: when on, the band holds a steady alpha (no blink).
    [DisallowMultipleComponent]
    public sealed class SafeZoneRing : MonoBehaviour
    {
        private const int Segments = 72;        // ring resolution (band + edge line)
        private const float EdgeWidth = 0.45f;  // LineRenderer width for the crisp lip line
        private const float GroundLift = 0.06f; // sit just above the floor to avoid z-fight
        private const float BandLift = 0.04f;   // band sits a hair below the line, still above floor
        private const float PulseSpeed = 4.2f;  // blink rate of the danger band

        // Danger band alpha envelope (multiplied by the band's base colour alpha).
        private const float BandMinAlpha = 0.18f; // dimmest point of the blink
        private const float BandMaxAlpha = 0.62f; // brightest point of the blink
        private const float BandSteadyAlpha = 0.42f; // fixed alpha under ReducedMotion (no blink)

        // Calm → danger palette for the crisp edge line.
        private static readonly Color SafeColor = new Color(0.25f, 0.7f, 1f);    // cool blue
        private static readonly Color DangerColor = new Color(1f, 0.25f, 0.18f); // hot red
        // Bright warning red for the translucent band (kept saturated so it pops on any floor).
        private static readonly Color BandColor = new Color(1f, 0.16f, 0.12f);

        private Vector3 _center;
        private float _fullRadius;

        private LineRenderer _edge;
        private Material _edgeMat;
        private readonly Vector3[] _circle = new Vector3[Segments];

        // Danger-band annulus mesh (triangle strip between an inner and outer circle).
        private MeshRenderer _bandRenderer;
        private MeshFilter _bandFilter;
        private Mesh _bandMesh;
        private Material _bandMat;
        private readonly Vector3[] _bandVerts = new Vector3[Segments * 2];
        private int[] _bandTris;

        /// Set the centre + full radius. Safe to call again every frame as the platform tracks.
        public void Configure(Vector3 center, float fullRadius)
        {
            _center = center;
            _fullRadius = Mathf.Max(0.01f, fullRadius);
            EnsureBuilt();
            transform.position = new Vector3(_center.x, _center.y, _center.z);
        }

        /// Drive each frame with the live edge radius, the warn-ahead inner radius (where the floor
        /// will be in ~2-3s), and the full radius. The crisp line tracks <paramref name="currentRadius"/>;
        /// the red band fills the annulus from <paramref name="warnInnerRadius"/> out to the live edge
        /// and pulses (blinks) its alpha. Under ReducedMotion the band holds a steady alpha.
        public void UpdateVisual(float currentRadius, float warnInnerRadius, float fullRadius)
        {
            float full = Mathf.Max(0.01f, fullRadius);
            float outer = Mathf.Max(0.01f, currentRadius);
            float inner = Mathf.Clamp(warnInnerRadius, 0.01f, outer);

            // 0 at full radius, 1 when fully shrunk → drives the line's danger tint.
            float danger = Mathf.Clamp01(1f - (outer / full));

            UpdateEdgeLine(outer, danger);
            UpdateDangerBand(inner, outer);
        }

        // ---- Crisp edge line ----------------------------------------------------

        private void UpdateEdgeLine(float radius, float danger)
        {
            if (_edge == null) return;

            float pulse = 0.5f + 0.5f * Mathf.Sin(Time.time * PulseSpeed);
            float tint = Mathf.Clamp01(danger + danger * pulse * 0.35f);
            Color edgeCol = Color.Lerp(SafeColor, DangerColor, tint);
            edgeCol.a = Mathf.Lerp(0.7f, 1f, danger);

            SetColor(_edgeMat, edgeCol);
            _edge.startColor = edgeCol;
            _edge.endColor = edgeCol;
            _edge.widthMultiplier = Mathf.Lerp(EdgeWidth, EdgeWidth * 1.5f, danger);

            BuildCircle(radius, GroundLift);
            _edge.SetPositions(_circle);
        }

        // ---- Translucent blinking danger band -----------------------------------

        private void UpdateDangerBand(float innerRadius, float outerRadius)
        {
            if (_bandRenderer == null || _bandMesh == null) return;

            // Hide the band entirely once it has effectively zero width (floor settled / not closing).
            float width = outerRadius - innerRadius;
            bool visible = width > 0.05f;
            if (_bandRenderer.enabled != visible) _bandRenderer.enabled = visible;
            if (!visible) return;

            // Blink the alpha unless ReducedMotion asks for a steady band.
            float alpha;
            if (SettingsStore.ReducedMotion)
            {
                alpha = BandSteadyAlpha;
            }
            else
            {
                float pulse = 0.5f + 0.5f * Mathf.Sin(Time.time * PulseSpeed);
                alpha = Mathf.Lerp(BandMinAlpha, BandMaxAlpha, pulse);
            }

            Color c = BandColor;
            c.a = alpha;
            SetColor(_bandMat, c);

            BuildAnnulus(innerRadius, outerRadius, BandLift);
            _bandMesh.vertices = _bandVerts;
            _bandMesh.RecalculateBounds();
        }

        // ---- Build / geometry ---------------------------------------------------

        private void EnsureBuilt()
        {
            if (_edge != null) return;

            // Crisp edge line.
            var lineGo = new GameObject("SafeZoneEdge");
            lineGo.transform.SetParent(transform, false);
            lineGo.transform.localPosition = Vector3.zero;

            _edge = lineGo.AddComponent<LineRenderer>();
            _edge.useWorldSpace = false;
            _edge.loop = true;
            _edge.positionCount = Segments;
            _edge.numCornerVertices = 2;
            _edge.alignment = LineAlignment.View;
            _edge.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _edge.receiveShadows = false;
            _edgeMat = MakeTransparent(SafeColor);
            _edge.sharedMaterial = _edgeMat;

            // Danger band annulus mesh.
            var bandGo = new GameObject("DangerBand");
            bandGo.transform.SetParent(transform, false);
            bandGo.transform.localPosition = Vector3.zero;

            _bandFilter = bandGo.AddComponent<MeshFilter>();
            _bandRenderer = bandGo.AddComponent<MeshRenderer>();
            _bandRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _bandRenderer.receiveShadows = false;
            _bandRenderer.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
            _bandRenderer.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;

            _bandMesh = new Mesh { name = "DangerBandMesh" };
            _bandMesh.MarkDynamic();
            BuildBandTriangles();
            BuildAnnulus(1f, 2f, BandLift); // placeholder geometry; rebuilt each frame
            _bandMesh.vertices = _bandVerts;
            _bandMesh.triangles = _bandTris;
            _bandMesh.RecalculateBounds();
            _bandFilter.sharedMesh = _bandMesh;

            _bandMat = MakeTransparent(BandColor);
            _bandRenderer.sharedMaterial = _bandMat;
        }

        // Lay out a flat circle in the XZ plane (local space, parent sits at centre).
        private void BuildCircle(float radius, float lift)
        {
            const float tau = Mathf.PI * 2f;
            for (int i = 0; i < Segments; i++)
            {
                float a = tau * (i / (float)Segments);
                _circle[i] = new Vector3(Mathf.Cos(a) * radius, lift, Mathf.Sin(a) * radius);
            }
        }

        // Fill the annulus vertex ring: even indices = inner circle, odd = outer circle, paired by angle.
        private void BuildAnnulus(float innerRadius, float outerRadius, float lift)
        {
            const float tau = Mathf.PI * 2f;
            for (int i = 0; i < Segments; i++)
            {
                float a = tau * (i / (float)Segments);
                float cos = Mathf.Cos(a);
                float sin = Mathf.Sin(a);
                _bandVerts[i * 2] = new Vector3(cos * innerRadius, lift, sin * innerRadius);
                _bandVerts[i * 2 + 1] = new Vector3(cos * outerRadius, lift, sin * outerRadius);
            }
        }

        // Triangle indices for the closed annulus strip (two triangles per segment, wrapping around).
        // Built once; only vertex positions change per frame.
        private void BuildBandTriangles()
        {
            _bandTris = new int[Segments * 6];
            int t = 0;
            for (int i = 0; i < Segments; i++)
            {
                int inA = i * 2;
                int outA = i * 2 + 1;
                int next = (i + 1) % Segments;
                int inB = next * 2;
                int outB = next * 2 + 1;

                // Wind so the face points up (+Y) toward the top-down/third-person camera.
                _bandTris[t++] = inA; _bandTris[t++] = inB; _bandTris[t++] = outA;
                _bandTris[t++] = outA; _bandTris[t++] = inB; _bandTris[t++] = outB;
            }
        }

        // ---- URP-safe transparent material --------------------------------------

        // URP/Unlit configured for alpha blending. Unlit keeps the band/line a flat, self-lit colour
        // (independent of scene lighting) and avoids URP/Lit's transparency keyword dance. Falls back to
        // Sprites/Default (also transparent + URP-safe). NEVER Unlit/Texture or Standard (pink under URP).
        private static Material MakeTransparent(Color color)
        {
            Shader sh = Shader.Find("Universal Render Pipeline/Unlit");
            if (sh == null) sh = Shader.Find("Sprites/Default"); // URP-safe transparent fallback

            var m = new Material(sh);
            if (m.HasProperty("_Surface")) m.SetFloat("_Surface", 1f);
            if (m.HasProperty("_Blend")) m.SetFloat("_Blend", 0f);
            if (m.HasProperty("_SrcBlend")) m.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            if (m.HasProperty("_DstBlend")) m.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            if (m.HasProperty("_ZWrite")) m.SetFloat("_ZWrite", 0f);
            // Render double-sided so the band reads even if the camera dips below the floor plane.
            if (m.HasProperty("_Cull")) m.SetFloat("_Cull", (float)UnityEngine.Rendering.CullMode.Off);
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
            if (_bandMat != null) Destroy(_bandMat);
            if (_bandMesh != null) Destroy(_bandMesh);
        }
    }
}
