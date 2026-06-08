using System;
using System.Collections;
using StumbleClone.Core;
using StumbleClone.Obstacles;
using StumbleClone.Visuals;
using UnityEngine;
using UnityEngine.AI;

namespace StumbleClone.Bots
{
    [RequireComponent(typeof(Rigidbody), typeof(CapsuleCollider), typeof(NavMeshAgent))]
    public sealed class BotController : MonoBehaviour, IRacer
    {
        [SerializeField] private float knockbackRecoverySeconds = 1.2f;
        [SerializeField] private float navResamplingRadius = 3f;
        [SerializeField] private float jumpSpeed = GameConstants.DefaultJumpSpeed;
        [SerializeField] private float jumpAgentLockSeconds = 0.35f;
        [SerializeField] private float pushForce = GameConstants.DefaultPushForce;
        [SerializeField] private float pushCooldown = GameConstants.DefaultPushCooldown;
        [SerializeField] private float pushRange = GameConstants.DefaultPushRange;

        [Header("Edge recovery")]
        [Tooltip("How far to search for the platform/navmesh when knocked off, to scramble back.")]
        [SerializeField] private float recoveryScanRadius = 20f;
        [SerializeField] private float recoveryMoveSpeed = 9f;
        [SerializeField] private float recoveryAirAccel = 28f;
        [Tooltip("Min seconds between recovery jumps (ground hop, then mid-air double jump).")]
        [SerializeField] private float recoveryJumpInterval = 0.32f;
        private const int MaxRecoveryJumps = 2; // ground hop + one mid-air = double jump

        public int racerId;
        public string displayName;
        public BotBehavior behavior;

        /// Optional fallback point a knocked-off bot scrambles toward (e.g. the arena center) when
        /// no navmesh is found within recoveryScanRadius. Set by the behavior.
        public Transform RecoveryAnchor { get; set; }

        private Rigidbody _rb;
        private NavMeshAgent _agent;
        private CapsuleCollider _collider;

        private bool _isAlive = true;
        private bool _isFinished;
        private bool _inKnockback;
        private bool _jumping;
        private bool _recovering;
        private int _recoveryJumpsLeft;
        private float _nextTickTime;
        private float _nextPushTime;
        private float _nextRecoveryJump;
        private float _recoverStartTime; // when the current off-mesh recovery began (for the stuck-rescue timeout)

        // Combat tuning applied per difficulty by the spawner (aggressive bots push harder/more often).
        private float _pushCooldownMul = 1f;
        private float _pushForceMul = 1f;

        // ---- Power-up buffs (additive; all neutral when inactive) ----
        // SPEED scales Agent.speed for a window then restores the captured base. SUPER JUMP scales
        // the jump impulse while live. SHIELD is a one-use flag consumed by Knockback.
        private bool _speedBoostActive;
        private float _speedBoostUntil = float.NegativeInfinity;
        private float _speedBoostBaseSpeed;            // Agent.speed captured at grant, restored on expiry
        private float _jumpMultiplier = 1f;
        private float _jumpBoostUntil = float.NegativeInfinity;
        private bool _shieldActive;

        /// Active jump launch velocity — base jumpSpeed scaled by any live SUPER JUMP buff.
        private float ActiveJumpSpeed => Time.time < _jumpBoostUntil ? jumpSpeed * _jumpMultiplier : jumpSpeed;

        public int RacerId => racerId;
        public string DisplayName => string.IsNullOrEmpty(displayName) ? ("Bot_" + racerId) : displayName;
        public Transform Transform => transform;
        public bool IsAlive => _isAlive;
        public bool IsFinished => _isFinished;
        public bool IsPlayer => false;

        public NavMeshAgent Agent => _agent;
        public Rigidbody Body => _rb;

        public event Action<IRacer> OnFinished;
        public event Action<IRacer> OnEliminated;

