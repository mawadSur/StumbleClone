using UnityEngine;
using UnityEngine.AI;
using StumbleClone.Animation;
using StumbleClone.Core;

namespace StumbleClone.Bots
{
    /// Drives the shared locomotion AnimatorController from a bot's NavMeshAgent movement.
    /// Mirrors PlayerAnimator's parameter contract (Speed/Grounded) and the same no-clips
    /// fallback to ProceduralCharacterAnimator, so bots move even before real clips exist.
    [RequireComponent(typeof(BotController))]
    public sealed class BotAnimator : MonoBehaviour
    {
        [SerializeField] private Animator animator;
        [SerializeField] private float maxSpeedForNormalization = GameConstants.DefaultMoveSpeed;

        private NavMeshAgent _agent;
        private BotController _bot;
        private ProceduralCharacterAnimator _proc;

        private static readonly int HashSpeed = Animator.StringToHash("Speed");
        private static readonly int HashGrounded = Animator.StringToHash("Grounded");

        private void Awake()
        {
            _bot = GetComponent<BotController>();
            if (animator == null) animator = GetComponentInChildren<Animator>();
            _agent = GetComponent<NavMeshAgent>();

            if (!AnimatorClipUtil.HasRealClips(animator))
                _proc = AnimatorClipUtil.AttachFallback(this, animator);
        }

        private void Update()
        {
            float planar = 0f;
            if (_agent != null && _agent.enabled)
            {
                Vector3 v = _agent.velocity;
                planar = new Vector2(v.x, v.z).magnitude;
            }
            float denom = Mathf.Max(0.01f, maxSpeedForNormalization);
            float speed01 = Mathf.Clamp01(planar / denom);
            bool grounded = _bot == null || _bot.IsGrounded();

            if (_proc != null)
            {
                _proc.SetLocomotion(speed01, grounded);
            }
            else if (animator != null)
            {
                animator.SetFloat(HashSpeed, speed01);
                animator.SetBool(HashGrounded, grounded);
            }
        }

        /// Lets BotController play the topple reaction on knockback (procedural fallback only
        /// for now; the real KnockedDown state hooks in once clips are bound).
        public void NotifyKnockedDown()
        {
            if (_proc != null) _proc.NotifyKnockedDown();
        }
    }
}
