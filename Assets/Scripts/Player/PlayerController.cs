using System;
using StumbleClone.Audio;
using StumbleClone.Core;
using UnityEngine;

namespace StumbleClone.Player
{
    [RequireComponent(typeof(Rigidbody), typeof(CapsuleCollider))]
    public sealed class PlayerController : MonoBehaviour, IRacer
    {
        [Header("Movement")]
        [SerializeField] private float moveSpeed = GameConstants.DefaultMoveSpeed;
        [SerializeField] private float jumpSpeed = GameConstants.DefaultJumpSpeed;
        [Tooltip("How fast planar velocity ramps toward the target while grounded (units/s^2). Higher = snappier.")]
        [SerializeField] private float groundAcceleration = 60f;
        [Tooltip("Planar acceleration while airborne (units/s^2). Lower than ground for floaty air control.")]
        [SerializeField] private float airAcceleration = 25f;
        [SerializeField] private float groundCheckDistance = 0.2f;
        [SerializeField] private float groundCheckRadius = 0.45f;
        [Tooltip("Layers that count as ground. Player/Bot bits are stripped at runtime to prevent self-hits.")]
        [SerializeField] private LayerMask groundMask = (1 << GameConstants.LayerGround) | (1 << GameConstants.LayerObstacle);
        [Tooltip("Slopes up to this angle (deg) are walkable: movement follows the surface and the body holds its spot when idle. Steeper surfaces let gravity slide the player down.")]
        [SerializeField] private float maxSlopeAngle = 50f;

        [Header("Jump Feel")]
        [Tooltip("Grace window after leaving a ledge during which a jump still fires.")]
        [SerializeField] private float coyoteTime = 0.1f;
        [Tooltip("How early a jump press is remembered and fired once grounded.")]
        [SerializeField] private float jumpBufferTime = 0.1f;

        [Header("Identity")]
        [SerializeField] private int racerId = 0;
        [SerializeField] private string displayName = "Player";

        [Header("Tuning")]
        [SerializeField] private float inputLockOnKnockback = 0.3f;

        private const float TurnSmoothTime = 0.08f;

        private Rigidbody _rb;
        private CapsuleCollider _collider;
        private Renderer[] _renderers;
        private PlayerInputHandler _input;
        private PlayerAnimator _animator;
        private Transform _cameraTransform;
        private float _inputLockUntil;
        private float _turnVelocity;
        private bool _grounded;
        private Vector3 _groundNormal = Vector3.up;
        private bool _onWalkableSlope;
        private float _lastGroundedTime = float.NegativeInfinity;
        private float _jumpBufferedTime = float.NegativeInfinity;

        public int RacerId => racerId;
        public string DisplayName => displayName;
        public Transform Transform => transform;
        public bool IsAlive { get; private set; } = true;
        public bool IsFinished { get; private set; }
        public bool IsPlayer => true;
        public bool IsGrounded => _grounded;

        public event Action<IRacer> OnFinished;
        public event Action<IRacer> OnEliminated;

        private void Awake()
        {
            _rb = GetComponent<Rigidbody>();
            _collider = GetComponent<CapsuleCollider>();
            _renderers = GetComponentsInChildren<Renderer>(true);
            _input = GetComponent<PlayerInputHandler>();
            _animator = GetComponent<PlayerAnimator>();
            _rb.freezeRotation = true;
            _rb.interpolation = RigidbodyInterpolation.Interpolate;
            _rb.collisionDetectionMode = CollisionDetectionMode.Continuous;

            // The ground check must never satisfy itself on the player's own body.
            // Strip Player/Bot bits even if the mask was authored as Everything in-prefab.
            int selfLayers = (1 << GameConstants.LayerPlayer) | (1 << GameConstants.LayerBot);
            groundMask &= ~selfLayers;
            if (groundMask == 0)
                groundMask = (1 << GameConstants.LayerGround) | (1 << GameConstants.LayerObstacle);
        }

        private void Start()
        {
            RefreshCamera();
        }

        // Camera.main does a tag scan; cache it. Re-fetched lazily if the rig
        // spawns late or the main camera changes, so movement never dies silently.
        private void RefreshCamera()
        {
            Camera cam = Camera.main;
            if (cam != null) _cameraTransform = cam.transform;
        }

        private void OnEnable()
        {
            RacerRegistry.Register(this);
        }

        private void OnDisable()
        {
            RacerRegistry.Unregister(this);
        }

        private void Update()
        {
            if (!IsAlive) return;

            if (transform.position.y < GameConstants.WorldKillY)
            {
                Eliminate();
                return;
            }

            UpdateInputLock();
            UpdateGrounded();

            if (_grounded) _lastGroundedTime = Time.time;
            if (_input != null && _input.JumpPressedMasked) _jumpBufferedTime = Time.time;

            bool withinCoyote = Time.time - _lastGroundedTime <= coyoteTime;
            bool jumpBuffered = Time.time - _jumpBufferedTime <= jumpBufferTime;
            if (jumpBuffered && withinCoyote)
            {
                Vector3 v = _rb.linearVelocity;
                v.y = jumpSpeed;
                _rb.linearVelocity = v;
                AudioManager.Play(Sfx.Jump);
                // Consume both so a single press can't double-jump within the windows.
                _jumpBufferedTime = float.NegativeInfinity;
                _lastGroundedTime = float.NegativeInfinity;
            }
        }

        private void FixedUpdate()
        {
            if (!IsAlive || IsFinished) return;
            ApplyMovement();
        }

        private void UpdateInputLock()
        {
            if (_input == null) return;
            _input.InputLocked = Time.time < _inputLockUntil;
        }

