using System;
using System.Collections;
using StumbleClone.Core;
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

        // Combat tuning applied per difficulty by the spawner (aggressive bots push harder/more often).
        private float _pushCooldownMul = 1f;
        private float _pushForceMul = 1f;

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
            _rb.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
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
                if (!_recovering) { _recovering = true; _recoveryJumpsLeft = MaxRecoveryJumps; _nextRecoveryJump = 0f; }
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
            return Physics.Raycast(origin, Vector3.down, halfHeight + 0.15f, ~0, QueryTriggerInteraction.Ignore);
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
            v.y = jumpSpeed;
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
            StartCoroutine(KnockbackRoutine(force));
        }

        private IEnumerator KnockbackRoutine(Vector3 force)
        {
            _inKnockback = true;

            if (_agent != null && _agent.enabled) _agent.enabled = false;
            _rb.isKinematic = false;
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
                _rb.linearVelocity = Vector3.zero;
                _rb.angularVelocity = Vector3.zero;
                _rb.isKinematic = true;
                transform.position = hit.position;
                if (_agent != null)
                {
                    _agent.enabled = true;
                    if (_agent.isOnNavMesh) _agent.Warp(hit.position);
                }
            }
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
        }
    }
}
