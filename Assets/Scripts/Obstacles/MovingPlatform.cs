using UnityEngine;
using StumbleClone.Core;

namespace StumbleClone.Obstacles
{
    [DisallowMultipleComponent]
    public class MovingPlatform : MonoBehaviour
    {
        [SerializeField] private Transform pointA;
        [SerializeField] private Transform pointB;
        [SerializeField] private float speed = 1f;
        [SerializeField] private float phaseOffset = 0f;

        private void Update()
        {
            if (pointA == null || pointB == null) return;
            // Sin curve normalized to 0..1 keeps motion smooth without snapping at endpoints.
            float t = (Mathf.Sin((Time.time + phaseOffset) * speed) + 1f) * 0.5f;
            transform.position = Vector3.Lerp(pointA.position, pointB.position, t);
        }

        private void OnTriggerStay(Collider other)
        {
            var racer = other.GetComponentInParent<IRacer>();
            if (racer == null) return;
            // Parent the racer so platform motion carries them; rigidbody velocity remains independent.
            if (racer.Transform.parent != transform)
            {
                racer.Transform.SetParent(transform, worldPositionStays: true);
            }
        }

        private void OnTriggerExit(Collider other)
        {
            // Scene unload can fire OnTriggerExit on destroyed objects — guard.
            if (this == null || other == null) return;
            var racer = other.GetComponentInParent<IRacer>();
            if (racer == null || racer.Transform == null) return;
            if (racer.Transform.parent == transform)
            {
                racer.Transform.SetParent(null, worldPositionStays: true);
            }
        }

        private void OnDrawGizmosSelected()
        {
            if (pointA != null && pointB != null)
            {
                Gizmos.color = Color.cyan;
                Gizmos.DrawLine(pointA.position, pointB.position);
                Gizmos.DrawWireSphere(pointA.position, 0.3f);
                Gizmos.DrawWireSphere(pointB.position, 0.3f);
            }
        }

        // Forwards trigger callbacks from a child volume so the platform's solid collider can stay non-trigger.
        public void HandleTriggerStay(Collider other)
        {
            var racer = other.GetComponentInParent<IRacer>();
            if (racer == null) return;
            if (racer.Transform.parent != transform)
            {
                racer.Transform.SetParent(transform, worldPositionStays: true);
            }
        }

        public void HandleTriggerExit(Collider other)
        {
            if (this == null || other == null) return;
            var racer = other.GetComponentInParent<IRacer>();
            if (racer == null || racer.Transform == null) return;
            if (racer.Transform.parent == transform)
            {
                racer.Transform.SetParent(null, worldPositionStays: true);
            }
        }
    }

    public class MovingPlatformTriggerRelay : MonoBehaviour
    {
        public MovingPlatform target;
        private bool _quitting;

        private void OnApplicationQuit() { _quitting = true; }
        private void OnDisable() { _quitting = true; }

        private void OnTriggerStay(Collider other)
        {
            if (_quitting || this == null || target == null || other == null) return;
            target.HandleTriggerStay(other);
        }

        private void OnTriggerExit(Collider other)
        {
            // Guard against scene unload — Unity fires OnTriggerExit on destroyed colliders
            // and reparenting a doomed transform throws MissingReferenceException.
            if (_quitting || this == null || target == null || other == null) return;
            target.HandleTriggerExit(other);
        }
    }
}
