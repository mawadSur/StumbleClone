using StumbleClone.Core;
using UnityEngine;

namespace StumbleClone.Obstacles
{
    /// A cluster of walkable pillars that rise from the platform at staggered heights,
    /// forming a short course the racer must hop across. Unlike the other hazards these
    /// don't push — the danger is the gaps between them and the fall beyond. They rise,
    /// hold, then sink and despawn.
    ///
    /// NOTE: interpretation of the "tiered blocks you jump between" request — tune block
    /// count / spacing / heights to taste, or say if you meant something different
    /// (e.g. a moving staircase, or rising tiles the floor turns into).
    public sealed class StepBlocks : MonoBehaviour
    {
        private float _lifetime = 14f;
        private float _riseTime = 1.0f;
        private float _spawnTime;
        private Transform[] _blocks;
        private float[] _targetHeights;
        private Color _color = new Color(0.85f, 0.55f, 0.2f);

        /// Build a line of pillars along a chord of the platform, heading inward from the
        /// spawn edge. blockSize controls footprint; gap is the empty space between pillars.
        public void Init(Vector3 edgePoint, Transform arenaCenter, int count, float blockSize, float gap, float maxHeight)
        {
            _spawnTime = Time.time;

            Vector3 inward = arenaCenter != null ? (arenaCenter.position - edgePoint) : transform.forward;
            inward.y = 0f;
            if (inward.sqrMagnitude < 0.0001f) inward = Vector3.forward;
            inward.Normalize();

            _blocks = new Transform[count];
            _targetHeights = new float[count];
            float stride = blockSize + gap;

            for (int i = 0; i < count; i++)
            {
                var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
                cube.name = "Step_" + i;
                cube.layer = GameConstants.LayerGround;
                cube.transform.SetParent(transform, false);

                // Staircase up to the middle then back down, so it reads as a hump to clear.
                float t = count > 1 ? i / (float)(count - 1) : 0f;
                float height = Mathf.Lerp(1.5f, maxHeight, 1f - Mathf.Abs(t - 0.5f) * 2f);
                _targetHeights[i] = height;

                cube.transform.localScale = new Vector3(blockSize, height, blockSize);
                Vector3 top = edgePoint + inward * (2f + i * stride);
                // Start sunken (top flush with floor); rise into place in Update.
                cube.transform.position = new Vector3(top.x, -height, top.z);

                var rend = cube.GetComponent<Renderer>();
                if (rend != null) rend.material.color = _color;

                _blocks[i] = cube.transform;
            }
        }

        private void Update()
        {
            float elapsed = Time.time - _spawnTime;

            if (_blocks != null && elapsed < _riseTime)
            {
                float k = Mathf.Clamp01(elapsed / _riseTime);
                for (int i = 0; i < _blocks.Length; i++)
                {
                    if (_blocks[i] == null) continue;
                    float h = _targetHeights[i];
                    Vector3 p = _blocks[i].position;
                    // Lerp the block's center from sunken (-h/2 relative) up to resting (h/2).
                    p.y = Mathf.Lerp(-h, h * 0.5f, k);
                    _blocks[i].position = p;
                }
            }

            if (elapsed >= _lifetime) Destroy(gameObject);
        }
    }
}