        private void Awake()
        {
            _rb = GetComponent<Rigidbody>();
            _collider = GetComponent<CapsuleCollider>();
            _agent = GetComponent<NavMeshAgent>();

            _rb.isKinematic = true;
            _rb.interpolation = RigidbodyInterpolation.Interpolate;
            _rb.collisionDetectionMode = CollisionDetectionMode.Continuous; // match the player; better sweep contact when shoved off a rim
            _agent.speed = GameConstants.DefaultMoveSpeed;
        }

        private void OnEnable()
        {
            RacerRegistry.Register(this);
            behavior?.OnAttach(this);
        }

        private void OnDisable()
        {
            behavior?.OnDetach(this);
            RacerRegistry.Unregister(this);
        }

        private void Update()
        {
            if (!_isAlive || _isFinished) return;

            // Tick the SPEED buff every frame (regardless of knockback/recovery state) so the
            // captured base speed is always restored when the window lapses, even if the agent
            // was disabled/re-enabled meanwhile.
            UpdateSpeedBoost();

            if (transform.position.y < GameConstants.WorldKillY)
            {
                Eliminate();
                return;
            }

            if (_inKnockback) return; // the knockback routine owns the body until it resnaps

            // Knocked off the platform (off the navmesh) but not mid intentional jump — scramble
            // back toward solid ground instead of just falling to our death.
            bool offMesh = _agent == null || !_agent.enabled || !_agent.isOnNavMesh;
            if (offMesh && !_jumping)
            {
                if (!_recovering) { _recovering = true; _recoveryJumpsLeft = MaxRecoveryJumps; _nextRecoveryJump = 0f; _recoverStartTime = Time.time; }
                RecoverTick();
                return;
            }
            _recovering = false;

            float now = Time.time;
            if (behavior != null && now >= _nextTickTime)
            {
                _nextTickTime = now + GameConstants.BotPathRefreshRate;
                behavior.Tick(this);
            }
        }

        /// Air-control + hop back toward the platform after being shoved off. Runs every frame while
        /// the bot is off the navmesh; resnaps and resumes normal AI the moment it regains footing.
        private void RecoverTick()
        {
            if (_rb.isKinematic) _rb.isKinematic = false;

            Vector3 pos = transform.position;

            // Stuck-rescue: if the bot has been off the mesh too long (wedged against a lip, or
            // grounded on geometry the NavMesh doesn't cover) it would otherwise freeze in place or
            // ride physics off the edge — the "bots stand still / die randomly" symptom. Hard-warp it
            // back onto the nearest mesh around its recovery anchor and resume normal AI.
            // Stuck-rescue search radius. Normally a wide 25m sweep around the anchor. BUT during the
            // Knockout shrink the platform can be far smaller than 25m, and sampling that wide can warp
            // the bot onto stale NavMesh that has already shrunk away, or onto the doomed rim it's about
            // to fall off. When a shrink is live, clamp the search to sit well INSIDE the current floor:
            // the centre region (where the Last-Stand recovery anchor sits) is the last to vanish, so a
            // point within ~0.85x the live safe radius of the anchor is always solid ground.
            float rescueRadius = 25f;
            if (ArenaShrinker.Active)
                rescueRadius = Mathf.Clamp(ArenaShrinker.CurrentSafeRadius * 0.85f, 2f, 25f);

            if (Time.time - _recoverStartTime > GameConstants.BotRecoveryTimeout && RecoveryAnchor != null &&
                NavMesh.SamplePosition(RecoveryAnchor.position, out NavMeshHit anchorHit, rescueRadius, NavMesh.AllAreas))
            {
                _rb.linearVelocity = Vector3.zero;
                _rb.angularVelocity = Vector3.zero;
                _rb.isKinematic = true;
                transform.position = anchorHit.position;
                if (_agent != null)
                {
                    _agent.enabled = true;
                    if (_agent.isOnNavMesh) _agent.Warp(anchorHit.position);
                }
                _recovering = false;
                return;
            }

            Vector3 targetPos;
            if (NavMesh.SamplePosition(pos, out NavMeshHit nav, recoveryScanRadius, NavMesh.AllAreas))
                targetPos = nav.position;
            else if (RecoveryAnchor != null)
                targetPos = RecoveryAnchor.position;
            else
                return; // nothing reachable — let physics run its course

            Vector3 to = targetPos - pos; to.y = 0f;
            float d = to.magnitude;
            Vector3 dir = d > 0.01f ? to / d : Vector3.zero;

            // Drive horizontally toward the platform — air control while falling, a run once grounded.
            Vector3 vel = _rb.linearVelocity;
            vel.x = Mathf.MoveTowards(vel.x, dir.x * recoveryMoveSpeed, recoveryAirAccel * Time.deltaTime);
            vel.z = Mathf.MoveTowards(vel.z, dir.z * recoveryMoveSpeed, recoveryAirAccel * Time.deltaTime);
            _rb.linearVelocity = vel;

            if (dir.sqrMagnitude > 0.001f)
                transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(dir), 0.25f);