        private void UpdateGrounded()
        {
            Vector3 origin = transform.position + Vector3.up * (groundCheckRadius + 0.05f);
            if (Physics.SphereCast(origin, groundCheckRadius, Vector3.down,
                out RaycastHit hit, groundCheckDistance + 0.05f, groundMask, QueryTriggerInteraction.Ignore))
            {
                _grounded = true;
                _groundNormal = hit.normal;
                _onWalkableSlope = Vector3.Angle(hit.normal, Vector3.up) <= maxSlopeAngle;
            }
            else
            {
                _grounded = false;
                _groundNormal = Vector3.up;
                _onWalkableSlope = false;
            }
        }

        private void ApplyMovement()
        {
            Vector2 raw = _input != null ? _input.MoveMasked : Vector2.zero;
            Vector3 desired = ComputeMoveDirection(raw);

            Vector3 vel = _rb.linearVelocity;
            float planarSpeed = new Vector2(vel.x, vel.z).magnitude;

            // Don't override velocity while a knockback impulse is carrying the body:
            // either we're in the post-hit input lock, or we're moving faster than our
            // own run speed because something pushed us. Stomping x/z here is exactly
            // what killed the push mechanic (only the +Y survived). Let physics carry it;
            // drag/friction bleed the extra speed back down to moveSpeed.
            bool knockbackActive = Time.time < _inputLockUntil;
            if (knockbackActive || planarSpeed > moveSpeed + 0.5f)
                return;

            Vector3 currentPlanar = new Vector3(vel.x, 0f, vel.z);
            Vector3 targetPlanar = desired * moveSpeed;
            float accel = _grounded ? groundAcceleration : airAcceleration;
            Vector3 newPlanar = Vector3.MoveTowards(currentPlanar, targetPlanar, accel * Time.fixedDeltaTime);

            vel.x = newPlanar.x;
            vel.z = newPlanar.z;

            // Slope handling. Gated on vel.y <= 0.1 so a fresh jump's upward velocity
            // (set in Update) survives — without the gate, the grounded sphere check
            // can still read true for a frame or two after takeoff and kill the jump.
            if (_grounded && _onWalkableSlope && vel.y <= 0.1f)
            {
                // Re-project the horizontal velocity onto the ramp so motion runs ALONG
                // the surface — driving straight into an upslope bleeds speed and lets
                // gravity win; running off a downslope launches the body into the air.
                Vector3 along = Vector3.ProjectOnPlane(new Vector3(vel.x, 0f, vel.z), _groundNormal);
                vel.x = along.x;
                vel.y = along.y;
                vel.z = along.z;

                // Anti-slide: standing still on a walkable ramp, pre-cancel the downhill
                // component of the gravity PhysX integrates this step so the body holds
                // its spot instead of creeping down the face. The into-surface component
                // is absorbed by the collider contact.
                bool idle = desired.sqrMagnitude < 0.01f && newPlanar.sqrMagnitude < 0.0001f;
                if (idle)
                    vel -= Vector3.ProjectOnPlane(Physics.gravity, _groundNormal) * Time.fixedDeltaTime;
            }

            _rb.linearVelocity = vel;

            if (desired.sqrMagnitude > 0.01f)
            {
                float targetYaw = Mathf.Atan2(desired.x, desired.z) * Mathf.Rad2Deg;
                float yaw = Mathf.SmoothDampAngle(_rb.rotation.eulerAngles.y, targetYaw,
                    ref _turnVelocity, TurnSmoothTime);
                // MoveRotation respects interpolation; transform.rotation on an
                // interpolated dynamic body causes visible jitter.
                _rb.MoveRotation(Quaternion.Euler(0f, yaw, 0f));
            }
        }

        private Vector3 ComputeMoveDirection(Vector2 raw)
        {
            if (raw.sqrMagnitude <= 0.0001f) return Vector3.zero;

            if (_cameraTransform == null) RefreshCamera();

            Vector3 fwd, right;
            if (_cameraTransform != null)
            {
                fwd = _cameraTransform.forward;
                right = _cameraTransform.right;
            }
            else
            {
                // No camera yet — fall back to world axes so the player can still move.
                fwd = Vector3.forward;
                right = Vector3.right;
            }
            fwd.y = 0f; right.y = 0f;
            fwd.Normalize(); right.Normalize();

            Vector3 dir = fwd * raw.y + right * raw.x;
            if (dir.sqrMagnitude > 1f) dir.Normalize();
            return dir;
        }

        public void Knockback(Vector3 force)
        {
            if (!IsAlive) return;
            _rb.AddForce(force + Vector3.up * GameConstants.KnockbackUpward, ForceMode.Impulse);
            _inputLockUntil = Time.time + inputLockOnKnockback;
            if (_animator != null) _animator.TriggerKnockedDown();
            AudioManager.Play(Sfx.Hit);
        }

        public void Eliminate()
        {
            if (!IsAlive) return;
            IsAlive = false;
            if (_collider != null) _collider.enabled = false;
            for (int i = 0; i < _renderers.Length; i++) _renderers[i].enabled = false;
            OnEliminated?.Invoke(this);
            GameEvents.RaiseRacerEliminated(this);
        }

        public void Finish()
        {
            if (IsFinished) return;
            IsFinished = true;
            OnFinished?.Invoke(this);
            GameEvents.RaiseRacerFinished(this);
        }

        public void Respawn(Vector3 position)
        {
            transform.position = position;
            _rb.linearVelocity = Vector3.zero;
            _rb.angularVelocity = Vector3.zero;
            if (_collider != null) _collider.enabled = true;
            for (int i = 0; i < _renderers.Length; i++) _renderers[i].enabled = true;
            IsAlive = true;
            _inputLockUntil = 0f;
        }
    }
}
