using StumbleClone.Core;
using StumbleClone.Game;
using StumbleClone.Player;
using UnityEngine;

namespace StumbleClone.CameraRig
{
    /// Plain-MonoBehaviour orbit follow camera. Avoids Cinemachine to remove a
    /// version-sensitive dependency from the gameplay prototype.
    public sealed class ThirdPersonCamera : MonoBehaviour
    {
        [Header("Target")]
        [SerializeField] private Transform target;

        [Header("Framing")]
        [SerializeField] private float distance = 14f;
        [SerializeField] private float height = 5.2f;
        [SerializeField] private float lateralOffset = 0f;

        [Header("Look")]
        [Tooltip("Degrees per unit of mouse delta. Mouse delta is already frame-independent, so no Time.deltaTime.")]
        [SerializeField] private float mouseSensitivity = 2f;
        [Tooltip("Degrees per second at full stick deflection. Scaled by Time.deltaTime so it's frame-rate independent.")]
        [SerializeField] private float gamepadLookSpeed = 180f;
        [SerializeField] private float pitchMin = -20f;
        [SerializeField] private float pitchMax = 60f;
        [SerializeField] private float initialPitch = 20f;

        [Header("Smoothing")]
        [SerializeField] private float positionSmoothTime = 0.08f;
        [SerializeField] private float rotationSmoothSpeed = 12f;

        [Header("Collision")]
        [SerializeField] private bool collisionEnabled = true;
        [SerializeField] private LayerMask collisionMask = ~0;
        [SerializeField] private float collisionPadding = 0.2f;

        [Header("Juice")]
        [Tooltip("Peak positional shake offset (m) at full trauma. Actual offset scales with trauma^2.")]
        [SerializeField] private float maxShakeOffset = 0.5f;
        [Tooltip("Peak rotational shake (deg) at full trauma. Actual scales with trauma^2.")]
        [SerializeField] private float maxShakeAngle = 2.5f;
        [Tooltip("How fast trauma bleeds back to zero (units/sec).")]
        [SerializeField] private float traumaDecay = 1.5f;
        [Tooltip("Perlin-noise sampling frequency for the shake (higher = jitterier).")]
        [SerializeField] private float shakeFrequency = 22f;
        [Tooltip("How fast a FOV punch eases back to the resting field of view (1/sec). ~12 gives a ~0.2s recovery.")]
        [SerializeField] private float fovRecoverSpeed = 12f;

        private PlayerInputHandler _input;
        private float _yaw;
        private float _pitch;
        private Vector3 _posVelocity;

        // Camera juice — trauma-based shake + a transient FOV punch. All gated behind
        // SettingsStore.ReducedMotion (no shake/FOV kick when the accessibility toggle is on).
        private Camera _camera;
        private float _baseFov;             // resting field of view, captured in Awake
        private float _trauma;              // 0..1; decays linearly, shake scales with its square
        private float _fovPunch;            // additive degrees layered on top of _baseFov, eases out
        private float _shakeSeed;           // per-instance Perlin offset so two cameras don't sync

        private void Awake()
        {
            // The Main Camera is baked into the scenes with the older, closer framing (distance 5,
            // height 2). Floor it to the current pull-back here so the change takes effect without
            // re-baking the binary scenes; a scene that bakes a wider value is still respected.
            distance = Mathf.Max(distance, 14f);
            height = Mathf.Max(height, 5.2f);

            _pitch = initialPitch;
            if (target != null)
            {
                _yaw = target.eulerAngles.y;
                _input = target.GetComponent<PlayerInputHandler>();
            }

            // Never let the camera collide with the player's/bots' own colliders —
            // that's what made the camera zoom into the character's face.
            int selfLayers = (1 << GameConstants.LayerPlayer) | (1 << GameConstants.LayerBot);
            collisionMask &= ~selfLayers;

            // Cache the Camera for the FOV punch; remember its authored FOV as the rest value.
            _camera = GetComponent<Camera>();
            if (_camera != null) _baseFov = _camera.fieldOfView;
            _shakeSeed = Random.value * 100f;
        }

        /// Add camera shake "trauma" (0..1, clamped). Call on real impacts; the shake intensity
        /// scales with trauma^2 and decays automatically. No-op visual effect while
        /// <see cref="SettingsStore.ReducedMotion"/> is on (the value still accumulates harmlessly).
        public void AddTrauma(float amount)
        {
            _trauma = Mathf.Clamp01(_trauma + Mathf.Max(0f, amount));
        }

        /// Snap the field of view up by <paramref name="deg"/> degrees, then ease it back to rest
        /// over ~0.2s. Use on bursts (e.g. a dash) for a punchy whoosh. No-op under ReducedMotion.
        public void PunchFov(float deg)
        {
            if (_camera == null || SettingsStore.ReducedMotion) return;
            _fovPunch = Mathf.Max(_fovPunch, deg);
        }

