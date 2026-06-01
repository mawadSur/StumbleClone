using UnityEngine;
using UnityEngine.AI;
using StumbleClone.Core;

namespace StumbleClone.Bots
{
    /// Drives the shared locomotion AnimatorController from a bot's NavMeshAgent
    /// movement. Mirrors PlayerAnimator's parameter contract (Speed/Grounded).
    /// Silent no-op if no Animator is present so the project plays without rigs.
    [RequireComponent(typeof(BotController))]
    public sealed class BotAnimator : MonoBehaviour
    {
        [SerializeField] private Animator animator;
        [SerializeField] private float maxSpeedForNormalization = GameConstants.DefaultMoveSpeed;

        private NavMeshAgent _agent;
        private BotController _bot;

        private static readonly int HashSpeed = Animator.StringToHash("Speed");
        private static readonly int HashGrounded = Animator.StringToHash("Grounded");

        private void Awake()
        {
            _bot = GetComponent<BotController>();
            if (animator == null) animator = GetComponentInChildren<Animator>();
            _agent = GetComponent<NavMeshAgent>();
        }

        private void Update()
        {
            if (animator == null) return;

            float planar = 0f;
            if (_agent != null && _agent.enabled)
            {
                Vector3 v = _agent.velocity;
                planar = new Vector2(v.x, v.z).magnitude;
            }
            float denom = Mathf.Max(0.01f, maxSpeedForNormalization);
            animator.SetFloat(HashSpeed, Mathf.Clamp01(planar / denom));
            animator.SetBool(HashGrounded, _bot == null || _bot.IsGrounded());
        }
    }
}
