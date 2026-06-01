using UnityEngine;

namespace StumbleClone.Obstacles
{
    /// Kinematic block that shoots straight in from an edge, sweeping a lane across the
    /// arena, then despawns once it has travelled past the far side. Uses a trigger and
    /// manual knockback (kinematic-vs-kinematic contact wouldn't push on its own), so the
    /// shove is always in the ram's travel direction.
    [RequireComponent(typeof(Rigidbody), typeof(BoxCollider))]
    public sealed class SlidingRam : ArenaObstacle
    {
        private Rigidbody _rb;
        private Vector3 _travelDir;
        private float _speed;
        private float _maxTravel;
        private float _travelled;

        protected override void OnEnable()
        {
            base.OnEnable();
            _rb = GetComponent<Rigidbody>();
            _rb.isKinematic = true;
            _rb.useGravity = false;
        }

        public override void Configure(Transform arenaCenter, float speedScale, float forceScale)
        {
            base.Configure(arenaCenter, speedScale, forceScale);

            Vector3 toCenter = arenaCenter != null
                ? (arenaCenter.position - transform.position)
                : transform.forward;
            toCenter.y = 0f;
            if (toCenter.sqrMagnitude < 0.0001f) toCenter = transform.forward;
            _travelDir = toCenter.normalized;

            transform.rotation = Quaternion.LookRotation(_travelDir, Vector3.up);
            _speed = 11f * Mathf.Max(0.5f, speedScale);
            // Travel across the whole diameter plus margin, then retire.
            _maxTravel = (arenaCenter != null ? toCenter.magnitude : 20f) * 2f + 6f;
        }

        protected override void Update()
        {
            base.Update();
            if (_rb == null) return;

            float step = _speed * Time.deltaTime;
            _rb.MovePosition(_rb.position + _travelDir * step);
            _travelled += step;
            if (_travelled >= _maxTravel) Despawn();
        }

        protected override Vector3 ComputePushDirection(Vector3 racerPosition)
        {
            // Always shove in the direction the ram is travelling.
            return _travelDir;
        }
    }
}
