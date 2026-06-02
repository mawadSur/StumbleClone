using System.Collections.Generic;
using StumbleClone.Core;
using StumbleClone.Game;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace StumbleClone.Obstacles
{
    /// Shrinking safe-zone for the Knockout (Last-Standing) arena. The headline
    /// "make it harder" pressure system: a circular safe radius holds full for a
    /// short grace, then contracts to ~30% over the round. Racers stranded outside
    /// the ring get a periodic inward shove and, if they stay out too long, are
    /// eliminated. Bots read <see cref="CurrentSafeRadius"/>/<see cref="Center"/> to
    /// keep themselves inside the closing ring.
    ///
    /// Self-bootstrapping (no scene wiring) on the "Level_LastStanding" scene only,
    /// following the SceneAtmosphere pattern: [RuntimeInitializeOnLoadMethod] +
    /// SceneManager.sceneLoaded + an EnsureForScene name guard. It tries to read the
    /// arena centre/radius from <see cref="LastStandingManager"/>; if absent it falls
    /// back to world origin and a sensible default radius.
    [DisallowMultipleComponent]
    public sealed class ArenaShrinker : MonoBehaviour
    {
        // ---- Tuning -------------------------------------------------------------
        // Scene-only, gameplay-feel knobs. Kept local (not GameConstants) because
        // they are specific to this one mode's pressure curve.
        private const float DefaultFullRadius = 12f;   // used when no manager radius is found
        private const float GracePeriod = 15f;         // hold full radius after level start
        private const float ShrinkDuration = 60f;      // time to contract from full → min
        private const float MinRadiusFraction = 0.3f;  // final radius as a fraction of full

        private const float SpawnGrace = 4f;           // never eliminate in the first seconds
        private const float OutsideKillTime = 3f;      // continuous seconds outside → eliminate
        private const float NudgeInterval = 0.6f;      // throttle inward shoves (Knockback pops up)
        private const float NudgeForce = 3.5f;         // small planar impulse toward centre

        // ---- Static read API (bots / others) ------------------------------------
        private static ArenaShrinker _instance;

        /// True while a shrinker is live for the current Knockout round.
        public static bool Active => _instance != null && _instance._running;

        /// World-space centre of the safe zone (XZ matters; Y tracks the arena floor).
        public static Vector3 Center => _instance != null ? _instance._center : Vector3.zero;

        /// Current effective safe radius (full → MinRadiusFraction*full over the round).
        public static float CurrentSafeRadius => _instance != null ? _instance._currentRadius : 0f;

        // ---- Instance state -----------------------------------------------------
        private Vector3 _center;
        private float _fullRadius = DefaultFullRadius;
        private float _currentRadius = DefaultFullRadius;
        private float _roundStartTime;
        private bool _running;

        private SafeZoneRing _ring;

        // Per-racer "time first stepped outside" tracker. Reused across frames; we
        // prune entries for racers that come back in or die, so it stays small.
        private readonly Dictionary<IRacer, float> _outsideSince = new Dictionary<IRacer, float>(16);
        private readonly Dictionary<IRacer, float> _lastNudge = new Dictionary<IRacer, float>(16);
        // Scratch buffer reused each frame to avoid allocating while pruning the dict.
        private readonly List<IRacer> _pruneScratch = new List<IRacer>(16);

        // ---- Bootstrap ----------------------------------------------------------

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
            _instance = new GameObject("ArenaShrinker").AddComponent<ArenaShrinker>();
        }

        // ---- Lifecycle ----------------------------------------------------------

        private void Awake()
        {
            if (_instance != null && _instance != this) { Destroy(gameObject); return; }
            _instance = this;
        }

        private void OnEnable()
        {
            GameEvents.LevelStarted += HandleLevelStarted;
        }

        private void OnDisable()
        {
            GameEvents.LevelStarted -= HandleLevelStarted;
        }

        private void OnDestroy()
        {
            if (_instance == this) _instance = null;
        }

        // ---- Round start / reset ------------------------------------------------

        private void HandleLevelStarted(LevelMode mode)
        {
            if (mode != LevelMode.LastStanding) return;
            ResolveArena();
            BeginRound();
        }

        /// Find the arena centre + radius. Prefers the mode manager's serialized values;
        /// falls back to world origin and a default radius so the system still functions
        /// if the manager is missing or unwired.
        private void ResolveArena()
        {
            var mgr = FindFirstObjectByType<LastStandingManager>();
            if (mgr != null && mgr.ArenaCenter != null)
            {
                _center = mgr.ArenaCenter.position;
                // The manager's arenaRadius is the hazard-spawn rim (just OUTSIDE the
                // platform edge). Pull the playable safe radius in a touch so the final
                // ring sits on solid ground, not at the lip.
                _fullRadius = Mathf.Max(4f, mgr.ArenaRadius * 0.85f);
            }
            else
            {
                _center = Vector3.zero;
                _fullRadius = DefaultFullRadius;
            }
        }

        private void BeginRound()
        {
            _roundStartTime = Time.time;
            _currentRadius = _fullRadius;
            _running = true;
            _outsideSince.Clear();
            _lastNudge.Clear();

            EnsureRing();
            _ring.Configure(_center, _fullRadius);
        }

        private void EnsureRing()
        {
            if (_ring != null) return;
            var go = new GameObject("SafeZoneRing");
            go.transform.SetParent(transform, false);
            _ring = go.AddComponent<SafeZoneRing>();
        }

        // ---- Per-frame ----------------------------------------------------------

        private void Update()
        {
            if (!_running) return;

            UpdateRadius();
            if (_ring != null) _ring.UpdateVisual(_currentRadius, _fullRadius);

            PunishOutsideRacers();
        }

        /// Hold full for the grace window, then ease down to the minimum over the
        /// shrink window, then hold the minimum.
        private void UpdateRadius()
        {
            float elapsed = Time.time - _roundStartTime;
            float minRadius = _fullRadius * MinRadiusFraction;

            if (elapsed <= GracePeriod)
            {
                _currentRadius = _fullRadius;
                return;
            }

            float t = Mathf.Clamp01((elapsed - GracePeriod) / ShrinkDuration);
            // SmoothStep so the squeeze eases in and out rather than a linear creep.
            _currentRadius = Mathf.Lerp(_fullRadius, minRadius, Mathf.SmoothStep(0f, 1f, t));
        }

        /// Each frame: anyone whose horizontal distance from centre exceeds the safe
        /// radius accrues "outside" time, gets a throttled inward nudge, and is
        /// eliminated once continuously outside past <see cref="OutsideKillTime"/>.
        private void PunishOutsideRacers()
        {
            bool pastSpawnGrace = (Time.time - _roundStartTime) >= SpawnGrace;
            float radSqr = _currentRadius * _currentRadius;
            var all = RacerRegistry.All;

            for (int i = 0; i < all.Count; i++)
            {
                IRacer r = all[i];
                if (r == null) continue;
                if (!r.IsAlive || r.IsFinished)
                {
                    if (_outsideSince.ContainsKey(r)) _outsideSince.Remove(r);
                    continue;
                }

                Vector3 p = r.Transform.position;
                float dx = p.x - _center.x;
                float dz = p.z - _center.z;
                float distSqr = dx * dx + dz * dz;

                if (distSqr <= radSqr)
                {
                    // Back inside — clear the timer.
                    if (_outsideSince.ContainsKey(r)) _outsideSince.Remove(r);
                    continue;
                }

                // Outside the ring. Start (or read) the per-racer timer.
                if (!_outsideSince.TryGetValue(r, out float since))
                {
                    since = Time.time;
                    _outsideSince[r] = since;
                }

                // Gentle, throttled inward shove so being outside reads as escalating
                // pressure. Knockback adds a fixed upward pop + input lock, so we must
                // NOT call it every frame — throttle per racer.
                ApplyInwardNudge(r, p);

                // Eliminate after sustained exposure, but never during spawn grace.
                if (pastSpawnGrace && (Time.time - since) >= OutsideKillTime)
                {
                    r.Eliminate();
                    _outsideSince.Remove(r);
                    if (_lastNudge.ContainsKey(r)) _lastNudge.Remove(r);
                }
            }

            PruneStaleTrackers(all);
        }

        private void ApplyInwardNudge(IRacer r, Vector3 pos)
        {
            if (_lastNudge.TryGetValue(r, out float last) && (Time.time - last) < NudgeInterval)
                return;

            Vector3 inward = _center - pos;
            inward.y = 0f;
            if (inward.sqrMagnitude < 0.0001f) return;
            inward.Normalize();

            r.Knockback(inward * NudgeForce);
            _lastNudge[r] = Time.time;
        }

        /// Drop tracker entries for racers no longer in the registry (scene swap,
        /// destroyed) so the dictionaries don't hold stale references.
        private void PruneStaleTrackers(IReadOnlyList<IRacer> all)
        {
            if (_outsideSince.Count == 0 && _lastNudge.Count == 0) return;

            _pruneScratch.Clear();
            foreach (var kv in _outsideSince)
                if (!ContainsRacer(all, kv.Key)) _pruneScratch.Add(kv.Key);
            for (int i = 0; i < _pruneScratch.Count; i++)
            {
                _outsideSince.Remove(_pruneScratch[i]);
                _lastNudge.Remove(_pruneScratch[i]);
            }

            _pruneScratch.Clear();
            foreach (var kv in _lastNudge)
                if (!ContainsRacer(all, kv.Key)) _pruneScratch.Add(kv.Key);
            for (int i = 0; i < _pruneScratch.Count; i++)
                _lastNudge.Remove(_pruneScratch[i]);
        }

        private static bool ContainsRacer(IReadOnlyList<IRacer> all, IRacer r)
        {
            for (int i = 0; i < all.Count; i++)
                if (all[i] == r) return true;
            return false;
        }
    }
}
