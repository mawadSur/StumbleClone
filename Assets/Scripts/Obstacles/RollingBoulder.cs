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

        protected override void OnEnable()
        {
            base.OnEnable();
            _rb = GetComponent<Rigidbody>();
        }

        public override void Configure(Transform arenaCenter, float speedScale, float forceScale)
        {
            base.Configure(arenaCenter, speedScale, forceScale);

            if (_rb == null) _rb = GetComponent<Rigidbody>();

            // Aim across the platform toward the far side, through the center, with a
            // little lateral jitter so boulders don't all converge on the exact middle.
            Vector3 toCenter = arenaCenter != null
                ? (arenaCenter.position - transform.position)
                : transform.forward;
            toCenter.y = 0f;
            if (toCenter.sqrMagnitude < 0.0001f) toCenter = transform.forward;
            toCenter.Normalize();

            Vector3 lateral = Vector3.Cross(Vector3.up, toCenter);
            Vector3 launch = (toCenter + lateral * Random.Range(-0.35f, 0.35f)).normalized;

            float speed = 9f * Mathf.Max(0.5f, speedScale);
            _rb.linearVelocity = launch * speed;
        }
    }
}
