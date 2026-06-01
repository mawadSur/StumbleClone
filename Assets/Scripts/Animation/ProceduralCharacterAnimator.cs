using UnityEngine;

namespace StumbleClone.Animation
{
    /// A zero-asset "make it look alive" animator. When the real Animator has no clips bound
    /// (the project currently ships rigged meshes but no animation clips), PlayerAnimator /
    /// BotAnimator attach this at runtime and feed it locomotion + jump + knockdown signals.
    /// It animates the character's VISUAL transform as a whole — no skeleton knowledge needed:
    /// walk bob, forward lean, side sway, idle breathing, jump squash/stretch, knockdown topple
    /// and recover. Auto-disabled once real clips are imported (the drivers stop attaching it).
    public sealed class ProceduralCharacterAnimator : MonoBehaviour
    {
        [SerializeField] private Transform visual;
        [SerializeField] private float bobHeight = 0.12f;
        [SerializeField] private float maxLeanDeg = 11f;
        [SerializeField] private float swayRollDeg = 7f;
        [SerializeField] private float stepFrequency = 2.2f; // strides/sec at full speed
        [SerializeField] private float idleBreath = 0.03f;

        private const float JumpDur = 0.35f;
        private const float KnockDur = 1.3f;

        private Vector3 _basePos;
        private Quaternion _baseRot;
        private Vector3 _baseScale;
        private bool _captured;

        private float _phase;
        private float _speed01;
        private float _jumpTimer;
        private float _knockTimer;

        /// Point the animator at the mesh root to animate (usually the Animator's own transform).
        public void SetVisual(Transform t)
        {
            visual = t;
            CaptureBase();
        }

        private void Awake()
        {
            if (visual == null)
            {
                // Prefer a child named "Character"; else the first skinned mesh; else first child.
                Transform found = transform.Find("Character");
                if (found == null)
                {
                    var smr = GetComponentInChildren<SkinnedMeshRenderer>();
                    if (smr != null) found = smr.transform;
                }
                if (found == null && transform.childCount > 0) found = transform.GetChild(0);
                visual = found != null ? found : transform;
            }
            CaptureBase();
        }

        private void CaptureBase()
        {
            if (visual == null) return;
            _basePos = visual.localPosition;
            _baseRot = visual.localRotation;
            _baseScale = visual.localScale;
            _captured = true;
        }

        public void SetLocomotion(float speed01, bool grounded)
        {
            _speed01 = Mathf.Clamp01(speed01);
        }

        public void NotifyJump() => _jumpTimer = JumpDur;

        public void NotifyKnockedDown() => _knockTimer = KnockDur;

        private void LateUpdate()
        {
            if (visual == null || !_captured) return;
            float dt = Time.deltaTime;

            // ---- Knockdown: topple over, lie a beat, get back up -------------------
            if (_knockTimer > 0f)
            {
                _knockTimer -= dt;
                float k = 1f - Mathf.Clamp01(_knockTimer / KnockDur);   // 0→1 over the move
                float down = Mathf.Sin(Mathf.Clamp01(k * 1.7f) * Mathf.PI * 0.5f);
                float up = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.72f, 1f, k));
                float amount = Mathf.Clamp01(down - up);
                visual.localRotation = _baseRot * Quaternion.Euler(amount * 82f, 0f, amount * 14f);
                visual.localPosition = _basePos + Vector3.up * (-0.18f * amount);
                visual.localScale = _baseScale;
                return;
            }

            _phase += dt * Mathf.PI * 2f * Mathf.Lerp(0.6f, stepFrequency, _speed01);

            // ---- Jump squash / stretch --------------------------------------------
            float jumpStretchY = 1f, jumpSquashXZ = 1f, jumpPop = 0f;
            if (_jumpTimer > 0f)
            {
                _jumpTimer -= dt;
                float j = 1f - Mathf.Clamp01(_jumpTimer / JumpDur); // 0→1
                float s = Mathf.Sin(j * Mathf.PI);                  // 0→1→0
                jumpStretchY = 1f + 0.18f * s;
                jumpSquashXZ = 1f - 0.10f * s;
                jumpPop = 0.10f * s;
            }

            // ---- Locomotion: bob + lean + sway, idle breathing --------------------
            float bob = Mathf.Abs(Mathf.Sin(_phase)) * bobHeight * _speed01;
            float breath = (_speed01 < 0.05f) ? Mathf.Sin(Time.time * 2f) * idleBreath : 0f;
            float lean = maxLeanDeg * _speed01;
            float sway = Mathf.Sin(_phase) * swayRollDeg * _speed01;

            visual.localPosition = _basePos + Vector3.up * (bob + jumpPop);
            visual.localRotation = _baseRot * Quaternion.Euler(lean, 0f, sway);
            visual.localScale = new Vector3(
                _baseScale.x * jumpSquashXZ,
                _baseScale.y * jumpStretchY * (1f + breath),
                _baseScale.z * jumpSquashXZ);
        }
    }
}
