using System.Collections;
using StumbleClone.Game;
using UnityEngine;

namespace StumbleClone.UI
{
    /// A quick fade + scale-up entrance for a runtime overlay so screens animate in instead of
    /// snapping. Adds a CanvasGroup, eases over ~0.22s on unscaled time (so it plays even while
    /// the game is paused), then removes itself. One-liner entry point: OverlayIntro.Play(go).
    ///
    /// Respects the Reduced-Motion accessibility setting: when SettingsStore.ReducedMotion is on,
    /// the scale-up is skipped and the fade collapses to a near-instant (<=0.05s) cross-fade.
    public sealed class OverlayIntro : MonoBehaviour
    {
        private const float Duration = 0.22f;

        /// Fade length used when Reduced Motion is enabled — short enough to read as instant,
        /// long enough to avoid a hard pop. No scaling is applied in this mode.
        private const float ReducedMotionDuration = 0.05f;

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

            // Reduced Motion: no scale-up, just a near-instant fade so the screen still cross-fades
            // in (rather than hard-popping) without any vestibular-triggering movement.
            bool reducedMotion = SettingsStore.ReducedMotion;
            float duration = reducedMotion ? ReducedMotionDuration : Duration;

            float t = 0f;
            cg.alpha = 0f;
            while (t < duration)
            {
                t += Time.unscaledDeltaTime;
                float k = Mathf.Clamp01(t / duration);
                float e = reducedMotion ? k : 1f - Mathf.Pow(1f - k, 3f); // linear vs ease-out cubic
                cg.alpha = e;
                if (!reducedMotion) target.localScale = baseScale * Mathf.Lerp(_fromScale, 1f, e);
                yield return null;
            }

            cg.alpha = 1f;
            target.localScale = baseScale;
            Destroy(this);
        }
    }
}
