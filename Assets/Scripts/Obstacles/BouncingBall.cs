using UnityEngine;

namespace StumbleClone.Obstacles
{
    /// A lively physics ball that bounces around the platform for pinball-style chaos.
    /// A bouncy physic material keeps it alive; a periodic re-impulse stops it from
    /// settling into a slow roll before its lifetime ends.
    [RequireComponent(typeof(Rigidbody), typeof(SphereCollider))]
    public sealed class BouncingBall : ArenaObstacle
    {
        private Rigidbody _rb;
        private float _speed;
        private float _nextKickTime;

        protected override void OnEnable()
        {
            base.OnEnable();
            _rb = GetComponent<Rigidbody>();
        }

        public override void Configure(Transform arenaCenter, float speedScale, float forceScale)
        {
            base.Configure(arenaCenter, speedScale, forceScale);
            if (_rb == null) _rb = GetComponent<Rigidbody>();

            Vector3 toCenter = arenaCenter != null
                ? (arenaCenter.position - transform.position)
                : transform.forward;
            toCenter.y = 0f;
            if (toCenter.sqrMagnitude < 0.0001f) toCenter = transform.forward;

            _speed = 8f * Mathf.Max(0.5f, speedScale);
            _rb.linearVelocity = toCenter.normalized * _speed + Vector3.up * 3f;
            _nextKickTime = Time.time + 1.5f;
        }

        protected override void Update()
        {
            base.Update();
            if (_rb == null) return;

            // If it has slowed to a crawl, kick it back toward the center to keep it bouncing.
            if (Time.time >= _nextKickTime)
            {
                _nextKickTime = Time.time + 1.5f;
                Vector3 planar = new Vector3(_rb.linearVelocity.x, 0f, _rb.linearVelocity.z);
                if (planar.magnitude < _speed * 0.5f && _arenaCenter != null)
                {
                    Vector3 toCenter = _arenaCenter.position - transform.position;
                    toCenter.y = 0f;
                    _rb.AddForce(toCenter.normalized * _speed + Vector3.up * 4f, ForceMode.VelocityChange);
                }
            }
        }
    }
}
