using UnityEngine;

namespace StumbleClone.Obstacles
{
    /// A ground marker placed at a rim octant a beat before a hazard arrives, so the player
    /// can "read" the incoming direction. Pulses yellow→red over its lifetime, then removes
    /// itself exactly as the hazard spawns. Built from a thin primitive disc (opaque, no
    /// transparency setup) so it renders reliably in URP with zero art. Spawned by
    /// ObstacleSpawner; no scene wiring.
    public sealed class TelegraphIndicator : MonoBehaviour
    {
        private float _life;
        private float _age;
        private Renderer _rend;
        private MaterialPropertyBlock _mpb;
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int EmissionId = Shader.PropertyToID("_EmissionColor");

        private static readonly Color Warn = new Color(1f, 0.85f, 0.1f);   // early: yellow
        private static readonly Color Danger = new Color(1f, 0.12f, 0.08f); // late:  red

        public static void Spawn(Vector3 rimPoint, float groundY, float life, float diameter)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            go.name = "Telegraph";
            var col = go.GetComponent<Collider>();
            if (col != null) Destroy(col); // visual only — never blocks gameplay
            // Thin disc lying flat just above the floor.
            go.transform.position = new Vector3(rimPoint.x, groundY + 0.05f, rimPoint.z);
            go.transform.localScale = new Vector3(diameter, 0.02f, diameter);

            var ti = go.AddComponent<TelegraphIndicator>();
            ti._life = Mathf.Max(0.1f, life);
            ti._rend = go.GetComponent<Renderer>();
            ti._mpb = new MaterialPropertyBlock();
        }

        private void Update()
        {
            _age += Time.deltaTime;
            float t = Mathf.Clamp01(_age / _life);

            // Colour ramps yellow→red; brightness pulses faster as the hazard nears.
            float pulse = 0.55f + 0.45f * Mathf.Abs(Mathf.Sin(_age * Mathf.Lerp(6f, 16f, t)));
            Color c = Color.Lerp(Warn, Danger, t) * pulse;
            c.a = 1f;

            if (_rend != null)
            {
                _rend.GetPropertyBlock(_mpb);
                _mpb.SetColor(BaseColorId, c);
                _mpb.SetColor(EmissionId, c * 1.5f);
                _rend.SetPropertyBlock(_mpb);
            }

            if (_age >= _life) Destroy(gameObject);
        }
    }
}
