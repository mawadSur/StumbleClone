using UnityEngine;
using StumbleClone.Core;

namespace StumbleClone.Obstacles
{
    [DisallowMultipleComponent]
    public class SwingingHammer : MonoBehaviour
    {
        [SerializeField] private float speed = 2f;
        [SerializeField] private float maxAngle = 60f;
        [SerializeField] private float knockbackMultiplier = 1.5f;
        [SerializeField] private float upwardBoost = GameConstants.KnockbackUpward;
        [SerializeField] private float phaseOffset = 0f;

        private Quaternion _initialLocalRot;

        private void Awake()
        {
            _initialLocalRot = transform.localRotation;
        }

        private void Update()
        {
            float angle = Mathf.Sin((Time.time + phaseOffset) * speed) * maxAngle;
            transform.localRotation = _initialLocalRot * Quaternion.Euler(angle, 0f, 0f);
        }

        private void OnCollisionEnter(Collision collision)
        {
            HandleHit(collision.collider, collision.relativeVelocity);
        }

        public void HandleChildCollision(Collision collision)
        {
            HandleHit(collision.collider, collision.relativeVelocity);
        }

        private void HandleHit(Collider other, Vector3 relativeVelocity)
        {
            var racer = other.GetComponentInParent<IRacer>();
            if (racer == null) return;

            Vector3 horizontal = new Vector3(relativeVelocity.x, 0f, relativeVelocity.z);
            if (horizontal.sqrMagnitude < 0.01f)
            {
                Vector3 away = other.transform.position - transform.position;
                away.y = 0f;
                horizontal = away.normalized * 8f;
            }

            Vector3 force = horizontal * knockbackMultiplier + Vector3.up * upwardBoost;
            racer.Knockback(force);
        }
    }

    public class SwingingHammerChild : MonoBehaviour
    {
        private SwingingHammer _parent;
        private void Awake() => _parent = GetComponentInParent<SwingingHammer>();
        private void OnCollisionEnter(Collision c) { if (_parent != null) _parent.HandleChildCollision(c); }
    }
}
