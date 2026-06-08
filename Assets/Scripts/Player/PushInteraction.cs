using StumbleClone.Audio;
using StumbleClone.CameraRig;
using StumbleClone.Core;
using StumbleClone.Visuals;
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

        private PlayerAnimator _animator;
        private ThirdPersonCamera _cameraRig;   // resolved lazily for the on-hit camera jolt
        private HeldItem _heldItem;             // optional held broom/slipper; null until one is collected

        private void Awake()
        {
            if (input == null) input = GetComponent<PlayerInputHandler>();
            if (selfCollider == null) selfCollider = GetComponent<CapsuleCollider>();
            _animator = GetComponent<PlayerAnimator>();
            _heldItem = GetComponent<HeldItem>(); // may be added later on pickup; re-resolved lazily below
        }

        private void Update()
        {
            if (input == null) return;
            if (!input.PushPressedMasked) return;
            if (Time.time < _nextPushTime) return;

            _nextPushTime = Time.time + pushCooldown;

            // Held-item hook: while the player holds a broom/slipper, the push button USES the item
            // instead of a normal shove. HeldItem is added on pickup, so resolve it lazily if Awake
            // ran before the first collect. With no item, TryUse() returns false and the push runs.
            if (_heldItem == null) _heldItem = GetComponent<HeldItem>();
            if (_heldItem != null && _heldItem.TryUse()) return;

            DoPush();
        }

        private void DoPush()
        {
            AudioManager.Play(Sfx.Push);
            if (_animator != null) _animator.TriggerPush();
            GetCapsulePoints(out Vector3 p1, out Vector3 p2, out float radius);
            int count = Physics.CapsuleCastNonAlloc(p1, p2, radius, transform.forward,
                _hits, pushRange, hitMask, QueryTriggerInteraction.Ignore);

            bool landed = false;
            Vector3 contactPoint = Vector3.zero;
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

                // Capture the contact point of the first racer the push connects with, for the scuff.
                // A zero-distance CapsuleCast returns a (0,0,0) hit point; fall back to the target's
                // position in that case so the puff lands on the body, not at the world origin.
                if (!landed)
                {
                    Vector3 p = _hits[i].point;
                    contactPoint = p == Vector3.zero ? racer.Transform.position : p;
                }
                landed = true;
            }

            // Punchy impact freeze — only when the push actually connected with a racer.
            if (landed)
            {
                HitStop.Do(0.06f);
                // Scuff of dust at the contact point (ReducedMotion-aware inside ImpactPuff).
                ImpactPuff.Spawn(contactPoint, 0.7f);
                // Small camera jolt on the connecting shove. Hard-gated by ReducedMotion inside AddTrauma's
                // consumer (the shake only renders when the toggle is off).
                if (_cameraRig == null)
                {
                    Camera cam = Camera.main;
                    if (cam != null) _cameraRig = cam.GetComponent<ThirdPersonCamera>();
                }
                if (_cameraRig != null) _cameraRig.AddTrauma(0.35f);
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
