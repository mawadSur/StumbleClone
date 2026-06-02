using StumbleClone.Audio;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace StumbleClone.UI
{
    /// Tactile press + hover feedback for a runtime button: a subtle scale punch on press
    /// (0.94) and a brightness lift on hover, both eased so state changes never snap. A
    /// disabled (non-interactable) button dims and stops responding. Pure transform/colour —
    /// it never changes layout bounds, so neighbouring UI stays put.
    ///
    /// Runs on unscaled time so feedback still works while the game is paused (Time.timeScale 0).
    [RequireComponent(typeof(RectTransform))]
    public sealed class ButtonFeedback : MonoBehaviour,
        IPointerDownHandler, IPointerUpHandler, IPointerEnterHandler, IPointerExitHandler
    {
        private const float PressScale = 0.94f;
        private const float HoverTint = 1.12f;
        private const float DisabledDim = 0.55f;

        private RectTransform _rt;
        private Image _img;
        private Selectable _selectable;
        private Color _base;
        private Vector3 _baseScale;
        private bool _pressed;
        private bool _hover;

        /// Wire up against the button's background image (call right after AddComponent).
        public void Init(Image img)
        {
            _rt = (RectTransform)transform;
            _img = img;
            _selectable = GetComponent<Selectable>();
            _base = img != null ? img.color : Color.white;
            _baseScale = _rt.localScale;
        }

        private void Awake()
        {
            if (_rt == null) Init(GetComponent<Image>());
        }

        private bool Interactable => _selectable == null || _selectable.interactable;

        public void OnPointerEnter(PointerEventData e) { _hover = true; }
        public void OnPointerExit(PointerEventData e) { _hover = false; _pressed = false; }
        public void OnPointerDown(PointerEventData e)
        {
            if (!Interactable) return;
            _pressed = true;
            // AudioManager.Play is null-safe (no-op until the manager bootstraps), so this never throws.
            AudioManager.Play(Sfx.UiClick);
        }
        public void OnPointerUp(PointerEventData e) { _pressed = false; }

        private void Update()
        {
            if (_rt == null) return;
            float k = 1f - Mathf.Exp(-Time.unscaledDeltaTime * 18f); // framerate-independent ease

            float targetScale = (Interactable && _pressed) ? PressScale : 1f;
            _rt.localScale = Vector3.Lerp(_rt.localScale, _baseScale * targetScale, k);

            if (_img == null) return;
            Color target;
            if (!Interactable)
            {
                target = new Color(_base.r * DisabledDim, _base.g * DisabledDim, _base.b * DisabledDim, _base.a * 0.8f);
            }
            else
            {
                float tint = _hover ? HoverTint : 1f;
                target = new Color(Mathf.Min(1f, _base.r * tint), Mathf.Min(1f, _base.g * tint),
                    Mathf.Min(1f, _base.b * tint), _base.a);
            }
            _img.color = Color.Lerp(_img.color, target, k);
        }
    }
}
