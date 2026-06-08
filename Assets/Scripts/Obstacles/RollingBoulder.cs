using StumbleClone.Core;
using StumbleClone.Visuals;
using UnityEngine;

namespace StumbleClone.Obstacles
{
    /// Heavy physics sphere launched across the arena. Rolls under gravity and
    /// shoves anything in its path. Physical contact moves the (dynamic) player on
    /// its own; the manual Knockback in the base also handles (kinematic) bots.
    ///
    /// At runtime (no scene re-bake needed) this restyles the plain grey ball into a
    /// chunky mottled-grey STONE by overlaying a few angular "rock facet" children at
    /// random rotations/scales, each given its own URP/Lit <see cref="RuntimeMaterial"/>
    /// in a varied mid-grey. It is also BREAKABLE: the first time it knocks a racer it
    /// flings that racer (strong outward+up), scatters a handful of small grey fragment
    /// cubes, fires an <see cref="ImpactPuff"/> burst, plays a crack, then destroys
    /// itself — a boulder is consumed when it hits someone.
    [RequireComponent(typeof(Rigidbody), typeof(SphereCollider))]
    public sealed class RollingBoulder : ArenaObstacle
    {
        // ---- Stone look (procedural, art-free) ----
        private const int MinFacets = 5;
        private const int MaxFacets = 8;            // inclusive; small for the draw-call budget
        private const float FacetScaleMin = 0.45f;  // facet size as a fraction of boulder diameter
        private const float FacetScaleMax = 0.72f;
        private const float GreyMin = 0.35f;        // mid-grey range so it reads as rock, not a ball
        private const float GreyMax = 0.55f;
        private const float StoneSmoothness = 0.06f; // dull, matte stone

        // ---- Shatter (breakable) ----
        private const int FragmentCount = 9;        // grey fragment cubes flung on shatter
        private const float FragmentScaleMin = 0.18f;
        private const float FragmentScaleMax = 0.40f;
        private const float FragmentScatterSpeed = 5.5f; // outward burst speed (m/s)
        private const float FragmentUpSpeed = 3.5f;      // upward pop on the fragments (m/s)
        private const float FragmentSpin = 6f;           // random angular velocity (rad/s)
        private const float FragmentLifetime = 1.6f;     // seconds before fragments self-destroy
        private const float KnockOutward = 1.55f;        // boulder's own hit is stronger than a graze
        private const float KnockUp = 0.9f;              // extra upward bias added to the shove

        private Rigidbody _rb;
        private bool _hasExplicitDir;
        private Vector3 _explicitDir;
        private bool _shattered;        // guard: shatter (and consume the boulder) exactly once
        private float _diameter = 1f;   // cached world diameter, drives facet/fragment sizing
        private AudioSource _audio;

        protected override void OnEnable()
        {
            base.OnEnable();
            _rb = GetComponent<Rigidbody>();
            BuildStoneLook();
        }

        /// Pins the boulder's launch direction to the wave's intended heading, replacing the
        /// random lateral jitter so the roll matches the telegraphed rim octant. `direction`
        /// is the world-space heading (inward across the arena); its Y is ignored. Call before
        /// or after Configure — Configure re-reads this flag.
        public void SetLaunchDirection(Vector3 direction)
        {
            direction.y = 0f;
            if (direction.sqrMagnitude < 0.0001f) return;
            _explicitDir = direction.normalized;
            _hasExplicitDir = true;

            // If Configure already ran, retroactively correct the velocity magnitude in place.
            if (_rb != null && _rb.linearVelocity.sqrMagnitude > 0.0001f)
                _rb.linearVelocity = _explicitDir * _rb.linearVelocity.magnitude;
        }

        public override void Configure(Transform arenaCenter, float speedScale, float forceScale)
        {
            base.Configure(arenaCenter, speedScale, forceScale);

            if (_rb == null) _rb = GetComponent<Rigidbody>();

            // Aim across the platform toward the far side, through the center.
            Vector3 toCenter = arenaCenter != null
                ? (arenaCenter.position - transform.position)
                : transform.forward;
            toCenter.y = 0f;
            if (toCenter.sqrMagnitude < 0.0001f) toCenter = transform.forward;
            toCenter.Normalize();

            Vector3 launch;
            if (_hasExplicitDir)
            {
                // Honour the wave's direction exactly so the roll matches its telegraph.
                launch = _explicitDir;
            }
            else
            {
                // No wave direction supplied: add a little lateral jitter so boulders don't
                // all converge on the exact middle.
                Vector3 lateral = Vector3.Cross(Vector3.up, toCenter);
                launch = (toCenter + lateral * Random.Range(-0.35f, 0.35f)).normalized;
            }

            float speed = 9f * Mathf.Max(0.5f, speedScale);
            _rb.linearVelocity = launch * speed;
        }

