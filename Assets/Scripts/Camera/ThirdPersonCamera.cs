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
        [SerializeField] private float distance = 5f;
        [SerializeField] private float height = 2f;
        [SerializeField] private float lateralOffset = 0f;

        [Header("Look")]
        [Tooltip("Degrees per unit of mouse delta. Mouse delta is already frame-independent, so no Time.deltaTime.")]
        [SerializeField] private float mouseSensitivity = 2f;
        [Tooltip("Degrees per second at full stick deflection. Scaled by Time.deltaTime so it's frame-rate independent.")]
        [SerializeField] private float gamepadLookSpeed = 180f;
        [SerializeField] private float pitchMin = -20f;
        [SerializeField] private float pitchMax = 60f;
        [SerializeField] private float initialPitch = 15f;

        [Header("Smoothing")]
        [SerializeField] private float positionSmoothTime = 0.08f;
        [SerializeField] private float rotationSmoothSpeed = 12f;

        [Header("Collision")]
        [SerializeField] private bool collisionEnabled = true;
        [SerializeField] private LayerMask collisionMask = ~0;
        [SerializeField] private float collisionPadding = 0.2f;

        private PlayerInputHandler _input;
        private float _yaw;
        private float _pitch;
        private Vector3 _posVelocity;

        private void Awake()
        {
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
        }
    }
}