        private void OnEnable()
        {
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }

        private void OnDisable()
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }

        public void SetTarget(Transform t)
        {
            target = t;
            _input = t != null ? t.GetComponent<PlayerInputHandler>() : null;
        }

        private void LateUpdate()
        {
            if (target == null) return;

            Vector2 look = _input != null ? _input.Look : Vector2.zero;
            // Mouse delta is per-frame movement (frame-independent); gamepad stick is a
            // sustained position and must be scaled by Time.deltaTime to be frame-rate independent.
            float lookScale = (_input != null && _input.LookFromGamepad)
                ? gamepadLookSpeed * Time.deltaTime
                : mouseSensitivity;
            // Player-tunable look-speed multiplier from the Settings menu. Applies uniformly to
            // mouse, gamepad, and touch (touch feeds the gamepad rightStick path).
            lookScale *= SettingsStore.LookSensitivity;
            _yaw += look.x * lookScale;
            _pitch -= look.y * lookScale;
            _pitch = Mathf.Clamp(_pitch, pitchMin, pitchMax);

            Quaternion rot = Quaternion.Euler(_pitch, _yaw, 0f);
            Vector3 focus = target.position + Vector3.up * height + target.right * lateralOffset;
            Vector3 desired = focus - rot * Vector3.forward * distance;

            if (collisionEnabled)
            {
                Vector3 dir = desired - focus;
                float dist = dir.magnitude;
                if (dist > 0.0001f && Physics.SphereCast(focus, collisionPadding, dir / dist,
                        out RaycastHit hit, dist, collisionMask, QueryTriggerInteraction.Ignore))
                {
                    desired = focus + (dir / dist) * Mathf.Max(0.1f, hit.distance - collisionPadding);
                }
            }

            transform.position = Vector3.SmoothDamp(transform.position, desired,
                ref _posVelocity, positionSmoothTime);
            transform.rotation = Quaternion.Slerp(transform.rotation, rot,
                1f - Mathf.Exp(-rotationSmoothSpeed * Time.deltaTime));

            ApplyJuice();
        }

        // Trauma-based shake + FOV punch, layered ON TOP of the settled follow pose so it never
        // fights the SmoothDamp/Slerp above. Fully suppressed while ReducedMotion is on; trauma and
        // the FOV punch are still decayed every frame so they don't pop when the toggle flips off.
        private void ApplyJuice()
        {
            float dt = Time.deltaTime;

            // Linear trauma bleed-off (frame-rate independent via dt).
            if (_trauma > 0f)
                _trauma = Mathf.Max(0f, _trauma - traumaDecay * dt);

            // Ease the FOV punch back to rest. Exponential decay toward 0 reads as a sharp snap-up
            // (set instantly by PunchFov) followed by a quick, smooth recovery in ~0.2s.
            if (_fovPunch > 0f)
            {
                _fovPunch *= Mathf.Exp(-fovRecoverSpeed * dt);
                if (_fovPunch < 0.01f) _fovPunch = 0f;
            }

            bool reduced = SettingsStore.ReducedMotion;

            // Positional + small rotational Perlin shake. Quadratic falloff makes small traumas
            // barely perceptible and big hits slam — the classic "trauma" feel.
            if (!reduced && _trauma > 0f)
            {
                float shake = _trauma * _trauma;
                float t = Time.time * shakeFrequency;
                // Perlin in [0,1] remapped to [-1,1] on three independent channels.
                float nx = Mathf.PerlinNoise(_shakeSeed, t) * 2f - 1f;
                float ny = Mathf.PerlinNoise(_shakeSeed + 17f, t) * 2f - 1f;
                float nz = Mathf.PerlinNoise(_shakeSeed + 31f, t) * 2f - 1f;
                float nroll = Mathf.PerlinNoise(_shakeSeed + 53f, t) * 2f - 1f;

                Vector3 offset = transform.right * (nx * maxShakeOffset * shake)
                                 + transform.up * (ny * maxShakeOffset * shake)
                                 + transform.forward * (nz * maxShakeOffset * 0.5f * shake);
                transform.position += offset;
                transform.rotation *= Quaternion.Euler(
                    ny * maxShakeAngle * shake,
                    nx * maxShakeAngle * shake,
                    nroll * maxShakeAngle * shake);
            }

            // Apply the (eased) FOV punch on top of the resting FOV. When ReducedMotion is on,
            // _fovPunch is held at/decayed to 0 so the camera simply sits at _baseFov.
            if (_camera != null)
                _camera.fieldOfView = _baseFov + (reduced ? 0f : _fovPunch);
        }
    }
}
