using System.Collections;
using System.Collections.Generic;
using StumbleClone.Core;
using UnityEngine;

namespace StumbleClone.Obstacles
{
    public enum ObstacleType
    {
        RollingBoulder,
        SlidingRam,
        SweepingBar,
        BouncingBall,
        StepBlocks,
    }

    /// Drives the Knockout arena. Instead of firing hazards from a random rim angle, it runs
    /// TELEGRAPHED WAVES: each wave is a named, recognizable pattern (Cross Sweep, Pincer,
    /// Clockwise Rotation, Spiral, Rain, Gauntlet) whose hazards come from discrete rim
    /// directions. A ground marker telegraphs each spawn so the player can read and learn it.
    /// Difficulty rises over time + eliminations (intensity → tier), and a SEEDED RNG keeps the
    /// early sequence identical every round so patterns are learnable. Built from primitives so
    /// the mode is playable before any art. Created at runtime by LastStandingManager via Begin().
    public sealed class ObstacleSpawner : MonoBehaviour
    {
        [SerializeField] private float spawnRadius = 18f;
        [SerializeField] private float rampDuration = 75f;
        [Tooltip("Diameter of the ground telegraph disc shown before each hazard spawns.")]
        [SerializeField] private float telegraphSize = 4f;

        [Header("Escalation")]
        [Tooltip("Fraction of a pattern's telegraph lead kept at the top tier. Keeps late waves " +
                 "learnable instead of unreadable — never drops below this.")]
        [SerializeField, Range(0.4f, 1f)] private float minTelegraphLeadFactor = 0.65f;
        [Tooltip("Rest gap between waves at the calmest moment (early game).")]
        [SerializeField] private float maxRestGap = 2.6f;
        [Tooltip("Rest gap between waves at peak intensity (late game).")]
        [SerializeField] private float minRestGap = 0.6f;
        [Tooltip("Tier at/above which waves may overlap: the next wave starts telegraphing " +
                 "before the current one fully clears, so late-game pressure combos.")]
        [SerializeField] private int comboTier = 2;

        // Fixed so the opening waves are the SAME every round → the player can learn them.
        private const int PatternSeed = 9173;

        private Transform _center;
        private bool _spawning;
        private float _startTime;
        private int _initialAlive;
        private System.Random _rng;
        private PhysicsMaterial _bouncy;

        /// Called by the mode manager once the level has started.
        public void Begin(Transform arenaCenter, float radius)
        {
            _center = arenaCenter;
            if (radius > 0.1f) spawnRadius = radius;
            _startTime = Time.time;
            _initialAlive = Mathf.Max(1, RacerRegistry.AliveCount);
            _rng = new System.Random(PatternSeed);
            _spawning = true;
            StopAllCoroutines();
            StartCoroutine(WaveLoop());
        }

        public void StopSpawning()
        {
            _spawning = false;
            StopAllCoroutines();
        }

        private IEnumerator WaveLoop()
        {
            yield return new WaitForSeconds(2f); // grace before the first wave

            var entries = new List<SpawnEntry>(16);
            while (_spawning)
            {
                if (RacerRegistry.AliveCount <= 1) { _spawning = false; yield break; }

                float intensity = ComputeIntensity();
                int tier = Mathf.Clamp(Mathf.FloorToInt(intensity * 4f), 0, 3);

                SpawnPattern pattern = PatternLibrary.Select(tier, _rng);
                entries.Clear();
                pattern.Build(entries, tier);
                entries.Sort(CompareDelay);

                // Escalation: at higher tiers the telegraph gives a little less lead time, but
                // never below minTelegraphLeadFactor so the wave stays readable and learnable.
                float lead = pattern.TelegraphLead * LeadFactor(intensity);
                // Spawn from the LIVE platform rim: when ArenaShrinker is closing the floor, read
                // its current edge so hazards arrive from the real, shrinking lip — not the stale
                // serialized radius (which would drop them mid-platform as the floor pulls in).
                Vector3 centerPos = CurrentCenter();
                float rimRadius = CurrentRimRadius();
                float groundY = centerPos.y + 0.45f;

                // Announce the wave so audio/UI can play a directional "tell". The first sorted
                // entry is the leading hazard the player must read first.
                if (entries.Count > 0)
                    GameEvents.RaiseWaveTelegraphed(pattern.Name, entries[0].dir);

                // Telegraph the whole wave up-front; each marker lives until its hazard lands.
                for (int i = 0; i < entries.Count; i++)
                {
                    Vector3 rim = ArenaDirections.RimPoint(centerPos, rimRadius, entries[i].dir);
                    TelegraphIndicator.Spawn(rim, groundY, lead + entries[i].delay, telegraphSize);
                }

                float speedScale = Mathf.Lerp(1f, 2f, intensity);
                float forceScale = Mathf.Lerp(1f, 1.7f, intensity);

                yield return new WaitForSeconds(lead);

                float spawnedAt = 0f;
                for (int i = 0; i < entries.Count && _spawning; i++)
                {
                    float wait = entries[i].delay - spawnedAt;
                    if (wait > 0f) yield return new WaitForSeconds(wait);
                    spawnedAt = entries[i].delay;

                    // Reuse the wave's rim snapshot (same centerPos/rimRadius the telegraph used)
                    // so each hazard lands where its marker promised, even though the platform has
                    // shrunk a little during the telegraph lead.
                    Vector3 rim = ArenaDirections.RimPoint(centerPos, rimRadius, entries[i].dir);
                    SpawnTypeAt(entries[i].type, rim, speedScale, forceScale, entries[i].dir, centerPos);
                }

                // Breather between waves — shorter as the round heats up. Past the combo tier the
                // rest is short enough that the next wave begins telegraphing while the current
                // wave's hazards are still crossing the arena, so late-game pressure combos.
                yield return new WaitForSeconds(RestGap(intensity, tier));
            }
        }

