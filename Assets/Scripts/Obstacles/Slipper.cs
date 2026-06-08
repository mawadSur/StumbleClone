using StumbleClone.Audio;
using StumbleClone.Core;
using StumbleClone.Visuals;
using UnityEngine;

namespace StumbleClone.Obstacles
{
    /// A thrown SLIPPER projectile — a small flat cube with a Rigidbody, launched forward by the
    /// player's held <see cref="StumbleClone.Player.HeldItem"/>. On contact with any IRacer that is
    /// NOT the thrower it knocks that racer down (a lateral shove plus an upward pop) and destroys
    /// itself. It also self-destructs when it hits the ground/a wall or after a short lifetime so a
    /// missed throw never litters the arena.
    ///
    /// Built entirely at runtime via <see cref="Spawn"/> (procedural cube + <see cref="RuntimeMaterial"/>,
    /// no art assets). The thrower's colliders are ignored from frame zero, and the projectile is
    /// additionally "armed" only after a brief delay so it can't clip the thrower's body on launch.
    [DisallowMultipleComponent]
    public sealed class Slipper : MonoBehaviour
    {
        // ---- Tuning (local — projectile feel) ----
        private const float Lifetime = 4f;          // auto-destroy after this many seconds
        private const float ArmDelay = 0.15f;       // ignore all hits for this long after launch
        private const float KnockForce = 11f;       // lateral knock magnitude on a racer hit
        private const float KnockUpward = 0.5f;     // upward share folded into the knock direction
        private const float SpinSpeed = 540f;       // deg/s tumble for readability
        private static readonly Vector3 BodyScale = new Vector3(0.5f, 0.16f, 0.32f); // small + flat
        private static readonly Color SlipperColor = new Color(0.85f, 0.25f, 0.35f); // slipper-ish red

        private Transform _thrower;     // never knocked by our own throw
        private float _armTime;         // Time.time after which hits register
        private float _deathTime;       // Time.time at which we self-destruct
        private bool _consumed;         // guards against a double hit in the same frame

        private int _groundMask;

        /// Build and launch a slipper. Call from the thrower's item logic.
        /// <param name="position">World spawn point (already in front of the thrower).</param>
        /// <param name="velocity">Initial linear velocity (direction * speed, with any upward arc).</param>
        /// <param name="thrower">The racer transform that threw it — never hit by this projectile.</param>
        /// <param name="throwerColliders">The thrower's colliders to ignore so it can't clip them.</param>
        public static Slipper Spawn(Vector3 position, Vector3 velocity, Transform thrower, Collider[] throwerColliders)
        {
            var go = new GameObject("Slipper");
            go.transform.position = position;
            // Face the throw direction so the flat cube reads like it's flying edge-on.
            if (velocity.sqrMagnitude > 0.0001f)
                go.transform.rotation = Quaternion.LookRotation(velocity.normalized, Vector3.up);

            var slipper = go.AddComponent<Slipper>();
            slipper.Init(velocity, thrower, throwerColliders);
            return slipper;
        }

        private void Init(Vector3 velocity, Transform thrower, Collider[] throwerColliders)
        {
            _thrower = thrower;
            _armTime = Time.time + ArmDelay;
            _deathTime = Time.time + Lifetime;
            _groundMask = (1 << GameConstants.LayerGround) | (1 << GameConstants.LayerObstacle);

            var col = gameObject.AddComponent<BoxCollider>();
            col.size = BodyScale;   // flat slipper footprint

            BuildVisual();

            var rb = gameObject.AddComponent<Rigidbody>();
            rb.useGravity = true;
            rb.interpolation = RigidbodyInterpolation.Interpolate;
            rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            rb.linearVelocity = velocity;
            // A little tumble so it reads as a thrown object.
            rb.angularVelocity = new Vector3(SpinSpeed, 0f, SpinSpeed * 0.4f) * Mathf.Deg2Rad;

            // Ignore the thrower's colliders outright so launch never bounces off the player's body.
            IgnoreColliders(col, throwerColliders);
        }

        // The renderable is a flat cube parented under the root (collider-stripped) so the root's
        // BoxCollider — already sized to BodyScale in Init — stays the sole physics shape.
        private void BuildVisual()
        {
            var meshGo = GameObject.CreatePrimitive(PrimitiveType.Cube);
            meshGo.name = "SlipperBody";

            var childCol = meshGo.GetComponent<Collider>();
            if (childCol != null) Destroy(childCol); // only the root BoxCollider drives physics

            var r = meshGo.GetComponent<Renderer>();
            if (r != null) r.sharedMaterial = RuntimeMaterial.Make(SlipperColor);

            Transform t = meshGo.transform;
            t.SetParent(transform, false);
            t.localPosition = Vector3.zero;
            t.localScale = BodyScale;
        }

        private void IgnoreColliders(Collider self, Collider[] others)
        {
            if (self == null || others == null) return;
            for (int i = 0; i < others.Length; i++)
            {
                Collider o = others[i];
                if (o != null) Physics.IgnoreCollision(self, o, true);
            }
        }

        private void Update()
        {
            if (Time.time >= _deathTime) Destroy(gameObject);
        }

        private void OnCollisionEnter(Collision collision)
        {
            HandleContact(collision.collider);
        }

        private void OnTriggerEnter(Collider other)
        {
            HandleContact(other);
        }

        private void HandleContact(Collider other)
        {
            if (_consumed || other == null) return;
            if (Time.time < _armTime) return; // not armed yet — ignore early self/ground brushes

            // Knock any racer that isn't the thrower.
            IRacer racer = other.GetComponentInParent<IRacer>();
            if (racer != null && racer.Transform != null && !ReferenceEquals(racer.Transform, _thrower))
            {
                if (racer.IsAlive)
                {
                    Vector3 dir = racer.Transform.position - transform.position;
                    dir.y = 0f;
                    if (dir.sqrMagnitude < 0.0001f) dir = transform.forward;
                    dir.Normalize();

                    Vector3 knock = (dir + Vector3.up * KnockUpward).normalized * KnockForce;
                    racer.Knockback(knock);

                    AudioManager.Play(Sfx.Hit, 0.9f, 1.1f);
                    ImpactPuff.Spawn(transform.position, SlipperColor, 0.6f);
                }
                Consume();
                return;
            }

            // Hit solid ground/wall (not the thrower, who is collision-ignored) — land and despawn.
            int layer = other.gameObject.layer;
            if (((1 << layer) & _groundMask) != 0)
            {
                ImpactPuff.Spawn(transform.position, SlipperColor, 0.4f);
                Consume();
            }
            // Anything else (another pickup trigger, etc.) is ignored — keep flying.
        }

        private void Consume()
        {
            if (_consumed) return;
            _consumed = true;
            Destroy(gameObject);
        }
    }
}
