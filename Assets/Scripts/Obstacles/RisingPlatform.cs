using System.Collections;
using UnityEngine;

namespace StumbleClone.Obstacles
{
    [DisallowMultipleComponent]
    public class RisingPlatform : MonoBehaviour
    {
        [SerializeField] private float startY = -10f;
        [SerializeField] private float endY = 0f;
        [SerializeField] private float duration = 4f;
        [SerializeField] private AnimationCurve curve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

        private bool _running;

        private void Awake()
        {
            Vector3 p = transform.position;
            p.y = startY;
            transform.position = p;
        }

        public void Begin()
        {
            if (_running) return;
            StartCoroutine(RiseRoutine());
        }

        private IEnumerator RiseRoutine()
        {
            _running = true;
            float elapsed = 0f;
            Vector3 basePos = transform.position;
            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.Clamp01(elapsed / duration);
                float eased = curve.Evaluate(t);
                Vector3 p = basePos;
                p.y = Mathf.Lerp(startY, endY, eased);
                transform.position = p;
                yield return null;
            }
            Vector3 final = transform.position;
            final.y = endY;
            transform.position = final;
        }
    }
}
