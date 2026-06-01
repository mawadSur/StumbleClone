using UnityEngine;

namespace StumbleClone.Player
{
    /// Maps PlayerController state onto Animator parameters. Silent no-op if no
    /// Animator is attached so the project plays before a Mixamo rig is imported.
    public sealed class PlayerAnimator : MonoBehaviour
    {
        [SerializeField] private Animator animator;
        [SerializeField] private Rigidbody body;
        [SerializeField] private PlayerController controller;
        [SerializeField] private float maxSpeedForNormalization = GameClampReference;

        private const float GameClampReference = 8f;

        private static readonly int HashSpeed = Animator.StringToHash("Speed");
        private static readonly int HashGrounded = Animator.StringToHash("Grounded");
        private static readonly int HashJump = Animator.StringToHash("Jump");
        private static readonly int HashFall = Animator.StringToHash("Fall");
        private static readonly int HashKnockedDown = Animator.StringToHash("KnockedDown");

        private bool _wasGrounded = true;

        private void Awake()
        {
            if (animator == null) animator = GetComponentInChildren<Animator>();
            if (body == null) body = GetComponent<Rigidbody>();
            if (controller == null) controller = GetComponent<PlayerController>();
        }

        private void Update()
        {
            if (animator == null || body == null) return;

            Vector3 v = body.linearVelocity;
            float planar = new Vector2(v.x, v.z).magnitude;
            float denom = Mathf.Max(0.01f, maxSpeedForNormalization);
            animator.SetFloat(HashSpeed, Mathf.Clamp01(planar / denom));

            bool grounded = controller != null && controller.IsGrounded;
            animator.SetBool(HashGrounded, grounded);
            animator.SetBool(HashFall, !grounded && v.y < -0.1f);

            if (!_wasGrounded && grounded)
            {
                // Just landed — clear in-air state cleanly.
            }
            if (_wasGrounded && !grounded && v.y > 0.1f)
            {
                animator.SetTrigger(HashJump);
            }
            _wasGrounded = grounded;
        }

        public void TriggerKnockedDown()
        {
            if (animator == null) return;
            animator.SetTrigger(HashKnockedDown);
        }
    }
}
