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

        public int racerId;
        public string displayName;
        public BotBehavior behavior;

        private Rigidbody _rb;
        private NavMeshAgent _agent;
        private CapsuleCollider _collider;

        private bool _isAlive = true;
        private bool _isFinished;
        private bool _inKnockback;
        private float _nextTickTime;
        private float _nextPushTime;

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

            float now = Time.time;
            if (!_inKnockback && behavior != null && now >= _nextTickTime)
            {
                _nextTickTime = now + GameConstants.BotPathRefreshRate;
                behavior.Tick(this);
            }
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
            if (_agent != null && _agent.enabled) _agent.enabled = false;
            _rb.isKinematic = false;
            Vector3 v = _rb.linearVelocity;
            v.y = jumpSpeed;
            _rb.linearVelocity = v;

            yield return new WaitForSeconds(jumpAgentLockSeconds);

            ResnapToNavMesh();
        }

        public void TryPush(IRacer target)
        {
            if (target == null || target == (IRacer)this) return;
            if (Time.time < _nextPushTime) return;
            if (!target.IsAlive || target.IsFinished) return;

            Vector3 toTarget = target.Transform.position - transform.position;
            float dist = toTarget.magnitude;
            if (dist > pushRange) return;

            _nextPushTime = Time.time + pushCooldown;
            Vector3 dir = dist > 0.001f ? toTarget / dist : transform.forward;
            target.Knockback(dir * pushForce);
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
