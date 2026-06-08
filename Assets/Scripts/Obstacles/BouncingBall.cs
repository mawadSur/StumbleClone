using System.Collections.Generic;
using StumbleClone.Core;
using StumbleClone.Visuals;
using UnityEngine;

namespace StumbleClone.Obstacles
{
    /// A live BOMB that bounces around the arena, counts its ground bounces, and POPS on the
    /// third one — spawning an explosion puff and radially knocking back every nearby racer.
    ///
    /// Class name kept as <c>BouncingBall</c> so existing spawner references still resolve; only
    /// the look and behaviour changed. All visuals (dark sphere body, fuse stalk, emissive spark,
    /// blinking warning) are built procedurally AT RUNTIME in <see cref="Configure"/> /
    /// <see cref="OnEnable"/>, so no scene re-bake is required. WebGL-safe: uses
    /// <see cref="RuntimeMaterial"/> (URP/Lit) and primitive meshes, never a runtime shader or
    /// ParticleSystem.
    ///
    /// A "bounce" is detected as a downward-to-upward vertical velocity reversal on ground contact
    /// (see <see cref="OnCollisionEnter"/>), debounced so a single landing can't double-count.
    [RequireComponent(typeof(Rigidbody), typeof(SphereCollider))]
    public sealed class BouncingBall : ArenaObstacle
    {
        // ---- Tuning (bomb behaviour) ----
        private const int BouncesToPop = 3;          // pops on the 3rd ground bounce
        private const float ExplosionRadius = 5f;    // metres — racers inside get knocked back
        private const float KnockbackForce = 16f;    // strong outward shove (racer adds its own up-bias)
        private const float ExplosionUpForce = 9f;   // extra explicit upward pop on the blast
        private const float BounceDebounce = 0.12f;  // s — ignore re-contacts within this window
        private const float DownwardThreshold = -0.4f; // must be falling this fast to count a bounce

        // ---- Tuning (fuse / warning tell) ----
        private const float WarnWindow = 1f;         // s before pop where the blink/pulse ramps up
        private const float BlinkBaseHz = 2f;        // warning blink rate at full fuse
        private const float BlinkPeakHz = 12f;       // warning blink rate right before popping
        private const float PulseAmplitude = 0.12f;  // ± scale fraction of the final-second pulse

        // ---- Visual palette ----
        private static readonly Color BombBody = new Color(0.07f, 0.07f, 0.08f, 1f);   // near-black casing
        private static readonly Color FuseStalk = new Color(0.45f, 0.32f, 0.18f, 1f);  // brown fuse cord
        private static readonly Color SparkHot = new Color(1f, 0.78f, 0.18f, 1f);      // emissive spark
        private static readonly Color WarnGlow = new Color(1f, 0.12f, 0.05f, 1f);      // red danger pulse

        private Rigidbody _rb;
        private float _speed;
        private float _nextKickTime;

        private int _bounceCount;
        private float _lastBounceTime = -10f;
        private bool _popped;

        // Runtime visual handles (children built in BuildBombVisual).
        private Material _bodyMat;     // the casing material (tinted red while warning)
        private Material _sparkMat;    // emissive spark material (flickers)
        private Transform _spark;      // tiny spark blob atop the fuse
        private Vector3 _baseScale = Vector3.one; // body scale snapshot for the pulse

        protected override void OnEnable()
        {
            base.OnEnable();
            if (_rb == null) _rb = GetComponent<Rigidbody>();
            _baseScale = transform.localScale;
            BuildBombVisual();
        }

        public override void Configure(Transform arenaCenter, float speedScale, float forceScale)
        {
            base.Configure(arenaCenter, speedScale, forceScale);
            if (_rb == null) _rb = GetComponent<Rigidbody>();

            Vector3 toCenter = arenaCenter != null
                ? (arenaCenter.position - transform.position)
                : transform.forward;
            toCenter.y = 0f;
            if (toCenter.sqrMagnitude < 0.0001f) toCenter = transform.forward;

            _speed = 8f * Mathf.Max(0.5f, speedScale);
            _rb.linearVelocity = toCenter.normalized * _speed + Vector3.up * 3f;
            _nextKickTime = Time.time + 1.5f;
        }

