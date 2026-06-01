using StumbleClone.Core;
using UnityEngine;

namespace StumbleClone.Obstacles
{
    /// Base for spawned hazards in the Knockout (Last Standing) arena. Handles racer
    /// contact -> Knockback and lifetime / fall-off despawn. Subclasses drive movement
    /// and may override the push direction. The racer applies its own upward bias in
    /// Knockback(), so obstacles pass a purely horizontal force.
    public abstract class ArenaObstacle : MonoBehaviour
    {
        [SerializeField] protected float pushForce = GameConstants.DefaultPushForce;
        [SerializeField] protected float lifetime = 12f;
        [Tooltip("Minimum seconds between pushes from this obstacle, so a single pass doesn't stack impulses.")]
        [SerializeField] protected float pushCooldown = 0.25f;

        protected Transform _arenaCenter;
        protected float _spawnTime;
        private float _nextPushTime;

        /// Called by the spawner right after instantiation. speedScale/forceScale carry
        /// the current escalation multipliers.
        public virtual void Configure(Transform arenaCenter, float speedScale, float forceScale)
        {
            _arenaCenter = arenaCenter;
            pushForce *= Mathf.Max(0.01f, forceScale);
        }

        protected virtual void OnEnable()
        {
            _spawnTime = Time.time;
        }

        protected virtual void Update()
        {
            if (Time.time - _spawnTime >= lifetime || transform.position.y < GameConstants.WorldKillY)
                Despawn();
        }

        private void OnCollisionEnter(Collision collision)
        {
            TryPush(collision.collider);
        }

        private void OnTriggerEnter(Collider other)
        {
            TryPush(other);
        }

        private void OnTriggerStay(Collider other)
        {
            TryPush(other);
        }

        protected void TryPush(Collider other)
        {
            if (Time.time < _nextPushTime) return;
            var racer = other.GetComponentInParent<IRacer>();
            if (racer == null || !racer.IsAlive || racer.IsFinished) return;

            Vector3 dir = ComputePushDirection(racer.Transform.position);
            dir.y = 0f;
            if (dir.sqrMagnitude < 0.0001f) dir = transform.forward;
            dir.Normalize();

            racer.Knockback(dir * pushForce);
            _nextPushTime = Time.time + pushCooldown;
        }

        /// Default push shoves the racer radially away from the obstacle. Directional
        /// obstacles (rams, sweeps) override this with their travel/tangent direction.
        protected virtual Vector3 ComputePushDirection(Vector3 racerPosition)
        {
            return racerPosition - transform.position;
        }

        protected virtual void Despawn()
        {
            Destroy(gameObject);
        }
    }
}
