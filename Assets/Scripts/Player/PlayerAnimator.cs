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
        private static readonly int HashDash = Animator.StringToHash("Dash");

        private const float DashLungeDur = 0.28f;

        private bool _wasGrounded = true;
        private ProceduralCharacterAnimator _proc;

        // Dash-lunge overlay for the REAL-animator path: the shipped controller has no "Dash"
        // state, so SetTrigger("Dash") would do nothing. Instead we briefly tilt + stretch the
        // animator's transform on top of whatever clip is playing, so the dash always reads.
        private bool _hasDashParam;
        private Transform _visual;
        private Vector3 _visualBasePos;
        private Quaternion _visualBaseRot;
        private Vector3 _visualBaseScale;
        private bool _visualCaptured;
        private float _dashLunge;

        private void Awake()
        {
            if (animator == null) animator = GetComponentInChildren<Animator>();
            if (body == null) body = GetComponent<Rigidbody>();
            if (controller == null) controller = GetComponent<PlayerController>();

            if (!AnimatorClipUtil.HasRealClips(animator))
            {
                _proc = AnimatorClipUtil.AttachFallback(this, animator);
            }
            else
            {
                // Real animator in use — set up the procedural dash overlay.
                _hasDashParam = HasParam(animator, HashDash);
                _visual = animator.transform;
                _visualBasePos = _visual.localPosition;
                _visualBaseRot = _visual.localRotation;
                _visualBaseScale = _visual.localScale;
                _visualCaptured = true;
            }
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

        // Applies the dash SLIDE after the Animator has written its pose for the frame: the
        // character drops low, reclines back (feet-first) and stretches along travel.
        private void LateUpdate()
        {
            if (!_visualCaptured || _dashLunge <= 0f) return;

            _dashLunge -= Time.deltaTime;
            float d = Mathf.Clamp01(_dashLunge / DashLungeDur); // 1 -> 0 over the move
            float s = Mathf.Sin(Mathf.Clamp01(d) * Mathf.PI);   // 0 -> 1 -> 0

            _visual.localPosition = _visualBasePos - new Vector3(0f, 0.32f * s, 0f);
            _visual.localRotation = _visualBaseRot * Quaternion.Euler(-38f * d, 0f, 0f);
            _visual.localScale = new Vector3(
                _visualBaseScale.x * (1f - 0.10f * s),
                _visualBaseScale.y * (1f - 0.34f * s),
                _visualBaseScale.z * (1f + 0.28f * s));

            if (_dashLunge <= 0f) // restore exactly on the last frame
            {
                _visual.localPosition = _visualBasePos;
                _visual.localRotation = _visualBaseRot;
                _visual.localScale = _visualBaseScale;
            }
        }

        public void TriggerKnockedDown()
        {
            if (_proc != null) _proc.NotifyKnockedDown();
            else if (animator != null) animator.SetTrigger(HashKnockedDown);
        }

        public void TriggerDash()
        {
            if (_proc != null)
            {
                _proc.NotifyDash();
                return;
            }
            if (_hasDashParam && animator != null) animator.SetTrigger(HashDash);
            _dashLunge = DashLungeDur; // procedural overlay — works with or without a Dash state
        }

        private static bool HasParam(Animator a, int nameHash)
        {
            if (a == null || a.runtimeAnimatorController == null) return false;
            var ps = a.parameters;
            for (int i = 0; i < ps.Length; i++)
                if (ps[i].nameHash == nameHash) return true;
            return false;
        }
    }
}
