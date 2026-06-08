using System.Collections.Generic;
using StumbleClone.Core;
using StumbleClone.Game;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace StumbleClone.Obstacles
{
    /// Self-bootstrapping power-up dispenser for the Knockout (Last-Standing) arena. Follows the
    /// <see cref="ArenaShrinker"/> / SceneAtmosphere pattern: [RuntimeInitializeOnLoadMethod] +
    /// SceneManager.sceneLoaded + an EnsureForScene name guard, so it works on the already-baked
    /// "Level_LastStanding" scene with no manual wiring.
    ///
    /// Every few seconds it drops a random <see cref="Powerup"/> at a random point inside the
    /// current safe radius, snapped down onto the ground. It keeps at most <see cref="MaxAlive"/>
    /// pickups live at once and stops spawning once the round ends (level ended / arena no longer
    /// running / no living racers).
    [DisallowMultipleComponent]
    public sealed class PowerupSpawner : MonoBehaviour
    {
        // ---- Tuning (local — mode-specific feel) ----
        private const float SpawnInterval = 6f;     // seconds between spawn attempts
        private const float FirstSpawnDelay = 8f;    // let the round settle before the first drop
        private const int MaxAlive = 3;              // cap on simultaneous live pickups
        private const float RadiusInset = 0.8f;      // keep drops comfortably inside the safe ring
        private const float MinRadius = 2f;          // don't bunch everything at the exact centre
        private const float GroundRayHeight = 6f;    // ray starts this far above the sampled point
        private const float GroundRayLength = 40f;   // max downward search distance
        private const float DefaultArenaRadius = 12f; // fallback when no bounds source is available

        private static PowerupSpawner _instance;

        // Live pickups we've spawned. Pruned of destroyed/collected entries each attempt so the
        // MaxAlive cap reflects reality without per-frame work.
        private readonly List<Powerup> _alive = new List<Powerup>(MaxAlive + 1);

        private Vector3 _arenaCenter;
        private float _arenaRadius = DefaultArenaRadius;
        private float _nextSpawnTime;
        private bool _running;
        private LayerMask _groundMask;

        // ---- Bootstrap ----

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            SceneManager.sceneLoaded += OnSceneLoaded;
            EnsureForScene(SceneManager.GetActiveScene());
        }

        private static void OnSceneLoaded(Scene s, LoadSceneMode m) => EnsureForScene(s);

        private static void EnsureForScene(Scene scene)
        {
            if (!scene.IsValid() || scene.name != "Level_LastStanding") return;
            if (_instance != null) return;
            _instance = new GameObject("PowerupSpawner").AddComponent<PowerupSpawner>();
        }

        // ---- Lifecycle ----

        private void Awake()
        {
            if (_instance != null && _instance != this) { Destroy(gameObject); return; }
            _instance = this;
            _groundMask = (1 << GameConstants.LayerGround) | (1 << GameConstants.LayerObstacle);
        }

        private void OnEnable()
        {
            GameEvents.LevelStarted += HandleLevelStarted;
            GameEvents.LevelEnded += HandleLevelEnded;
        }

        private void OnDisable()
        {
            GameEvents.LevelStarted -= HandleLevelStarted;
            GameEvents.LevelEnded -= HandleLevelEnded;
        }

        private void OnDestroy()
        {
            if (_instance == this) _instance = null;
        }

        private void HandleLevelStarted(LevelMode mode)
        {
            if (mode != LevelMode.LastStanding) return;
            ResolveArena();
            _alive.Clear();
            _nextSpawnTime = Time.time + FirstSpawnDelay;
            _running = true;
        }

        private void HandleLevelEnded(IRacer winner)
        {
            _running = false;
        }

        /// Static fallback for the arena bounds at round start (the shrinker may not be live yet).
        /// Per-spawn we prefer the live shrinker radius — see <see cref="CurrentCenterRadius"/>.
        private void ResolveArena()
        {
            var mgr = FindFirstObjectByType<LastStandingManager>();
            if (mgr != null && mgr.ArenaCenter != null)
            {
                _arenaCenter = mgr.ArenaCenter.position;
                _arenaRadius = Mathf.Max(MinRadius + 1f, mgr.ArenaRadius);
            }
            else
            {
                _arenaCenter = Vector3.zero;
                _arenaRadius = DefaultArenaRadius;
            }
        }

        // ---- Per-frame ----

        private void Update()
        {
            if (!_running) return;

            // The round is over once nobody's left standing — stop dispensing.
            if (RacerRegistry.AliveCount <= 1)
            {
                _running = false;
                return;
            }

            if (Time.time < _nextSpawnTime) return;
            _nextSpawnTime = Time.time + SpawnInterval;

            PruneDead();
            if (_alive.Count >= MaxAlive) return;

            TrySpawnOne();
        }

        private void PruneDead()
        {
            for (int i = _alive.Count - 1; i >= 0; i--)
                if (_alive[i] == null) _alive.RemoveAt(i);
        }

        // Prefer the live shrinking ring so drops follow the closing safe zone; fall back to the
        // static bounds resolved at round start.
        private void CurrentCenterRadius(out Vector3 center, out float radius)
        {
            if (ArenaShrinker.Active)
            {
                center = ArenaShrinker.Center;
                radius = ArenaShrinker.CurrentSafeRadius;
            }
            else
            {
                center = _arenaCenter;
                radius = _arenaRadius;
            }
        }

        private void TrySpawnOne()
        {
            CurrentCenterRadius(out Vector3 center, out float radius);

            float usable = radius - RadiusInset;
            if (usable < MinRadius) return; // ring too tight to place a fair pickup

            // Uniform-ish point in the disc: sqrt on the radius fraction so drops aren't centre-biased.
            float ang = Random.value * Mathf.PI * 2f;
            float dist = Mathf.Lerp(MinRadius, usable, Mathf.Sqrt(Random.value));
            Vector3 flat = new Vector3(center.x + Mathf.Cos(ang) * dist, center.y, center.z + Mathf.Sin(ang) * dist);

            if (!TryFindGround(flat, out Vector3 groundPoint)) return;

            var go = new GameObject("Powerup");
            var powerup = go.AddComponent<Powerup>();
            powerup.Configure(RandomType(), groundPoint);
            _alive.Add(powerup);
        }

        // Drop a ray from above the candidate XZ to find the floor it should hover over. Ignores
        // triggers so it lands on solid ground, not on another pickup's trigger volume.
        private bool TryFindGround(Vector3 flat, out Vector3 groundPoint)
        {
            Vector3 origin = new Vector3(flat.x, flat.y + GroundRayHeight, flat.z);
            if (Physics.Raycast(origin, Vector3.down, out RaycastHit hit, GroundRayLength,
                    _groundMask, QueryTriggerInteraction.Ignore))
            {
                groundPoint = hit.point;
                return true;
            }
            groundPoint = flat;
            return false; // nothing solid below — skip this attempt rather than float over a gap
        }

        private static PowerupType RandomType()
        {
            // Five equally-weighted types: the three timed/one-use buffs plus the two held items
            // (Broom / Slipper). Held items are player-only — a bot that grabs one just pops it.
            switch (Random.Range(0, 5))
            {
                case 0: return PowerupType.Speed;
                case 1: return PowerupType.Shield;
                case 2: return PowerupType.SuperJump;
                case 3: return PowerupType.Broom;
                default: return PowerupType.Slipper;
            }
        }
    }
}
