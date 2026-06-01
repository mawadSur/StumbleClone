using StumbleClone.Animation;
using StumbleClone.Core;
using UnityEngine;

namespace StumbleClone.Player
{
    /// Maps PlayerController state onto Animator parameters. If the Animator has no real clips
    /// bound (the project currently ships rigged meshes but no animation clips), it falls back
    /// to a ProceduralCharacterAnimator so the character still visibly moves. Silent no-op if
    /// there's no Animator at all.
    public sealed class PlayerAnimator : MonoBehaviour
    {
        [SerializeField] private Animator animator;
        [SerializeField] private Rigidbody body;
        [SerializeField] private PlayerController controller;
        [SerializeField] private float maxSpeedForNormalization = GameConstants.DefaultMoveSpeed;

        private static readonly int HashSpeed = Animator.StringToHash("Speed");
        private static readonly int HashGrounded = Animator.StringToHash("Grounded");
        private static readonly int HashJump = Animator.StringToHash("Jump");
        private static readonly int HashFall = Animator.StringToHash("Fall");
        private static readonly int HashKnockedDown = Animator.StringToHash("KnockedDown");

        private bool _wasGrounded = true;
        private ProceduralCharacterAnimator _proc;

        private void Awake()
        {
            if (animator == null) animator = GetComponentInChildren<Animator>();
            if (body == null) body = GetComponent<Rigidbody>();
            if (controller == null) controller = GetComponent<PlayerController>();

            if (!AnimatorClipUtil.HasRealClips(animator))
                _proc = AnimatorClipUtil.AttachFallback(this, animator);
        }

        private void Update()
        {
            if (body == null) return;

            Vector3 v = body.linearVelocity;
            float planar = new Vector2(v.x, v.z).magnitude;
            float denom = Mathf.Max(0.01f, maxSpeedForNormalization);
            float speed01 = Mathf.Clamp01(planar / denom);

            bool grounded = controller != null && controller.IsGrounded;
            bool justJumped = _wasGrounded && !grounded && v.y > 0.1f;

            if (_proc != null)
            {
                _proc.SetLocomotion(speed01, grounded);
                if (justJumped) _proc.NotifyJump();
            }
            else if (animator != null)
            {
                animator.SetFloat(HashSpeed, speed01);
                animator.SetBool(HashGrounded, grounded);
                animator.SetBool(HashFall, !grounded && v.y < -0.1f);
                if (justJumped) animator.SetTrigger(HashJump);
            }

            _wasGrounded = grounded;
        }

        public void TriggerKnockedDown()
        {
            if (_proc != null) _proc.NotifyKnockedDown();
            else if (animator != null) animator.SetTrigger(HashKnockedDown);
        }
    }
}
