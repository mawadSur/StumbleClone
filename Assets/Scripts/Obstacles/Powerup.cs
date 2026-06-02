using System.Collections;
using StumbleClone.Audio;
using StumbleClone.Bots;
using StumbleClone.Player;
using UnityEngine;

namespace StumbleClone.Obstacles
{
    /// The three collectible buff types granted by a <see cref="Powerup"/> pickup.
    public enum PowerupType
    {
        /// Temporary run-speed boost.
        Speed,
        /// One-use guard that ignores the next knockback/push.
        Shield,
        /// Temporary higher jump.
        SuperJump
    }

    /// A floating collectible buff for the Knockout arena. Built entirely at runtime
    /// (no prefab wiring) by <see cref="PowerupSpawner"/>: a small bright, glowing, slowly
    /// rotating + bobbing sphere wrapped in a trigger <see cref="SphereCollider"/>. When a
    /// racer walks into it, the matching buff is granted to that racer and the pickup destroys
    /// itself with a tiny pop.
    ///
    /// IRacer resolution note: an interface can't be reached with GetComponentInParent through
    /// a child collider's GetComponent on the interface type reliably, and Unity's
    /// GetComponentInParent does in fact walk interfaces — but to stay explicit and match the
    /// project's concrete controllers, we resolve the two concrete components
    /// (<see cref="PlayerController"/> / <see cref="BotController"/>) via GetComponentInParent
    /// on the collider's transform. Trigger colliders can live on a child of the racer root, so
    /// we always search upward from the collider that entered.
    [DisallowMultipleComponent]
    [RequireComponent(typeof(SphereCollider))]
    public sealed class Powerup : MonoBehaviour
    {
        // ---- Visual / motion tuning (local — pickup-specific feel) ----
        private const float Radius = 0.45f;          // visual + trigger size
        private const float SpinSpeed = 90f;         // deg/s yaw spin
        private const float BobAmplitude = 0.25f;    // vertical bob height
        private const float BobFrequency = 1.5f;     // bob cycles/s
        private const float HoverHeight = 1.0f;      // rest height above the ground point it spawned on
        private const float PopDuration = 0.18f;     // scale-up-and-fade pop on collect

        // ---- Buff strengths (local — gameplay feel for this mode) ----
        private const float SpeedMultiplier = 1.6f;
        private const float SpeedSeconds = 5f;
        private const float JumpMultiplier = 1.5f;
        private const float JumpSeconds = 5f;

        private PowerupType _type;
        private Transform _visual;
        private float _bobPhase;       // randomized so a cluster doesn't bob in lockstep
        private float _baseY;          // hover centre height
        private bool _collected;       // guards against a second trigger during the pop

        /// The buff this pickup grants. Set by the spawner via <see cref="Configure"/>.
        public PowerupType Type => _type;

        /// Build a pickup at <paramref name="groundPoint"/> with the given type. Call right after
        /// AddComponent. Creates the glowing visual, sizes the trigger collider, and colours it
        /// per type (Speed = yellow, Shield = cyan, SuperJump = magenta).
        /// <param name="type">Which buff this pickup grants.</param>
        /// <param name="groundPoint">World point on the ground; the pickup hovers above it.</param>
        public void Configure(PowerupType type, Vector3 groundPoint)
        {
            _type = type;

            _baseY = groundPoint.y + HoverHeight;
            transform.position = new Vector3(groundPoint.x, _baseY, groundPoint.z);
            _bobPhase = Random.value * Mathf.PI * 2f;

            var col = GetComponent<SphereCollider>();
            col.isTrigger = true;
            col.radius = Radius;

            BuildVisual(ColorFor(type));
        }

        // The collider lives on this root object; the rendered sphere is a child so we can spin
        // it without rotating the trigger (harmless, but keeps the collider axis-stable).
        private void BuildVisual(Color color)
        {
            var sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            sphere.name = "PowerupVisual";
            _visual = sphere.transform;
            _visual.SetParent(transform, false);
            _visual.localPosition = Vector3.zero;
            _visual.localScale = Vector3.one * (Radius * 2f);

            // The child primitive's own collider would double up with the trigger — drop it.
            var childCol = sphere.GetComponent<Collider>();
            if (childCol != null) Destroy(childCol);

            // Bright, glowing URP material (emissive) so it pops in the arena.
            RuntimeMaterial.Apply(sphere, color, emissive: true);
        }

