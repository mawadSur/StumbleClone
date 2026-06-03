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
        private const float SpinSpeed = 150f;        // deg/s yaw spin (lively)
        private const float BobAmplitude = 0.25f;    // vertical bob height
        private const float BobFrequency = 1.5f;     // bob cycles/s
        private const float HoverHeight = 1.0f;      // rest height above the ground point it spawned on
        private const float PopDuration = 0.18f;     // scale-up-and-fade pop on collect

        // ---- Idle-animation tuning (emissive pulse + scale "breathing") ----
        private const float PulseFrequency = 2.2f;   // emission-glow cycles/s
        private const float PulseMin = 0.55f;        // emission intensity floor (× base colour)
        private const float PulseMax = 2.4f;         // emission intensity ceiling
        private const float BreatheFrequency = 1.1f; // scale-breathe cycles/s
        private const float BreatheAmount = 0.08f;   // ± fraction of base scale

        // ---- Buff strengths (local — gameplay feel for this mode) ----
        private const float SpeedMultiplier = 1.6f;
        private const float SpeedSeconds = 5f;
        private const float JumpMultiplier = 1.5f;
        private const float JumpSeconds = 5f;

        private PowerupType _type;
        private Transform _visual;     // spinning root the body + detail primitives parent under
        private Material _glowMat;     // the body material we pulse the emission on (cached once)
        private Color _baseColor;      // per-type base colour, source for the emission pulse
        private Transform _orbit;      // optional orbiting element (Shield ring) — null for others
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

        // The collider lives on this root object; the rendered visual is a child so we can spin
        // it without rotating the trigger (harmless, but keeps the collider axis-stable). Each
        // type gets a unique silhouette built from primitives parented under a single spinning
        // root (_visual). The root itself is an empty Transform; the body primitive (whose
        // material we pulse) and any detail/orbit primitives hang off it.
        private void BuildVisual(Color color)
        {
            _baseColor = color;

            // One shared emissive material instance — all this pickup's primitives use it, so the
            // emission pulse and the collect-fade (PopThenDestroy) touch every piece at once.
            _glowMat = RuntimeMaterial.Make(color, emissive: true);

            var rootGo = new GameObject("PowerupVisual");
            _visual = rootGo.transform;
            _visual.SetParent(transform, false);
            _visual.localPosition = Vector3.zero;

            switch (_type)
            {
                case PowerupType.Speed: BuildSpeed(); break;
                case PowerupType.Shield: BuildShield(); break;
                case PowerupType.SuperJump: BuildSuperJump(); break;
                default: BuildShield(); break;
            }
        }

        // Speed (yellow): an elongated, forward-leaning core capsule flanked by a stepped trail of
        // shrinking cubes — reads as a streaking dart. Body = the leaning capsule.
        private void BuildSpeed()
        {
            float d = Radius * 2f;
            Transform body = AddPrimitive(PrimitiveType.Capsule, _visual, isBody: true);
            body.localScale = new Vector3(d * 0.5f, d * 0.85f, d * 0.5f);
            body.localRotation = Quaternion.Euler(90f, 0f, 0f);   // lay it forward (along +Z)
            _visual.localRotation = Quaternion.Euler(-18f, 0f, 0f); // lean the whole dart forward

            // Three trailing cubes behind the core, shrinking — a sense of motion-blur exhaust.
            for (int i = 0; i < 3; i++)
            {
                Transform t = AddPrimitive(PrimitiveType.Cube, _visual, isBody: false);
                float s = d * (0.34f - i * 0.08f);
                t.localScale = new Vector3(s, s, s);
                t.localPosition = new Vector3(0f, 0f, -d * (0.4f + i * 0.32f));
            }
        }

        // Shield (cyan): a faceted core orb hugged by an orbiting ring of small cubes that circles
        // it (animated in Update via _orbit). Body = the orb.
        private void BuildShield()
        {
            float d = Radius * 2f;
            Transform body = AddPrimitive(PrimitiveType.Sphere, _visual, isBody: true);
            body.localScale = Vector3.one * (d * 0.78f);

            // Orbit pivot the ring segments parent under; Update spins this for a guarding ring.
            var orbitGo = new GameObject("Orbit");
            _orbit = orbitGo.transform;
            _orbit.SetParent(_visual, false);
            _orbit.localRotation = Quaternion.Euler(70f, 0f, 0f); // tilt the ring plane

            for (int i = 0; i < 4; i++)
            {
                Transform t = AddPrimitive(PrimitiveType.Cube, _orbit, isBody: false);
                float ang = i * (Mathf.PI * 0.5f);
                float r = d * 0.62f;
                t.localPosition = new Vector3(Mathf.Cos(ang) * r, 0f, Mathf.Sin(ang) * r);
                t.localScale = Vector3.one * (d * 0.2f);
            }
        }

        // SuperJump (magenta): an upward-pointing chevron arrow — a vertical shaft capped by a
        // cone-like stack of two shrinking, rotated cubes. Body = the shaft.
        private void BuildSuperJump()
        {
            float d = Radius * 2f;
            Transform shaft = AddPrimitive(PrimitiveType.Cube, _visual, isBody: true);
            shaft.localScale = new Vector3(d * 0.3f, d * 0.85f, d * 0.3f);
            shaft.localPosition = new Vector3(0f, -d * 0.18f, 0f);

            // Two stacked cubes, rotated 45° and shrinking upward, fake an arrowhead point.
            for (int i = 0; i < 2; i++)
            {
                Transform t = AddPrimitive(PrimitiveType.Cube, _visual, isBody: false);
                float s = d * (0.6f - i * 0.24f);
                t.localScale = new Vector3(s, s * 0.4f, s);
                t.localPosition = new Vector3(0f, d * (0.42f + i * 0.22f), 0f);
                t.localRotation = Quaternion.Euler(0f, 45f, 0f);
            }
        }

        // Spawn one primitive under <paramref name="parent"/>, strip its doubled-up collider, and
        // give it the shared glow material. The primitive flagged isBody is just named "Body" for
        // clarity; the emission pulse and collect-fade drive the shared _glowMat, not one renderer.
        private Transform AddPrimitive(PrimitiveType prim, Transform parent, bool isBody)
        {
            var go = GameObject.CreatePrimitive(prim);
            var childCol = go.GetComponent<Collider>();
            if (childCol != null) Destroy(childCol); // only the root trigger should remain

            var r = go.GetComponent<Renderer>();
            if (r != null) r.sharedMaterial = _glowMat;

            Transform t = go.transform;
            t.SetParent(parent, false);

            if (isBody) go.name = "Body";
            return t;
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

            float t = Time.time;

            // Lively yaw spin of the whole silhouette + a counter-spinning guard ring (Shield).
            // All allocation-free: we reuse cached transforms/material set up in BuildVisual.
            if (_visual != null)
            {
                _visual.Rotate(0f, SpinSpeed * Time.deltaTime, 0f, Space.Self);

                // Scale "breathing" — a gentle uniform pulse of the whole pickup.
                float breathe = 1f + Mathf.Sin(t * BreatheFrequency * Mathf.PI * 2f + _bobPhase) * BreatheAmount;
                _visual.localScale = new Vector3(breathe, breathe, breathe);
            }

            // Orbiting guard ring spins faster, on its tilted plane (Shield only; null otherwise).
            if (_orbit != null) _orbit.Rotate(0f, SpinSpeed * 2.2f * Time.deltaTime, 0f, Space.Self);

            // Pulsing emissive glow — animate the cached material's emission intensity over time.
            if (_glowMat != null && _glowMat.HasProperty("_EmissionColor"))
            {
                float k = (Mathf.Sin(t * PulseFrequency * Mathf.PI * 2f + _bobPhase) + 1f) * 0.5f;
                float intensity = Mathf.Lerp(PulseMin, PulseMax, k);
                _glowMat.SetColor("_EmissionColor", _baseColor * intensity);
            }

            // Gentle vertical bob (root object — moves the trigger with the visual).
            float y = _baseY + Mathf.Sin(t * BobFrequency * Mathf.PI * 2f + _bobPhase) * BobAmplitude;
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
            AudioManager.Play(Sfx.Pickup); // little pickup pop — distinct from the match-win chime
            var col = GetComponent<SphereCollider>();
            if (col != null) col.enabled = false;
            StartCoroutine(PopThenDestroy());
        }

        private IEnumerator PopThenDestroy()
        {
            // The whole silhouette parents under _visual, so scaling the root pops every primitive
            // at once; the shared _glowMat (cached in BuildVisual) fades them all together.
            Material mat = _glowMat;

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