        // ------------------------------------------------------------------
        // STONE LOOK — overlay angular grey rock facets and matte the base.
        // ------------------------------------------------------------------

        /// Restyle this instance into a chunky mottled-grey stone at runtime. Recolours the
        /// base sphere to a matte mid-grey and parents a handful of small angular cube/tetra
        /// facets at random rotations/scales over it so the silhouette reads as rock, not a
        /// smooth ball. Pure decoration: facet colliders are stripped, the boulder still rolls
        /// as the single SphereCollider it always was.
        private void BuildStoneLook()
        {
            _diameter = Mathf.Max(0.1f, ApproxDiameter());

            // 1) Matte, mid-grey the base sphere so it stops reading as a smooth ball.
            var baseRenderer = GetComponent<Renderer>();
            if (baseRenderer != null)
            {
                var baseMat = RuntimeMaterial.Make(RandomGrey());
                Dull(baseMat);
                baseRenderer.sharedMaterial = baseMat;
            }

            // 2) Overlay angular facets. Already styled? (e.g. pooled re-enable) skip.
            if (transform.Find("RockFacet0") != null) return;

            int facets = Random.Range(MinFacets, MaxFacets + 1);
            for (int i = 0; i < facets; i++)
            {
                // Alternate cubes and (squashed) tetra-ish cubes for an irregular, faceted read.
                var facet = GameObject.CreatePrimitive(PrimitiveType.Cube);
                facet.name = "RockFacet" + i;

                // Pure decoration — never collide; the boulder keeps its single sphere collider.
                var col = facet.GetComponent<Collider>();
                if (col != null) Destroy(col);

                var ft = facet.transform;
                ft.SetParent(transform, false);

                // Sit each facet just under the sphere surface and rotate it randomly so the
                // corners jut out at angles like broken rock.
                Vector3 onSphere = Random.onUnitSphere;
                ft.localPosition = onSphere * 0.32f; // local units (sphere primitive r = 0.5)
                ft.localRotation = Random.rotationUniform;

                // Irregular non-uniform scale so no two facets look alike (chunky, stony).
                float s = Random.Range(FacetScaleMin, FacetScaleMax);
                ft.localScale = new Vector3(
                    s * Random.Range(0.7f, 1.15f),
                    s * Random.Range(0.55f, 1.0f),
                    s * Random.Range(0.7f, 1.15f));

                var r = facet.GetComponent<Renderer>();
                if (r != null)
                {
                    var m = RuntimeMaterial.Make(RandomGrey());
                    Dull(m);
                    r.sharedMaterial = m;
                }
            }
        }

        // ------------------------------------------------------------------
        // BREAKABLE — shatter on the first racer hit, consuming the boulder.
        // ------------------------------------------------------------------

        // ArenaObstacle declares its own PRIVATE contact callbacks; private members are not
        // inherited, so these are independent declarations (no hiding, no `new` needed). The base
        // may still graze-push a racer via its copy, but our shatter sets _shattered and Despawns
        // immediately, so any base nudge is a harmless no-op afterward — the boulder is consumed
        // the moment it knocks someone.
        private void OnCollisionEnter(Collision collision)
        {
            TryShatterOnRacer(collision.collider);
        }

        private void OnTriggerEnter(Collider other)
        {
            TryShatterOnRacer(other);
        }

        private void OnTriggerStay(Collider other)
        {
            TryShatterOnRacer(other);
        }

        /// If <paramref name="other"/> belongs to a live racer, give it a strong outward+up
        /// knock and shatter this boulder. Guarded so it fires exactly once. (The base
        /// <see cref="ArenaObstacle"/> also nudges contacts via its own callbacks; here we
        /// apply the stronger consuming hit ourselves, then destroy before any double-up.)
        private void TryShatterOnRacer(Collider other)
        {
            if (_shattered || other == null) return;

            var racer = other.GetComponentInParent<IRacer>();
            if (racer == null || !racer.IsAlive || racer.IsFinished) return;

            _shattered = true;

            // Strong outward shove (radially away from the boulder) plus an upward bias so the
            // racer is launched, not just nudged. Knockback() adds its own up-bias on top.
            Vector3 outward = racer.Transform.position - transform.position;
            outward.y = 0f;
            if (outward.sqrMagnitude < 0.0001f) outward = transform.forward;
            outward.Normalize();
            Vector3 force = (outward * KnockOutward + Vector3.up * KnockUp) * pushForce;
            racer.Knockback(force);

            Shatter();
        }