        protected override void Update()
        {
            base.Update();
            if (_rb == null || _popped) return;

            // Keep it lively: if it has slowed to a crawl, kick it back toward the center.
            if (Time.time >= _nextKickTime)
            {
                _nextKickTime = Time.time + 1.5f;
                Vector3 planar = new Vector3(_rb.linearVelocity.x, 0f, _rb.linearVelocity.z);
                if (planar.magnitude < _speed * 0.5f && _arenaCenter != null)
                {
                    Vector3 toCenter = _arenaCenter.position - transform.position;
                    toCenter.y = 0f;
                    _rb.AddForce(toCenter.normalized * _speed + Vector3.up * 4f, ForceMode.VelocityChange);
                }
            }

            UpdateFuseTell();
        }

        // A bounce = ground contact while the bomb was falling (downward → upward reversal).
        // Base ArenaObstacle's own private OnCollisionEnter still fires for racer pushes; Unity
        // invokes both message methods, so adding our own here is safe and does not shadow it.
        private void OnCollisionEnter(Collision collision)
        {
            if (_popped || _rb == null) return;
            if (Time.time - _lastBounceTime < BounceDebounce) return;

            // Only count a true landing: contact normal points up and we were moving downward.
            bool falling = collision.relativeVelocity.y > -DownwardThreshold // approaching speed
                           || _rb.linearVelocity.y < DownwardThreshold;
            bool fromBelow = collision.contactCount == 0 || collision.GetContact(0).normal.y > 0.3f;
            if (!falling || !fromBelow) return;

            _lastBounceTime = Time.time;
            _bounceCount++;

            if (_bounceCount >= BouncesToPop) Pop();
        }

        // ---- Explosion ----

        private void Pop()
        {
            if (_popped) return;
            _popped = true;

            Vector3 center = transform.position;

            // Visual burst: a smoke puff plus a confetti-style shrapnel spray (both WebGL-safe).
            ImpactPuff.Spawn(center, WarnGlow, 2.2f);
            ImpactPuff.Confetti(center, 1.4f);

            // Radial knockback to every living racer within the blast radius.
            ApplyRadialKnockback(center);

            CleanupVisual();
            Destroy(gameObject);
        }

        private void ApplyRadialKnockback(Vector3 center)
        {
            var seen = new HashSet<IRacer>();
            Collider[] hits = Physics.OverlapSphere(center, ExplosionRadius);
            for (int i = 0; i < hits.Length; i++)
            {
                var col = hits[i];
                if (col == null) continue;
                var racer = col.GetComponentInParent<IRacer>();
                if (racer == null || !racer.IsAlive || racer.IsFinished) continue;
                if (!seen.Add(racer)) continue; // one impulse per racer (multi-collider bodies)

                Vector3 away = racer.Transform.position - center;
                away.y = 0f;
                if (away.sqrMagnitude < 0.0001f) away = Random.insideUnitSphere; // dead-center fallback
                away.y = 0f;
                if (away.sqrMagnitude < 0.0001f) away = Vector3.forward;

                // Falloff: full force at the core, easing to ~40% at the rim — still a real shove.
                float dist = Vector3.Distance(racer.Transform.position, center);
                float falloff = Mathf.Lerp(1f, 0.4f, Mathf.Clamp01(dist / ExplosionRadius));

                Vector3 force = away.normalized * (KnockbackForce * falloff)
                                + Vector3.up * (ExplosionUpForce * falloff);
                racer.Knockback(force);
            }
        }

        // ---- Runtime visual (built as child objects) ----

