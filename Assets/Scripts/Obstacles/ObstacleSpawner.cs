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

    /// Drives the Knockout arena: spawns hazards from the platform rim aimed inward,
    /// escalating spawn rate / speed / force as time passes and as racers are eliminated.
    /// Built procedurally from primitives so the mode is fully playable before any art
    /// import (swap to prefabs later). Created at runtime by LastStandingManager via Begin().
    public sealed class ObstacleSpawner : MonoBehaviour
    {
        [SerializeField] private float spawnRadius = 18f;
        [SerializeField] private float rampDuration = 75f;
        [SerializeField] private float minInterval = 0.6f;
        [SerializeField] private float maxInterval = 2.8f;

        private static readonly ObstacleType[] PushTypes =
        {
            ObstacleType.RollingBoulder,
            ObstacleType.SlidingRam,
            ObstacleType.SweepingBar,
            ObstacleType.BouncingBall,
            ObstacleType.StepBlocks,
        };

        private Transform _center;
        private bool _spawning;
        private float _startTime;
        private int _initialAlive;
        private float _nextSpawnTime;
        private PhysicsMaterial _bouncy;

        /// Called by the mode manager once the level has started.
        public void Begin(Transform arenaCenter, float radius)
        {
            _center = arenaCenter;
            if (radius > 0.1f) spawnRadius = radius;
            _startTime = Time.time;
            _initialAlive = Mathf.Max(1, RacerRegistry.AliveCount);
            _nextSpawnTime = Time.time + 2f; // brief grace before the first hazard
            _spawning = true;
        }

        public void StopSpawning() => _spawning = false;

        private void Update()
        {
            if (!_spawning) return;
            if (RacerRegistry.AliveCount <= 1) { _spawning = false; return; }

            if (Time.time < _nextSpawnTime) return;

            float intensity = ComputeIntensity();
            SpawnOne(intensity);
            // At high intensity, occasionally double up for chaos.
            if (intensity > 0.6f && Random.value < (intensity - 0.6f)) SpawnOne(intensity);

            float interval = Mathf.Lerp(maxInterval, minInterval, intensity);
            _nextSpawnTime = Time.time + interval;
        }

        private float ComputeIntensity()
        {
            float timeT = Mathf.Clamp01((Time.time - _startTime) / rampDuration);
            int deaths = _initialAlive - RacerRegistry.AliveCount;
            float deathT = _initialAlive > 1 ? deaths / (float)(_initialAlive - 1) : 0f;
            return Mathf.Clamp01(0.5f * timeT + 0.5f * deathT);
        }

        private void SpawnOne(float intensity)
        {
            float speedScale = Mathf.Lerp(1f, 2f, intensity);
            float forceScale = Mathf.Lerp(1f, 1.7f, intensity);

            float angle = Random.value * Mathf.PI * 2f;
            Vector3 centerPos = _center != null ? _center.position : Vector3.zero;
            Vector3 rim = centerPos + new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * spawnRadius;

            ObstacleType type = PushTypes[Random.Range(0, PushTypes.Length)];
            switch (type)
            {
                case ObstacleType.RollingBoulder: SpawnBoulder(rim, speedScale, forceScale); break;
                case ObstacleType.SlidingRam: SpawnRam(rim, speedScale, forceScale); break;
                case ObstacleType.SweepingBar: SpawnSweep(rim, speedScale, forceScale); break;
                case ObstacleType.BouncingBall: SpawnBall(rim, speedScale, forceScale); break;
                case ObstacleType.StepBlocks: SpawnSteps(rim); break;
            }
        }

        // ---- factory ------------------------------------------------------------

        private void SpawnBoulder(Vector3 rim, float speedScale, float forceScale)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            go.name = "Boulder";
            go.transform.localScale = Vector3.one * 2.2f;
            go.transform.position = new Vector3(rim.x, 1.5f, rim.z);
            Tint(go, new Color(0.4f, 0.4f, 0.42f));

            var rb = go.AddComponent<Rigidbody>();
            rb.mass = 10f;
            rb.collisionDetectionMode = CollisionDetectionMode.Continuous;

            go.AddComponent<RollingBoulder>().Configure(_center, speedScale, forceScale);
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

        private void SpawnSweep(Vector3 rim, float speedScale, float forceScale)
        {
            float length = Mathf.Min(spawnRadius, 10f);
            Vector3 centerPos = _center != null ? _center.position : Vector3.zero;
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
            var rend = go.GetComponent<Renderer>();
            if (rend != null) rend.material.color = color;
        }
    }
}