        /// Break the boulder apart: scatter grey fragment cubes with brief Rigidbody velocities,
        /// fire an ImpactPuff dust burst, play a crack, then destroy the boulder GameObject.
        private void Shatter()
        {
            Vector3 center = transform.position;

            SpawnFragments(center);

            // Greyish dust burst at the break point (WebGL-safe, self-cleaning).
            ImpactPuff.Spawn(center, new Color(0.5f, 0.5f, 0.52f, 1f), Mathf.Clamp(_diameter, 0.6f, 2f));

            PlayCrack();

            Despawn();
        }

        /// Fling a handful of small grey cubes outward+up, each a free physics body that
        /// scatters and self-destroys shortly after.
        private void SpawnFragments(Vector3 center)
        {
            for (int i = 0; i < FragmentCount; i++)
            {
                var frag = GameObject.CreatePrimitive(PrimitiveType.Cube);
                frag.name = "BoulderFragment";

                float fs = _diameter * Random.Range(FragmentScaleMin, FragmentScaleMax) * 0.5f;
                fs = Mathf.Max(0.05f, fs);
                frag.transform.position = center + Random.insideUnitSphere * (_diameter * 0.25f);
                frag.transform.rotation = Random.rotationUniform;
                frag.transform.localScale = new Vector3(
                    fs * Random.Range(0.7f, 1.2f),
                    fs * Random.Range(0.7f, 1.2f),
                    fs * Random.Range(0.7f, 1.2f));

                var r = frag.GetComponent<Renderer>();
                if (r != null)
                {
                    var m = RuntimeMaterial.Make(RandomGrey());
                    Dull(m);
                    r.sharedMaterial = m;
                }

                // Tighten the auto box collider's physics so fragments don't shove racers.
                var col = frag.GetComponent<Collider>();
                if (col != null) col.isTrigger = false;

                var rb = frag.AddComponent<Rigidbody>();
                rb.mass = 0.2f;
                Vector3 outward = Random.onUnitSphere;
                outward.y = Mathf.Abs(outward.y); // bias the spray upward
                rb.linearVelocity = outward * FragmentScatterSpeed + Vector3.up * FragmentUpSpeed;
                rb.angularVelocity = Random.insideUnitSphere * FragmentSpin;

                Destroy(frag, FragmentLifetime);
            }
        }

        // ------------------------------------------------------------------
        // Helpers
        // ------------------------------------------------------------------

        /// Play a short crack via a transient AudioSource if one (or a clip) is available.
        /// Fully null-guarded — silent if no clip is wired, never throws.
        private void PlayCrack()
        {
            try
            {
                if (_audio == null) _audio = GetComponent<AudioSource>();
                if (_audio != null && _audio.clip != null)
                {
                    // Detach a one-shot so the sound survives this object's destruction.
                    AudioSource.PlayClipAtPoint(_audio.clip, transform.position, _audio.volume);
                }
            }
            catch
            {
                // No audio wired — the shatter is still fully functional without it.
            }
        }

        /// Approximate the boulder's world diameter from its sphere collider / bounds so
        /// facets and fragments scale with whatever size the scene placed it at.
        private float ApproxDiameter()
        {
            var sc = GetComponent<SphereCollider>();
            if (sc != null)
            {
                Vector3 ls = transform.lossyScale;
                float maxAxis = Mathf.Max(Mathf.Abs(ls.x), Mathf.Abs(ls.y), Mathf.Abs(ls.z));
                return sc.radius * 2f * Mathf.Max(0.01f, maxAxis);
            }
            var r = GetComponent<Renderer>();
            if (r != null) return r.bounds.size.magnitude * 0.5f;
            return Mathf.Max(0.1f, transform.lossyScale.x);
        }

        private static Color RandomGrey()
        {
            float g = Random.Range(GreyMin, GreyMax);
            // Tiny per-channel jitter so the rock reads as mottled rather than flat grey.
            return new Color(
                Mathf.Clamp01(g + Random.Range(-0.04f, 0.04f)),
                Mathf.Clamp01(g + Random.Range(-0.04f, 0.04f)),
                Mathf.Clamp01(g + Random.Range(-0.03f, 0.05f)),
                1f);
        }

        /// Make a URP/Lit material read as dull matte stone (no metal, near-zero smoothness).
        private static void Dull(Material m)
        {
            if (m == null) return;
            if (m.HasProperty("_Metallic")) m.SetFloat("_Metallic", 0f);
            if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", StoneSmoothness);
            if (m.HasProperty("_Glossiness")) m.SetFloat("_Glossiness", StoneSmoothness);
        }
    }
}
