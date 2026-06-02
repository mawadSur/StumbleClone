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

        [Header("Air Dash")]
        [Tooltip("A second jump press while airborne fires a horizontal dash instead of a jump.")]
        [SerializeField] private bool airDashEnabled = true;
        [Tooltip("Planar speed of the dash burst (units/s).")]
        [SerializeField] private float dashSpeed = GameConstants.DefaultDashSpeed;
        [Tooltip("How long the dash holds its speed with gravity cancelled (s).")]
        [SerializeField] private float dashDuration = GameConstants.DefaultDashDuration;

        [Header("Identity")]
        [SerializeField] private int racerId = 0;
        [SerializeField] private string displayName = "Player";

        [Header("Tuning")]
        [SerializeField] private float inputLockOnKnockback = 0.3f;

        [Header("Spawn Safety")]
        [Tooltip("Seconds after spawn during which the player can't be eliminated; falls/pushes snap back to the spawn point instead. Guards against being shoved off the map by a spawn-frame collider overlap.")]
        [SerializeField] private float spawnGrace = 1.5f;
        [Tooltip("Log spawn diagnostics (ground below, overlapping colliders) once on Start.")]
        [SerializeField] private bool logSpawnDiagnostics = true;

        private const float TurnSmoothTime = 0.08f;

        private Rigidbody _rb;
        private CapsuleCollider _collider;
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
        private Vector3 _spawnPoint;
        private float _spawnSafeUntil;
        private float _lastResnapLogTime = float.NegativeInfinity;
        private bool _dashArmed;            // one dash per airborne stint; re-armed on landing
        private bool _dashing;
        private float _dashUntil = float.NegativeInfinity;

        // ---- Power-up buffs (additive; all neutral when inactive) ----
        // Each buff is a timed multiplier (1 = no effect) plus an expiry stamp. They feed into
        // the existing movement/jump code through small accessor helpers so the base logic is
        // untouched when no buff is live. The shield is a one-use flag consumed by Knockback.
        private float _speedMultiplier = 1f;
        private float _speedBoostUntil = float.NegativeInfinity;
        private float _jumpMultiplier = 1f;
        private float _jumpBoostUntil = float.NegativeInfinity;
        private bool _shieldActive;

        /// Active planar speed cap — base moveSpeed scaled by any live SPEED buff. Used in place of
        /// the raw moveSpeed everywhere the movement code clamps or targets run speed, so the boost
        /// is transparent and reverts automatically the moment the timer lapses.
        private float ActiveMoveSpeed => Time.time < _speedBoostUntil ? moveSpeed * _speedMultiplier : moveSpeed;

        /// Active jump launch velocity — base jumpSpeed scaled by any live SUPER JUMP buff.
        private float ActiveJumpSpeed => Time.time < _jumpBoostUntil ? jumpSpeed * _jumpMultiplier : jumpSpeed;

        // ---- Read-only buff state (for the HUD; additive, no behavior change) ----

        /// True while the one-use SHIELD guard is armed (consumed by the next <see cref="Knockback"/>).
        public bool ShieldActive => _shieldActive;

        /// Seconds remaining on the SPEED buff, or 0 when inactive.
        public float SpeedBoostRemaining => Mathf.Max(0f, _speedBoostUntil - Time.time);

        /// Seconds remaining on the SUPER JUMP buff, or 0 when inactive.
        public float JumpBoostRemaining => Mathf.Max(0f, _jumpBoostUntil - Time.time);

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
            _spawnPoint = transform.position;
            _spawnSafeUntil = Time.time + spawnGrace;
            if (logSpawnDiagnostics) RunSpawnDiagnostics();
        }

        // One-shot spawn report: what's under the player, and is anything overlapping the
        // capsule right now (which would make PhysX shove the player off the map). Read the
        // Console for "[PlayerSpawn]" right after pressing Play.
        private void RunSpawnDiagnostics()
        {
            Vector3 p = transform.position;

            if (Physics.Raycast(p + Vector3.up * 2f, Vector3.down, out RaycastHit hit, 100f,
                    groundMask, QueryTriggerInteraction.Ignore))
                Debug.Log($"[PlayerSpawn] pos={p} | ground '{hit.collider.name}' {hit.distance:F2}m below (top y={hit.point.y:F2})");
            else
                Debug.LogWarning($"[PlayerSpawn] pos={p} | NO ground within 100m below — spawn point is over a gap/off the level. Player will fall.");

            // Capsule overlap test against everything except triggers.
            Vector3 c = p + _collider.center;
            float half = Mathf.Max(_collider.height * 0.5f - _collider.radius, 0f);
            Vector3 top = c + Vector3.up * half;
            Vector3 bot = c - Vector3.up * half;
            Collider[] overlaps = Physics.OverlapCapsule(top, bot, _collider.radius + 0.02f, ~0,
                QueryTriggerInteraction.Ignore);
            int others = 0;
            for (int i = 0; i < overlaps.Length; i++)
            {
                Collider o = overlaps[i];
                if (o == _collider || o.transform.IsChildOf(transform)) continue;
                others++;
                Debug.LogWarning($"[PlayerSpawn] OVERLAPPING '{o.name}' (layer {LayerMask.LayerToName(o.gameObject.layer)}) — this collider is stacked on the player and will push it off at spawn.");
            }
            if (others == 0)
                Debug.Log("[PlayerSpawn] no colliders overlapping the player at spawn (no push expected).");
        }

        // During the spawn-grace window, a fall/elimination is treated as a spawn glitch:
        // snap back to the spawn point and kill momentum instead of dying.
        private void ReSnapToSpawn()
        {
            transform.position = _spawnPoint;
            _rb.linearVelocity = Vector3.zero;
            _rb.angularVelocity = Vector3.zero;
            if (logSpawnDiagnostics && Time.time - _lastResnapLogTime > 0.25f)
            {
                _lastResnapLogTime = Time.time;
                Debug.LogWarning($"[PlayerSpawn] recovered to spawn {_spawnPoint} during grace window — something is displacing the player at start.");
            }
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
                if (Time.time < _spawnSafeUntil) { ReSnapToSpawn(); return; }
                Eliminate();
                return;
            }

            UpdateInputLock();
            UpdateGrounded();

            if (_grounded)
            {
                _lastGroundedTime = Time.time;
                _dashArmed = true; // re-arm the dash every time we touch ground
            }

            bool jumpPressed = _input != null && _input.JumpPressedMasked;
            if (jumpPressed) _jumpBufferedTime = Time.time;

            bool withinCoyote = Time.time - _lastGroundedTime <= coyoteTime;
            bool jumpBuffered = Time.time - _jumpBufferedTime <= jumpBufferTime;
            if (jumpBuffered && withinCoyote)
            {
                Vector3 v = _rb.linearVelocity;
                v.y = ActiveJumpSpeed;
                _rb.linearVelocity = v;
                AudioManager.Play(Sfx.Jump);
                // Consume both so a single press can't double-jump within the windows.
                _jumpBufferedTime = float.NegativeInfinity;
                _lastGroundedTime = float.NegativeInfinity;
            }
            else if (airDashEnabled && jumpPressed && !_grounded && !withinCoyote && _dashArmed)
            {
                // Second jump press while airborne -> dash instead of a (non-existent) double jump.
                Dash();
                _dashArmed = false;
                _jumpBufferedTime = float.NegativeInfinity;
            }
        }

        private void FixedUpdate()
        {
            if (!IsAlive || IsFinished) return;

            if (_dashing)
            {
                if (Time.time < _dashUntil)
                {
                    // Cancel gravity so the burst stays flat and snappy instead of arcing down.
                    // ApplyMovement won't stomp it: planar speed exceeds moveSpeed, so the
                    // knockback guard lets physics carry the dash.
                    _rb.AddForce(-Physics.gravity, ForceMode.Acceleration);
                }
                else
                {
                    // Dash window over — clamp planar speed back to moveSpeed so control returns
                    // immediately (the body has no drag to bleed the burst off on its own).
                    Vector3 v = _rb.linearVelocity;
                    Vector3 planar = new Vector3(v.x, 0f, v.z);
                    float cap = ActiveMoveSpeed;
                    if (planar.magnitude > cap)
                    {
                        planar = planar.normalized * cap;
                        v.x = planar.x;
                        v.z = planar.z;
                        _rb.linearVelocity = v;
                    }
                    _dashing = false;
                }
            }

            ApplyMovement();
        }

        private void Dash()
        {
            // Dash toward current input; if there's no input, dash where the body faces.
            Vector2 raw = _input != null ? _input.MoveMasked : Vector2.zero;
            Vector3 dir = ComputeMoveDirection(raw);
            if (dir.sqrMagnitude < 0.01f) dir = transform.forward;
            dir.y = 0f;
            if (dir.sqrMagnitude < 0.0001f) dir = Vector3.forward;
            dir.Normalize();

            // Flat horizontal burst — drop any falling velocity so the dash reads clean.
            _rb.linearVelocity = dir * dashSpeed;
            _dashUntil = Time.time + dashDuration;
            _dashing = true;

            // Face the dash direction immediately for readability.
            float yaw = Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg;
            _rb.MoveRotation(Quaternion.Euler(0f, yaw, 0f));

            AudioManager.Play(Sfx.Jump);
            if (_animator != null) _animator.TriggerDash();
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

            // SPEED buff scales the run cap transparently (== moveSpeed when no buff is live).
            float activeMoveSpeed = ActiveMoveSpeed;

            // Don't override velocity while a knockback impulse is carrying the body:
            // either we're in the post-hit input lock, or we're moving faster than our
            // own run speed because something pushed us. Stomping x/z here is exactly
            // what killed the push mechanic (only the +Y survived). Let physics carry it;
            // drag/friction bleed the extra speed back down to moveSpeed.
            bool knockbackActive = Time.time < _inputLockUntil;
            if (knockbackActive || planarSpeed > activeMoveSpeed + 0.5f)
                return;

            Vector3 currentPlanar = new Vector3(vel.x, 0f, vel.z);
            Vector3 targetPlanar = desired * activeMoveSpeed;
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
            // SHIELD buff: absorb exactly one incoming hit, then expire. Consumed before any
            // impulse / input lock / animation so the push reads as fully ignored.
            if (_shieldActive)
            {
                _shieldActive = false;
                return;
            }
            _rb.AddForce(force + Vector3.up * GameConstants.KnockbackUpward, ForceMode.Impulse);
            _inputLockUntil = Time.time + inputLockOnKnockback;
            if (_animator != null) _animator.TriggerKnockedDown();
            AudioManager.Play(Sfx.Hit);
        }

        // ---- Power-up buff grants (called by Powerup pickups) ----

        /// SPEED power-up: scale the player's run speed by <paramref name="multiplier"/> for
        /// <paramref name="seconds"/>. Re-grants refresh the timer and adopt the new multiplier.
        /// Reverts automatically — the movement code reads the scaled cap only while the timer holds.
        /// <param name="multiplier">Run-speed scale (&gt; 1 = faster); clamped to at least 1.</param>
        /// <param name="seconds">Duration in seconds the boost stays active.</param>
        public void ApplySpeedBoost(float multiplier, float seconds)
        {
            _speedMultiplier = Mathf.Max(1f, multiplier);
            _speedBoostUntil = Time.time + Mathf.Max(0f, seconds);
        }

        /// SHIELD power-up: arm a one-use guard that ignores the next <see cref="Knockback"/>.
        /// The shield does not expire on a timer — it persists until a hit consumes it.
        public void GrantShield()
        {
            _shieldActive = true;
        }

        /// SUPER JUMP power-up: scale the jump launch velocity by <paramref name="multiplier"/>
        /// for <paramref name="seconds"/>. Affects jumps started while the timer holds; reverts
        /// automatically afterward.
        /// <param name="multiplier">Jump-velocity scale (&gt; 1 = higher); clamped to at least 1.</param>
        /// <param name="seconds">Duration in seconds the boost stays active.</param>
        public void GrantJumpBoost(float multiplier, float seconds)
        {
            _jumpMultiplier = Mathf.Max(1f, multiplier);
            _jumpBoostUntil = Time.time + Mathf.Max(0f, seconds);
        }

        public void Eliminate()
        {
            if (!IsAlive) return;
            // Spawn grace: a kill-zone hit or fall in the first moments is almost certainly a
            // spawn-placement/overlap glitch, not real play. Recover instead of dying.
            if (Time.time < _spawnSafeUntil)
            {
                ReSnapToSpawn();
                return;
            }
            IsAlive = false;
            if (_collider != null) _collider.enabled = false;
            SetRenderersEnabled(false);
            OnEliminated?.Invoke(this);
            GameEvents.RaiseRacerEliminated(this);
        }

        // Fetch renderers live (not a cached array): a skin swap may have replaced the visual
        // model after Awake, so a stale cache could hold destroyed renderers — touching one would
        // throw and abort Eliminate before the elimination event fires (round never ends).
        private void SetRenderersEnabled(bool on)
        {
            var renderers = GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < renderers.Length; i++)
                if (renderers[i] != null) renderers[i].enabled = on;
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
            SetRenderersEnabled(true);
            IsAlive = true;
            _inputLockUntil = 0f;
            _dashArmed = true;
            _dashing = false;
            _dashUntil = float.NegativeInfinity;
            // Clear any in-flight power-up buffs so a respawn starts clean.
            _speedMultiplier = 1f;
            _speedBoostUntil = float.NegativeInfinity;
            _jumpMultiplier = 1f;
            _jumpBoostUntil = float.NegativeInfinity;
            _shieldActive = false;
        }
    }
}