        private void BuildBombVisual()
        {
            // Recolour the body sphere to a near-black casing.
            if (_bodyMat == null)
            {
                _bodyMat = RuntimeMaterial.Make(BombBody);
                var r = GetComponent<Renderer>();
                if (r != null) r.sharedMaterial = _bodyMat;
            }

            // Fuse stalk: a thin brown cylinder rising from the top of the casing.
            if (transform.Find("BombFuse") == null)
            {
                var fuse = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                fuse.name = "BombFuse";
                StripCollider(fuse);
                fuse.transform.SetParent(transform, false);
                fuse.transform.localScale = new Vector3(0.12f, 0.28f, 0.12f); // thin + short
                fuse.transform.localPosition = new Vector3(0f, 0.6f, 0f);     // sits on top
                fuse.transform.localRotation = Quaternion.Euler(12f, 0f, 8f); // slight lean
                SetMat(fuse, RuntimeMaterial.Make(FuseStalk));
            }

            // Spark: a tiny emissive blob at the fuse tip — the lit, hissing end.
            if (_spark == null && transform.Find("BombSpark") == null)
            {
                var spark = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                spark.name = "BombSpark";
                StripCollider(spark);
                spark.transform.SetParent(transform, false);
                spark.transform.localScale = Vector3.one * 0.16f;
                spark.transform.localPosition = new Vector3(0.06f, 0.86f, 0f); // at the leaned fuse tip
                _sparkMat = RuntimeMaterial.Make(SparkHot, emissive: true);
                SetMat(spark, _sparkMat);
                _spark = spark.transform;
            }
        }

        // Blink the casing toward red and pulse its scale in the final second before popping.
        // Frequency ramps from BlinkBaseHz up to BlinkPeakHz as the fuse burns down so the player
        // gets an accelerating "get clear!" tell. The spark always flickers for life.
        private void UpdateFuseTell()
        {
            float life = Time.time - _spawnTime;
            float remaining = Mathf.Max(0f, lifetime - life);

            // Always flicker the spark a little so the fuse reads as lit.
            if (_sparkMat != null && _sparkMat.HasProperty("_EmissionColor"))
            {
                float flick = 0.6f + 0.4f * Mathf.Abs(Mathf.Sin(Time.time * 22f));
                _sparkMat.SetColor("_EmissionColor", SparkHot * flick);
            }

            if (remaining > WarnWindow)
            {
                // Outside the warning window: keep body dark, no pulse.
                if (_bodyMat != null) SetBodyColor(BombBody);
                transform.localScale = _baseScale;
                return;
            }

            // Inside the final second: accelerating blink + scale pulse.
            float t = 1f - Mathf.Clamp01(remaining / WarnWindow); // 0 → 1 as we near the pop
            float hz = Mathf.Lerp(BlinkBaseHz, BlinkPeakHz, t);
            float blink = 0.5f + 0.5f * Mathf.Sin(Time.time * hz * Mathf.PI * 2f);

            if (_bodyMat != null) SetBodyColor(Color.Lerp(BombBody, WarnGlow, blink * (0.4f + 0.6f * t)));

            float pulse = 1f + PulseAmplitude * t * Mathf.Sin(Time.time * hz * Mathf.PI * 2f);
            transform.localScale = _baseScale * pulse;
        }

        // The arena despawns obstacles on lifetime/fall-off; if this bomb times out before its
        // third bounce, still detonate so it never just vanishes mid-air.
        protected override void Despawn()
        {
            if (!_popped)
            {
                Pop();
                return;
            }
            CleanupVisual();
            base.Despawn();
        }

        // ---- Small helpers ----

        private void SetBodyColor(Color c)
        {
            if (_bodyMat == null) return;
            if (_bodyMat.HasProperty("_BaseColor")) _bodyMat.SetColor("_BaseColor", c);
            if (_bodyMat.HasProperty("_Color")) _bodyMat.SetColor("_Color", c);
        }

        private static void SetMat(GameObject go, Material m)
        {
            var r = go.GetComponent<Renderer>();
            if (r != null) r.sharedMaterial = m;
        }

        private static void StripCollider(GameObject go)
        {
            var col = go.GetComponent<Collider>();
            if (col != null) Destroy(col);
        }

        private void CleanupVisual()
        {
            if (_bodyMat != null) { Destroy(_bodyMat); _bodyMat = null; }
            if (_sparkMat != null) { Destroy(_sparkMat); _sparkMat = null; }
        }
    }
}