            bool grounded = IsGrounded();
            if (grounded) _recoveryJumpsLeft = MaxRecoveryJumps; // refill on footing

            // Double jump: a ground hop, then a second boost near the apex / while falling to
            // clear the platform lip and claw back on.
            bool canAirJump = !grounded && _rb.linearVelocity.y < 2.5f;
            if (_recoveryJumpsLeft > 0 && Time.time >= _nextRecoveryJump && (grounded || canAirJump))
            {
                _recoveryJumpsLeft--;
                _nextRecoveryJump = Time.time + recoveryJumpInterval;
                Vector3 jv = _rb.linearVelocity; jv.y = jumpSpeed; _rb.linearVelocity = jv;
            }

            // Back over solid navmesh and grounded — rebind the agent and resume normal behavior.
            if (grounded && NavMesh.SamplePosition(pos, out _, 1.0f, NavMesh.AllAreas))
                ResnapToNavMesh();
        }

        public bool SetDestination(Vector3 worldPos)
        {
            if (!_isAlive || _isFinished || _inKnockback) return false;
            if (_agent == null || !_agent.enabled || !_agent.isOnNavMesh) return false;
            return _agent.SetDestination(worldPos);
        }

        public bool IsGrounded()
        {
            float halfHeight = _collider != null ? _collider.height * 0.5f : 1f;
            Vector3 origin = transform.position + Vector3.up * 0.05f;
            // Strip Player/Bot layers so the ray can't self-hit this bot's own capsule (or another
            // racer) and falsely report grounded — which would stop the recovery double-jump firing.
            int groundMask = ~((1 << GameConstants.LayerPlayer) | (1 << GameConstants.LayerBot));
            return Physics.Raycast(origin, Vector3.down, halfHeight + 0.15f, groundMask, QueryTriggerInteraction.Ignore);
        }

        public void Jump()
        {
            if (!_isAlive || _isFinished || _inKnockback) return;
            if (!IsGrounded()) return;
            StartCoroutine(JumpRoutine());
        }

        private IEnumerator JumpRoutine()
        {
            _jumping = true;
            if (_agent != null && _agent.enabled) _agent.enabled = false;
            _rb.isKinematic = false;
            Vector3 v = _rb.linearVelocity;
            v.y = ActiveJumpSpeed; // SUPER JUMP buff scales this; == jumpSpeed when inactive
            _rb.linearVelocity = v;

            yield return new WaitForSeconds(jumpAgentLockSeconds);

            ResnapToNavMesh();
            _jumping = false; // if the jump carried us off the mesh, RecoverTick takes over
        }

        /// Shove a target away from us (radial). Used when no edge-directed aim is supplied.
        public void TryPush(IRacer target)
        {
            if (target == null) return;
            DoPush(target, target.Transform.position - transform.position);
        }

        /// Shove a target along an explicit world direction — e.g. outward toward the rim, to push
        /// the player off the platform. Range is still gated by actual distance to the target.
        public void TryPushToward(IRacer target, Vector3 worldDir)
        {
            DoPush(target, worldDir);
        }

