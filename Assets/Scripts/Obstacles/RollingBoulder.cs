using UnityEngine;

namespace StumbleClone.Obstacles
{
    /// Heavy physics sphere launched across the arena. Rolls under gravity and
    /// shoves anything in its path. Physical contact moves the (dynamic) player on
    /// its own; the manual Knockback in the base also handles (kinematic) bots.
    [RequireComponent(typeof(Rigidbody), typeof(SphereCollider))]
    public sealed class RollingBoulder : ArenaObstacle
    {
        private Rigidbody _rb;
        private bool _hasExplicitDir;
        private Vector3 _explicitDir;

        protected override void OnEnable()
        {
            base.OnEnable();
            _rb = GetComponent<Rigidbody>();
        }

        /// Pins the boulder's launch direction to the wave's intended heading, replacing the
        /// random lateral jitter so the roll matches the telegraphed rim octant. `direction`
        /// is the world-space heading (inward across the arena); its Y is ignored. Call before
        /// or after Configure — Configure re-reads this flag.
        public void SetLaunchDirection(Vector3 direction)
        {
            direction.y = 0f;
            if (direction.sqrMagnitude < 0.0001f) return;
            _explicitDir = direction.normalized;
            _hasExplicitDir = true;

            // If Configure already ran, retroactively correct the velocity magnitude in place.
            if (_rb != null && _rb.linearVelocity.sqrMagnitude > 0.0001f)
                _rb.linearVelocity = _explicitDir * _rb.linearVelocity.magnitude;
        }

        public override void Configure(Transform arenaCenter, float speedScale, float forceScale)
        {
            base.Configure(arenaCenter, speedScale, forceScale);

            if (_rb == null) _rb = GetComponent<Rigidbody>();

            // Aim across the platform toward the far side, through the center.
            Vector3 toCenter = arenaCenter != null
                ? (arenaCenter.position - transform.position)
                : transform.forward;
            toCenter.y = 0f;
            if (toCenter.sqrMagnitude < 0.0001f) toCenter = transform.forward;
            toCenter.Normalize();

            Vector3 launch;
            if (_hasExplicitDir)
            {
                // Honour the wave's direction exactly so the roll matches its telegraph.
                launch = _explicitDir;
            }
            else
            {
                // No wave direction supplied: add a little lateral jitter so boulders don't
                // all converge on the exact middle.
                Vector3 lateral = Vector3.Cross(Vector3.up, toCenter);
                launch = (toCenter + lateral * Random.Range(-0.35f, 0.35f)).normalized;
            }

            float speed = 9f * Mathf.Max(0.5f, speedScale);
            _rb.linearVelocity = launch * speed;
        }
    }
}
