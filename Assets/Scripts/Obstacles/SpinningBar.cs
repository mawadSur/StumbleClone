using UnityEngine;
using StumbleClone.Core;

namespace StumbleClone.Obstacles
{
    [DisallowMultipleComponent]
    public class SpinningBar : MonoBehaviour
    {
        [SerializeField] private float degreesPerSecond = 90f;
        [SerializeField] private float knockbackMultiplier = 1.5f;
        [SerializeField] private float upwardBoost = GameConstants.KnockbackUpward;

        private void Update()
        {
            transform.Rotate(0f, degreesPerSecond * Time.deltaTime, 0f, Space.Self);
        }

        private void OnCollisionEnter(Collision collision)
        {
            TryKnockback(collision.collider, collision.relativeVelocity);
        }

        // Child colliders forward their hits to the parent via this method.
        public void HandleChildCollision(Collision collision)
        {
            TryKnockback(collision.collider, collision.relativeVelocity);
        }

        private void TryKnockback(Collider other, Vector3 relativeVelocity)
        {
            var racer = other.GetComponentInParent<IRacer>();
            if (racer == null) return;

            Vector3 horizontal = new Vector3(relativeVelocity.x, 0f, relativeVelocity.z);
            // Fallback if hit was nearly head-on: push away from bar center.
            if (horizontal.sqrMagnitude < 0.01f)
            {
                Vector3 away = other.transform.position - transform.position;
                away.y = 0f;
                horizontal = away.normalized * 6f;
            }

            Vector3 force = horizontal * knockbackMultiplier + Vector3.up * upwardBoost;
            racer.Knockback(force);
        }
    }

    // Attach to child collider GameObjects so hits on the bar tip get forwarded.
    public class SpinningBarChild : MonoBehaviour
    {
        private SpinningBar _parent;
        private void Awake() => _parent = GetComponentInParent<SpinningBar>();
        private void OnCollisionEnter(Collision c) { if (_parent != null) _parent.HandleChildCollision(c); }
    }
}