        private void DoPush(IRacer target, Vector3 dirRaw)
        {
            if (target == null || target == (IRacer)this) return;
            if (Time.time < _nextPushTime) return;
            if (!target.IsAlive || target.IsFinished) return;

            Vector3 toTarget = target.Transform.position - transform.position;
            if (toTarget.magnitude > pushRange) return;

            _nextPushTime = Time.time + pushCooldown * _pushCooldownMul;
            Vector3 d = dirRaw; d.y = 0f;
            Vector3 dir = d.sqrMagnitude > 0.0001f ? d.normalized
                : (toTarget.sqrMagnitude > 0.0001f ? toTarget.normalized : transform.forward);
            target.Knockback(dir * pushForce * _pushForceMul);
        }

        /// Per-difficulty combat scaling applied at spawn — aggressive bots shove harder and more
        /// often. cooldownMul &lt; 1 pushes more frequently; forceMul &gt; 1 hits harder.
        public void SetCombatTuning(float cooldownMul, float forceMul)
        {
            _pushCooldownMul = Mathf.Max(0.1f, cooldownMul);
            _pushForceMul = Mathf.Max(0.1f, forceMul);
        }

        public void Knockback(Vector3 force)
        {
            if (!_isAlive || _isFinished) return;
            // Re-entrant guard: a bot already mid-knockback ignores additional pushes. Without this,
            // chained shoves (7 bots in a scrum) stack KnockbackRoutines that each disable the agent
            // for 1.2s and re-launch the dynamic body — a frame-rate-dependent way to get flung off
            // and "die randomly". One knockback at a time.
            if (_inKnockback) return;
            // SHIELD buff: absorb exactly one incoming hit, then expire. Consumed before the
            // knockback routine starts so the push reads as fully ignored.
            if (_shieldActive)
            {
                _shieldActive = false;
                return;
            }
            // Cosmetic slapstick: a brief limp tumble in the hit direction. Visual-only — runs
            // independently of the knockback routine and never touches the body/agent/impulse.
            // Self-bootstrapping so no prefab wiring is needed.
            (GetComponent<RagdollEffect>() ?? gameObject.AddComponent<RagdollEffect>()).Trigger(force);
            StartCoroutine(KnockbackRoutine(force));
        }

        private IEnumerator KnockbackRoutine(Vector3 force)
        {
            _inKnockback = true;

            if (_agent != null && _agent.enabled) _agent.enabled = false;
            _rb.isKinematic = false;
            // Apply the impulse on a physics step (not mid-Update) so the knockback strength is
            // frame-rate independent — the body has just gone dynamic, so wait one FixedUpdate first.
            yield return new WaitForFixedUpdate();
            _rb.AddForce(force + Vector3.up * GameConstants.KnockbackUpward, ForceMode.Impulse);

            yield return new WaitForSeconds(knockbackRecoverySeconds);

            ResnapToNavMesh();
            _inKnockback = false;
        }

        private void ResnapToNavMesh()
        {
            if (!_isAlive || _isFinished) return;

            if (NavMesh.SamplePosition(transform.position, out NavMeshHit hit, navResamplingRadius, NavMesh.AllAreas))
            {
                // Zero momentum only while still dynamic — setting velocity on an already-kinematic
                // body throws a per-call warning (Unity ignores it). Making it kinematic clears
                // velocity anyway, so the guard is purely to silence the log spam.
                if (!_rb.isKinematic)
                {
                    _rb.linearVelocity = Vector3.zero;
                    _rb.angularVelocity = Vector3.zero;
                }
                _rb.isKinematic = true;
                transform.position = hit.position;
                if (_agent != null)
                {
                    _agent.enabled = true;
                    if (_agent.isOnNavMesh) _agent.Warp(hit.position);
                }
            }
        }

        // ---- Power-up buff grants (called by Powerup pickups) ----

