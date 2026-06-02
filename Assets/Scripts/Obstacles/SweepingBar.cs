using UnityEngine;

namespace StumbleClone.Obstacles
{
    /// A bar anchored at the rim that sweeps across the surface like a windshield wiper,
    /// flinging racers along its arc (outward + sideways). Rotates about an external pivot
    /// so the inner tip travels fast across the platform.
    [RequireComponent(typeof(Rigidbody), typeof(BoxCollider))]
    public sealed class SweepingBar : ArenaObstacle
    {
        private Rigidbody _rb;
        private Vector3 _pivot;
        private float _angularSpeed = 90f; // deg/s, sign set in Configure
        private bool _hasPivot;
        private bool _hasExplicitSpin;
        private float _explicitSign = 1f;

        protected override void OnEnable()
        {
            base.OnEnable();
            _rb = GetComponent<Rigidbody>();
            _rb.isKinematic = true;
            _rb.useGravity = false;
        }

        /// Rim anchor the bar rotates around. Called by the spawner after placement.
        public void SetPivot(Vector3 pivot)
        {
            _pivot = pivot;
            _hasPivot = true;
        }

        /// Pins the sweep direction instead of randomizing it, so the bar's arc matches the
        /// wave's intent. `clockwise` true sweeps clockwise (viewed from above), false counter.
        /// Call before or after Configure — Configure re-reads this flag.
        public void SetSpin(bool clockwise)
        {
            _explicitSign = clockwise ? -1f : 1f;
            _hasExplicitSpin = true;
            // Re-apply the sign to a magnitude already chosen by Configure.
            _angularSpeed = Mathf.Abs(_angularSpeed) * _explicitSign;
        }

        public override void Configure(Transform arenaCenter, float speedScale, float forceScale)
        {
            base.Configure(arenaCenter, speedScale, forceScale);
            float sign = _hasExplicitSpin ? _explicitSign : (Random.value < 0.5f ? -1f : 1f);
            _angularSpeed = 70f * Mathf.Max(0.5f, speedScale) * sign;
        }

        protected override void Update()
        {
            base.Update();
            if (!_hasPivot) return;
            transform.RotateAround(_pivot, Vector3.up, _angularSpeed * Time.deltaTime);
        }

        protected override Vector3 ComputePushDirection(Vector3 racerPosition)
        {
            // Tangent of the sweep at the racer's radius, in the direction of rotation.
            Vector3 radial = racerPosition - _pivot;
            radial.y = 0f;
            Vector3 tangent = Vector3.Cross(Vector3.up, radial);
            return _angularSpeed >= 0f ? tangent : -tangent;
        }
    }
}
