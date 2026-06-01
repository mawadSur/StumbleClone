using UnityEngine;
using StumbleClone.Core;

namespace StumbleClone.Obstacles
{
    [DisallowMultipleComponent]
    public class ShrinkingZoneVisualizer : MonoBehaviour
    {
        [SerializeField] private LineRenderer ring;
        [SerializeField] private int segments = 64;
        [SerializeField] private float yOffset = 0.05f;
        [SerializeField] private Color color = new Color(1f, 0.4f, 0.4f, 1f);
        [SerializeField] private float lineWidth = 0.3f;

        private float _currentRadius = 20f;

        private void Awake()
        {
            EnsureRing();
            Redraw();
        }

        private void OnEnable()
        {
            GameEvents.ShrinkRadiusChanged += HandleRadius;
        }

        private void OnDisable()
        {
            GameEvents.ShrinkRadiusChanged -= HandleRadius;
        }

        private void HandleRadius(float radius)
        {
            _currentRadius = Mathf.Max(0f, radius);
            Redraw();
        }

        private void EnsureRing()
        {
            if (ring != null) return;
            ring = gameObject.GetComponent<LineRenderer>();
            if (ring == null) ring = gameObject.AddComponent<LineRenderer>();
            ring.useWorldSpace = false;
            ring.loop = true;
            ring.widthMultiplier = lineWidth;
            ring.material = new Material(Shader.Find("Sprites/Default"));
            ring.startColor = color;
            ring.endColor = color;
        }

        private void Redraw()
        {
            if (ring == null) return;
            ring.positionCount = segments;
            float step = Mathf.PI * 2f / segments;
            for (int i = 0; i < segments; i++)
            {
                float a = step * i;
                ring.SetPosition(i, new Vector3(Mathf.Cos(a) * _currentRadius, yOffset, Mathf.Sin(a) * _currentRadius));
            }
        }
    }
}