        /// SPEED power-up: scale this bot's NavMeshAgent speed by <paramref name="multiplier"/> for
        /// <paramref name="seconds"/>, then restore the speed captured at grant time. A re-grant while
        /// already boosted refreshes the timer and re-applies the multiplier against the original base
        /// (so stacking can't compound the speed past one multiplier).
        /// <param name="multiplier">Speed scale (&gt; 1 = faster); clamped to at least 1.</param>
        /// <param name="seconds">Duration in seconds the boost stays active.</param>
        public void ApplySpeedBoost(float multiplier, float seconds)
        {
            if (_agent == null) return;
            float mul = Mathf.Max(1f, multiplier);
            // Capture the base only on a fresh grant; refreshing an active boost keeps the original base.
            if (!_speedBoostActive) _speedBoostBaseSpeed = _agent.speed;
            _speedBoostActive = true;
            _speedBoostUntil = Time.time + Mathf.Max(0f, seconds);
            _agent.speed = _speedBoostBaseSpeed * mul;
        }

        /// SHIELD power-up: arm a one-use guard that ignores the next <see cref="Knockback"/>.
        /// The shield does not expire on a timer — it persists until a hit consumes it.
        public void GrantShield()
        {
            _shieldActive = true;
        }

        /// SUPER JUMP power-up: scale this bot's jump impulse by <paramref name="multiplier"/> for
        /// <paramref name="seconds"/>. Affects intentional jumps started while the timer holds; reverts
        /// automatically afterward.
        /// <param name="multiplier">Jump-velocity scale (&gt; 1 = higher); clamped to at least 1.</param>
        /// <param name="seconds">Duration in seconds the boost stays active.</param>
        public void GrantJumpBoost(float multiplier, float seconds)
        {
            _jumpMultiplier = Mathf.Max(1f, multiplier);
            _jumpBoostUntil = Time.time + Mathf.Max(0f, seconds);
        }

        /// Restore the captured base speed once the SPEED window lapses. Cheap no-op while inactive.
        private void UpdateSpeedBoost()
        {
            if (!_speedBoostActive) return;
            if (Time.time < _speedBoostUntil) return;
            _speedBoostActive = false;
            if (_agent != null) _agent.speed = _speedBoostBaseSpeed;
        }

        public void Eliminate()
        {
            if (!_isAlive) return;
            _isAlive = false;

            if (_agent != null && _agent.enabled) _agent.enabled = false;
            _rb.isKinematic = false;

            OnEliminated?.Invoke(this);
            GameEvents.RaiseRacerEliminated(this);
        }

        public void Finish()
        {
            if (_isFinished || !_isAlive) return;
            _isFinished = true;

            if (_agent != null && _agent.enabled)
            {
                _agent.ResetPath();
                _agent.isStopped = true;
            }

            OnFinished?.Invoke(this);
            GameEvents.RaiseRacerFinished(this);
        }

        public void Respawn(Vector3 position)
        {
            _isAlive = true;
            _isFinished = false;
            _inKnockback = false;

            if (_agent != null && _agent.enabled) _agent.enabled = false;
            _rb.linearVelocity = Vector3.zero;
            _rb.angularVelocity = Vector3.zero;
            _rb.isKinematic = true;
            transform.position = position;

            if (NavMesh.SamplePosition(position, out NavMeshHit hit, navResamplingRadius, NavMesh.AllAreas))
            {
                transform.position = hit.position;
            }
            if (_agent != null)
            {
                _agent.enabled = true;
                if (_agent.isOnNavMesh) _agent.isStopped = false;
            }

            _nextTickTime = 0f;
            _nextPushTime = 0f;

            // Clear any in-flight power-up buffs so a respawn starts clean.
            if (_speedBoostActive && _agent != null) _agent.speed = _speedBoostBaseSpeed;
            _speedBoostActive = false;
            _speedBoostUntil = float.NegativeInfinity;
            _jumpMultiplier = 1f;
            _jumpBoostUntil = float.NegativeInfinity;
            _shieldActive = false;
        }
    }
}