        private static int CompareDelay(SpawnEntry a, SpawnEntry b) => a.delay.CompareTo(b.delay);

        /// Margin (metres) by which the spawn rim sits OUTSIDE the live platform edge, so hazards
        /// enter from just beyond the lip and roll inward across the floor rather than appearing
        /// on top of racers at the rim.
        private const float RimOutsideMargin = 1.5f;

        /// Centre to spawn around. Tracks ArenaShrinker's live platform centre when the shrinker
        /// is active; otherwise the serialized arenaCenter (or origin).
        private Vector3 CurrentCenter()
        {
            if (ArenaShrinker.Active) return ArenaShrinker.Center;
            return _center != null ? _center.position : Vector3.zero;
        }

        /// Radius to spawn hazards at. When the platform is shrinking, sit just OUTSIDE its live
        /// edge so hazards arrive from the real, closing rim. Otherwise use the static spawnRadius
        /// the manager configured. Never smaller than a tiny floor so a fully-closed disc still
        /// spawns sane hazards.
        private float CurrentRimRadius()
        {
            if (ArenaShrinker.Active)
            {
                float edge = ArenaShrinker.CurrentSafeRadius;
                if (edge > 0.1f) return Mathf.Max(2f, edge + RimOutsideMargin);
            }
            return spawnRadius;
        }

        private float ComputeIntensity()
        {
            float timeT = Mathf.Clamp01((Time.time - _startTime) / rampDuration);
            int deaths = _initialAlive - RacerRegistry.AliveCount;
            float deathT = _initialAlive > 1 ? deaths / (float)(_initialAlive - 1) : 0f;
            return Mathf.Clamp01(0.5f * timeT + 0.5f * deathT);
        }

        /// Telegraph lead shrinks as intensity climbs but is floored at minTelegraphLeadFactor so
        /// even the hardest waves can still be recognized and learned (the ROADMAP "learn" goal).
        private float LeadFactor(float intensity) => Mathf.Lerp(1f, minTelegraphLeadFactor, intensity);

        /// Rest gap between waves. Shrinks with intensity; at/above comboTier it is clamped tighter
        /// so the next wave overlaps the tail of the current one for combined late-game pressure.
        private float RestGap(float intensity, int tier)
        {
            float gap = Mathf.Lerp(maxRestGap, minRestGap, intensity);
            if (tier >= comboTier)
                gap = Mathf.Min(gap, Mathf.Lerp(minRestGap, 0f, intensity)); // allow overlap late
            return Mathf.Max(0f, gap);
        }

        private void SpawnTypeAt(ObstacleType type, Vector3 rim, float speedScale, float forceScale,
                                 SpawnDirection dir, Vector3 centerPos)
        {
            switch (type)
            {
                case ObstacleType.RollingBoulder: SpawnBoulder(rim, speedScale, forceScale, centerPos); break;
                case ObstacleType.SlidingRam: SpawnRam(rim, speedScale, forceScale); break;
                case ObstacleType.SweepingBar: SpawnSweep(rim, speedScale, forceScale, dir); break;
                case ObstacleType.BouncingBall: SpawnBall(rim, speedScale, forceScale); break;
                case ObstacleType.StepBlocks: SpawnSteps(rim); break;
            }
        }

