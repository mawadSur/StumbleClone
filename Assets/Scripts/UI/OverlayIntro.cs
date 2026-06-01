using System.Collections;
using UnityEngine;

namespace StumbleClone.UI
{
    /// A quick fade + scale-up entrance for a runtime overlay so screens animate in instead of
    /// snapping. Adds a CanvasGroup, eases over ~0.22s on unscaled time (so it plays even while
    /// the game is paused), then removes itself. One-liner entry point: OverlayIntro.Play(go).
    public sealed class OverlayIntro : MonoBehaviour
    {
        private const float Duration = 0.22f;

        private float _fromScale = 0.94f;

        /// Animate the overlay in. fromScale is the starting scale of the first child container.
        public static void Play(GameObject overlay, float fromScale = 0.94f)
        {
            if (overlay == null) return;
            var existing = overlay.GetComponent<OverlayIntro>();
            if (existing != null) Destroy(existing);
            var intro = overlay.AddComponent<OverlayIntro>();
            intro._fromScale = fromScale;
        }

        private IEnumerator Start()
        {
            var cg = GetComponent<CanvasGroup>();
            if (cg == null) cg = gameObject.AddComponent<CanvasGroup>();

            Transform target = transform.childCount > 0 ? transform.GetChild(0) : transform;
            Vector3 baseScale = target.localScale;

            float t = 0f;
            cg.alpha = 0f;
            while (t < Duration)
            {
                t += Time.unscaledDeltaTime;
                float k = Mathf.Clamp01(t / Duration);
                float e = 1f - Mathf.Pow(1f - k, 3f); // ease-out cubic
                cg.alpha = e;
                target.localScale = baseScale * Mathf.Lerp(_fromScale, 1f, e);
                yield return null;
            }

            cg.alpha = 1f;
            target.localScale = baseScale;
            Destroy(this);
        }
    }
}