        private static Color ColorFor(PowerupType type)
        {
            switch (type)
            {
                case PowerupType.Speed: return new Color(1f, 0.92f, 0.15f);     // yellow
                case PowerupType.Shield: return new Color(0.15f, 0.85f, 1f);    // cyan
                case PowerupType.SuperJump: return new Color(1f, 0.2f, 0.9f);   // magenta
                default: return Color.white;
            }
        }

        private void Update()
        {
            if (_collected) return;

            // Slow spin (visual child only) + gentle vertical bob. No allocations.
            if (_visual != null) _visual.Rotate(0f, SpinSpeed * Time.deltaTime, 0f, Space.Self);

            float y = _baseY + Mathf.Sin(Time.time * BobFrequency * Mathf.PI * 2f + _bobPhase) * BobAmplitude;
            Vector3 p = transform.position;
            p.y = y;
            transform.position = p;
        }

        private void OnTriggerEnter(Collider other)
        {
            if (_collected) return;

            // Resolve the racer that touched us from the concrete controllers. The trigger that
            // fires belongs to the racer's body collider, which may sit on a child of the root —
            // so search upward from the collider's transform.
            var player = other.GetComponentInParent<PlayerController>();
            if (player != null)
            {
                if (!player.IsAlive) return;
                GrantTo(player);
                Collect();
                return;
            }

            var bot = other.GetComponentInParent<BotController>();
            if (bot != null)
            {
                if (!bot.IsAlive) return;
                GrantTo(bot);
                Collect();
            }
            // Anything else (ground, obstacle, hazard) is ignored — pickup stays live.
        }

        private void GrantTo(PlayerController player)
        {
            switch (_type)
            {
                case PowerupType.Speed: player.ApplySpeedBoost(SpeedMultiplier, SpeedSeconds); break;
                case PowerupType.Shield: player.GrantShield(); break;
                case PowerupType.SuperJump: player.GrantJumpBoost(JumpMultiplier, JumpSeconds); break;
            }
        }

        private void GrantTo(BotController bot)
        {
            switch (_type)
            {
                case PowerupType.Speed: bot.ApplySpeedBoost(SpeedMultiplier, SpeedSeconds); break;
                case PowerupType.Shield: bot.GrantShield(); break;
                case PowerupType.SuperJump: bot.GrantJumpBoost(JumpMultiplier, JumpSeconds); break;
            }
        }

        // Disable the trigger immediately, play a tiny scale-up + fade pop, then destroy. Guarded
        // by _collected so a second overlapping racer in the same frame can't double-grant.
        private void Collect()
        {
            _collected = true;
            AudioManager.Play(Sfx.Win); // little pickup pop
            var col = GetComponent<SphereCollider>();
            if (col != null) col.enabled = false;
            StartCoroutine(PopThenDestroy());
        }

        private IEnumerator PopThenDestroy()
        {
            Renderer rend = _visual != null ? _visual.GetComponent<Renderer>() : null;
            Material mat = rend != null ? rend.material : null;

            Vector3 startScale = _visual != null ? _visual.localScale : Vector3.one;
            Vector3 endScale = startScale * 1.6f;

            float t = 0f;
            while (t < PopDuration)
            {
                t += Time.deltaTime;
                float k = Mathf.Clamp01(t / PopDuration);
                if (_visual != null) _visual.localScale = Vector3.Lerp(startScale, endScale, k);
                if (mat != null && mat.HasProperty("_BaseColor"))
                {
                    Color c = mat.GetColor("_BaseColor");
                    c.a = 1f - k;
                    mat.SetColor("_BaseColor", c);
                }
                yield return null;
            }

            Destroy(gameObject);
        }
    }
}
