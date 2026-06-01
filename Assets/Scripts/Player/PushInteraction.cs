using StumbleClone.Core;
using UnityEngine;

namespace StumbleClone.Player
{
    /// Capsule-cast forward on push input; applies Knockback to any IRacer hit.
    public sealed class PushInteraction : MonoBehaviour
    {
        [SerializeField] private PlayerInputHandler input;
        [SerializeField] private CapsuleCollider selfCollider;
        [SerializeField] private float pushForce = GameConstants.DefaultPushForce;
        [SerializeField] private float pushRange = GameConstants.DefaultPushRange;
        [SerializeField] private float pushCooldown = GameConstants.DefaultPushCooldown;
        [SerializeField] private LayerMask hitMask = ~0;
        [SerializeField] private float upwardForceShare = 0f;

        private const int HitBufferSize = 8;
        private readonly RaycastHit[] _hits = new RaycastHit[HitBufferSize];
        private float _nextPushTime;

        private void Awake()
        {
            if (input == null) input = GetComponent<PlayerInputHandler>();
            if (selfCollider == null) selfCollider = GetComponent<CapsuleCollider>();
        }

        private void Update()
        {
            if (input == null) return;
            if (!input.PushPressedMasked) return;
            if (Time.time < _nextPushTime) return;

            _nextPushTime = Time.time + pushCooldown;
            DoPush();
        }

        private void DoPush()
        {
            GetCapsulePoints(out Vector3 p1, out Vector3 p2, out float radius);
            int count = Physics.CapsuleCastNonAlloc(p1, p2, radius, transform.forward,
                _hits, pushRange, hitMask, QueryTriggerInteraction.Ignore);

            for (int i = 0; i < count; i++)
            {
                Collider col = _hits[i].collider;
                if (col == null) continue;
                IRacer racer = col.GetComponentInParent<IRacer>();
                if (racer == null) continue;
                if (ReferenceEquals(racer.Transform, transform)) continue;

                Vector3 dir = transform.forward;
                dir.y += upwardForceShare;
                racer.Knockback(dir.normalized * pushForce);
            }
        }

        private void GetCapsulePoints(out Vector3 p1, out Vector3 p2, out float radius)
        {
            if (selfCollider != null)
            {
                Vector3 center = transform.TransformPoint(selfCollider.center);
                float half = Mathf.Max(0f, selfCollider.height * 0.5f - selfCollider.radius);
                Vector3 axis = transform.up * half;
                p1 = center + axis;
                p2 = center - axis;
                radius = selfCollider.radius;
            }
            else
            {
                Vector3 center = transform.position + Vector3.up * 1f;
                p1 = center + Vector3.up * 0.5f;
                p2 = center - Vector3.up * 0.5f;
                radius = 0.4f;
            }
        }

        private void OnDrawGizmosSelected()
        {
            GetCapsulePoints(out Vector3 p1, out Vector3 p2, out float radius);
            Vector3 fwd = transform.forward * pushRange;
            Gizmos.color = new Color(1f, 0.5f, 0f, 0.7f);
            Gizmos.DrawWireSphere(p1, radius);
            Gizmos.DrawWireSphere(p2, radius);
            Gizmos.DrawWireSphere(p1 + fwd, radius);
            Gizmos.DrawWireSphere(p2 + fwd, radius);
            Gizmos.DrawLine(p1, p1 + fwd);
            Gizmos.DrawLine(p2, p2 + fwd);
        }
    }
}