        // ---- factory ------------------------------------------------------------

        private void SpawnBoulder(Vector3 rim, float speedScale, float forceScale, Vector3 centerPos)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            go.name = "Boulder";
            go.transform.localScale = Vector3.one * 2.2f;
            go.transform.position = new Vector3(rim.x, 1.5f, rim.z);
            Tint(go, new Color(0.4f, 0.4f, 0.42f));

            var rb = go.AddComponent<Rigidbody>();
            rb.mass = 10f;
            rb.collisionDetectionMode = CollisionDetectionMode.Continuous;

            var boulder = go.AddComponent<RollingBoulder>();
            // Roll inward along the wave's rim direction instead of randomizing, so the boulder
            // tracks its telegraph. Inward = from the rim toward the arena center.
            boulder.SetLaunchDirection(centerPos - new Vector3(rim.x, centerPos.y, rim.z));
            boulder.Configure(_center, speedScale, forceScale);
        }

        private void SpawnRam(Vector3 rim, float speedScale, float forceScale)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = "SlidingRam";
            go.transform.localScale = new Vector3(3.5f, 2f, 1.6f);
            go.transform.position = new Vector3(rim.x, 1.1f, rim.z);
            Tint(go, new Color(0.7f, 0.25f, 0.2f));

            var col = go.GetComponent<Collider>();
            if (col != null) col.isTrigger = true;
            go.AddComponent<Rigidbody>();

            go.AddComponent<SlidingRam>().Configure(_center, speedScale, forceScale);
        }

        private void SpawnSweep(Vector3 rim, float speedScale, float forceScale, SpawnDirection dir)
        {
            // Cap the bar by the LIVE rim so it reaches roughly to centre but doesn't massively
            // overhang the far edge once the platform has shrunk.
            float length = Mathf.Min(CurrentRimRadius(), 10f);
            Vector3 centerPos = CurrentCenter();
            Vector3 inward = centerPos - rim; inward.y = 0f;
            if (inward.sqrMagnitude < 0.0001f) inward = Vector3.forward;
            inward.Normalize();

            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = "SweepingBar";
            go.transform.rotation = Quaternion.LookRotation(inward, Vector3.up);
            go.transform.localScale = new Vector3(0.7f, 1.8f, length);
            // Centre the bar midway between the rim pivot and its inner tip.
            go.transform.position = rim + inward * (length * 0.5f) + Vector3.up * 1.0f;
            Tint(go, new Color(0.85f, 0.7f, 0.2f));

            var col = go.GetComponent<Collider>();
            if (col != null) col.isTrigger = true;
            go.AddComponent<Rigidbody>();

            var bar = go.AddComponent<SweepingBar>();
            // Spin deterministically from the wave's rim octant (so the arc stops fighting the
            // pattern): octants on the N→SE half sweep clockwise, the rest counter-clockwise.
            bar.SetSpin(clockwise: (int)dir < ArenaDirections.Count / 2);
            bar.Configure(_center, speedScale, forceScale);
            bar.SetPivot(new Vector3(rim.x, go.transform.position.y, rim.z));
        }

        private void SpawnBall(Vector3 rim, float speedScale, float forceScale)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            go.name = "BouncingBall";
            go.transform.localScale = Vector3.one * 1.3f;
            go.transform.position = new Vector3(rim.x, 2.5f, rim.z);
            Tint(go, new Color(0.2f, 0.7f, 0.85f));

            var rb = go.AddComponent<Rigidbody>();
            rb.mass = 3f;

            var col = go.GetComponent<Collider>();
            if (col != null)
            {
                if (_bouncy == null)
                    _bouncy = new PhysicsMaterial("ArenaBouncy") { bounciness = 0.85f, bounceCombine = PhysicsMaterialCombine.Maximum };
                col.sharedMaterial = _bouncy;
            }

            go.AddComponent<BouncingBall>().Configure(_center, speedScale, forceScale);
        }

        private void SpawnSteps(Vector3 rim)
        {
            var go = new GameObject("StepBlocks");
            go.transform.position = new Vector3(rim.x, 0f, rim.z);
            go.AddComponent<StepBlocks>().Init(rim, _center, count: 5, blockSize: 1.6f, gap: 1.4f, maxHeight: 3.5f);
        }

        private static void Tint(GameObject go, Color color)
        {
            // URP/Lit material — the default primitive material is built-in Standard (pink in URP).
            RuntimeMaterial.Apply(go, color);
        }
    }
}
