using UnityEngine;

namespace StumbleClone.UI
{
    /// Drives its RectTransform to fill the device safe area (the screen region not covered by
    /// notches, the Dynamic Island, hole-punch cameras, rounded corners, or the on-screen
    /// navigation bar). Recomputes only when the screen resolution, orientation, or reported
    /// safe area actually changes, so the per-frame poll in <see cref="Update"/> is cheap.
    ///
    /// Crucially this is a NO-OP on desktop and most WebGL: there <c>Screen.safeArea</c> equals
    /// the full screen rect, which yields anchors (0,0)-(1,1) and zero offsets — i.e. exactly the
    /// full-rect layout existing screens already use. On a notched phone the same math insets the
    /// content so it never lands under a cutout, while the cutout region stays empty.
    ///
    /// Place this on a full-rect child of a ScreenSpaceOverlay/Camera canvas and parent screen
    /// content onto it (RuntimeUI.Overlay does this automatically).
    [DisallowMultipleComponent]
    public sealed class SafeAreaFitter : MonoBehaviour
    {
        private RectTransform _rt;

        /// When true, destroying this object also destroys its parent (the canvas GameObject that
        /// hosts it). RuntimeUI.Overlay returns this SafeArea child but creates a dedicated canvas
        /// per overlay; callers Destroy() the returned object, so without this the empty canvas GO
        /// would leak. MobileControls leaves this false — its canvas is persistent and shared.
        private bool _ownsParentCanvas;

        /// Marks this fitter as owning the parent canvas GameObject so it is torn down together.
        /// Called by RuntimeUI.Overlay immediately after AddComponent.
        public void OwnParentCanvas()
        {
            _ownsParentCanvas = true;
        }

        private void OnDestroy()
        {
            if (!_ownsParentCanvas) return;
            var parent = transform.parent;
            // Guard against editor teardown / already-destroyed parents.
            if (parent != null && parent.gameObject != null)
            {
                Destroy(parent.gameObject);
            }
        }

        // Cached signature of the last applied state. Sentinel values force a recompute on the
        // first OnEnable/Update regardless of the initial device values.
        private Rect _lastSafeArea = new Rect(0f, 0f, 0f, 0f);
        private int _lastScreenWidth = -1;
        private int _lastScreenHeight = -1;
        private ScreenOrientation _lastOrientation = (ScreenOrientation)(-1);

        private void Awake()
        {
            _rt = transform as RectTransform;
        }

        private void OnEnable()
        {
            // Force a recompute next Apply regardless of cached state (e.g. after re-enable on a
            // device that rotated while this object was inactive).
            _lastScreenWidth = -1;
            _lastScreenHeight = -1;
            Apply();
        }

        private void Update()
        {
            Apply();
        }

        private void Apply()
        {
            if (_rt == null)
            {
                _rt = transform as RectTransform;
                if (_rt == null) return;
            }

            int width = Screen.width;
            int height = Screen.height;
            Rect safeArea = Screen.safeArea;
            ScreenOrientation orientation = Screen.orientation;

            // Nothing changed since the last applied frame — skip the (tiny) work entirely.
            if (width == _lastScreenWidth &&
                height == _lastScreenHeight &&
                orientation == _lastOrientation &&
                safeArea == _lastSafeArea)
            {
                return;
            }

            _lastScreenWidth = width;
            _lastScreenHeight = height;
            _lastOrientation = orientation;
            _lastSafeArea = safeArea;

            // Guard against zero (or not-yet-known) screen dimensions: a divide-by-zero would
            // produce NaN anchors and blank the UI. Leave the rect as-is until the screen reports
            // valid dimensions on a later frame.
            if (width <= 0 || height <= 0) return;

            Vector2 size = new Vector2(width, height);
            Vector2 anchorMin = safeArea.position;
            Vector2 anchorMax = safeArea.position + safeArea.size;
            anchorMin.x /= size.x;
            anchorMin.y /= size.y;
            anchorMax.x /= size.x;
            anchorMax.y /= size.y;

            // Sanity-clamp in case a platform reports a safe area slightly outside the screen.
            anchorMin.x = Mathf.Clamp01(anchorMin.x);
            anchorMin.y = Mathf.Clamp01(anchorMin.y);
            anchorMax.x = Mathf.Clamp01(anchorMax.x);
            anchorMax.y = Mathf.Clamp01(anchorMax.y);

            _rt.anchorMin = anchorMin;
            _rt.anchorMax = anchorMax;
            _rt.offsetMin = Vector2.zero;
            _rt.offsetMax = Vector2.zero;
        }
    }
}
