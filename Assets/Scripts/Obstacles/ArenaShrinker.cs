using StumbleClone.Core;
using StumbleClone.Game;
using Unity.AI.Navigation;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace StumbleClone.Obstacles
{
    /// Shrinking PLATFORM for the Knockout (Last-Standing) arena — the headline
    /// "make it harder" pressure system. Earlier this was an INVISIBLE radius check
    /// that eliminated racers who strayed past a circle while they were still standing
    /// on solid ground (the platform had been enlarged 1.6x by ArenaResizer but the
    /// safe radius was never re-scaled). That auto-killed players for no visible reason.
    ///
    /// Now the floor the player can SEE physically shrinks: after a grace period the
    /// "Arena" disc's X/Z scale eases down from its full size to ~30%, the MeshCollider
    /// follows the transform for free, and the NavMesh is rebuilt on a throttled cadence
    /// so bots keep their footing on the closing platform. Racers die by FALLING OFF the
    /// shrinking floor into the existing kill zone (KillZone / FallKillZone) — there is
    /// no invisible-radius elimination anymore.
    ///
    /// Single source of truth: the CURRENT platform radius is measured from the Arena's
    /// real mesh geometry, so it is correct regardless of ArenaResizer's enlargement.
    /// <see cref="CurrentSafeRadius"/>/<see cref="Center"/> expose the live platform edge so
    /// bots (LastStandBotBehavior) retreat to stay on the floor and ObstacleSpawner spawns
    /// hazards at the real rim as it closes.
    ///
    /// Self-bootstrapping (no scene wiring) on the "Level_LastStanding" scene only,
    /// following the SceneAtmosphere pattern: [RuntimeInitializeOnLoadMethod] +
    /// SceneManager.sceneLoaded + an EnsureForScene name guard.
    ///
    /// Ordering: ArenaResizer scales the platform in its sceneLoaded callback; this
    /// shrinker measures the platform on LevelStarted (raised after the scene is up and
    /// managers have enabled), so it always reads the already-enlarged geometry.
    [DisallowMultipleComponent]
    public sealed class ArenaShrinker : MonoBehaviour
    {
        // ---- Tuning -------------------------------------------------------------
        // Scene-only, gameplay-feel knobs. Kept local (not GameConstants) because
        // they are specific to this one mode's pressure curve.
        private const float DefaultFullRadius = 20f;   // used only if the Arena can't be found
        private const float GracePeriod = 15f;         // hold full size after level start
        private const float ShrinkDuration = 60f;      // time to contract from full → min
        private const float MinRadiusFraction = 0.3f;  // final radius as a fraction of full

        // Throttle the NavMesh rebuild: rebaking every frame while the disc scales would
        // hitch hard. Rebuild on a fixed cadence (and once at the end) so bots track the
        // shrinking walkable surface without per-frame cost.
        private const float NavRebuildInterval = 1.5f;

        // How far ahead (seconds) the danger band warns: its inner edge is drawn where the
        // floor WILL be this many seconds from now, so the red band covers the strip of floor
        // about to vanish and players get a heads-up to move inward before the lip reaches them.
        private const float WarnLookahead = 2.5f;

        private const string ArenaObjectName = "Arena";
        // A unit Cylinder primitive's mesh spans diameter 1 (radius 0.5) on X/Z; used as a
        // fallback if the MeshFilter's real extents are somehow unavailable.
        private const float UnitCylinderRadius = 0.5f;

        // ---- Static read API (bots / others) ------------------------------------
        private static ArenaShrinker _instance;

        /// True while a shrinker is live for the current Knockout round.
        public static bool Active => _instance != null && _instance._running;

        /// World-space centre of the platform (XZ matters; Y tracks the arena floor).
        public static Vector3 Center => _instance != null ? _instance._center : Vector3.zero;

        /// Current platform radius (full → MinRadiusFraction*full over the round). This IS
        /// the live edge of the visible floor — bots retreat to it; hazards spawn at it.
        public static float CurrentSafeRadius => _instance != null ? _instance._currentRadius : 0f;

        // ---- Instance state -----------------------------------------------------
        private Vector3 _center;
        private Transform _arena;          // the platform disc we scale
        private Vector3 _arenaRestScale;   // its full (post-ArenaResizer) scale, restored on destroy
        private float _meshRadiusX = UnitCylinderRadius; // local mesh half-extent on X (unit cylinder = 0.5)
        private float _meshRadiusZ = UnitCylinderRadius; // local mesh half-extent on Z

        private float _fullRadius = DefaultFullRadius;
        private float _currentRadius = DefaultFullRadius;
        private float _roundStartTime;
        private float _lastNavRebuild;
        private bool _running;
        private bool _navRebuiltAtMin;

        private NavMeshSurface[] _surfaces;
        private SafeZoneRing _ring;

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
            RestoreArena();
            if (_instance == this) _instance = null;
        }

        // ---- Round start / reset ------------------------------------------------

        private void HandleLevelStarted(LevelMode mode)
        {
            if (mode != LevelMode.LastStanding) return;
            ResolveArena();
            BeginRound();
        }

        /// Find the actual platform object and measure its CURRENT real radius from the
        /// renderer/mesh geometry — the single source of truth, correct no matter how
        /// ArenaResizer has scaled it. Captures the resting scale so the round can shrink
        /// from full and we can restore on teardown.
        private void ResolveArena()
        {
            _arena = FindArenaTransform();
            if (_arena != null)
            {
                _arenaRestScale = _arena.localScale;
                MeasureMeshRadius(_arena);
                _center = _arena.position;
                _fullRadius = Mathf.Max(1f, CurrentArenaRadius());
            }
            else
            {
                // No platform found — fall back to a manager centre / default so the system
                // still functions (visual ring only; nothing to scale).
                var mgr = FindFirstObjectByType<LastStandingManager>();
                _center = (mgr != null && mgr.ArenaCenter != null) ? mgr.ArenaCenter.position : Vector3.zero;
                _fullRadius = DefaultFullRadius;
            }
        }

        /// Prefer a ground-layer object literally named "Arena"; if the name ever changes,
        /// fall back to the LastStandingManager's centre object (mirrors ArenaTilt's lookup).
        private static Transform FindArenaTransform()
        {
            var go = GameObject.Find(ArenaObjectName);
            if (go != null && go.GetComponent<MeshFilter>() != null) return go.transform;

            var mgr = FindFirstObjectByType<LastStandingManager>();
            if (mgr != null && mgr.ArenaCenter != null) return mgr.ArenaCenter;
            return go != null ? go.transform : null;
        }

        /// Cache the platform mesh's local X/Z half-extents so radius can be derived from
        /// scale alone — rotation-independent (ArenaTilt leans the disc, which would inflate
        /// a world-space AABB; multiplying mesh extents by lossy scale avoids that error).
        private void MeasureMeshRadius(Transform arena)
        {
            var mf = arena.GetComponent<MeshFilter>();
            if (mf != null && mf.sharedMesh != null)
            {
                Vector3 ext = mf.sharedMesh.bounds.extents; // local-space half-extents
                _meshRadiusX = ext.x > 0.0001f ? ext.x : UnitCylinderRadius;
                _meshRadiusZ = ext.z > 0.0001f ? ext.z : UnitCylinderRadius;
            }
            else
            {
                _meshRadiusX = _meshRadiusZ = UnitCylinderRadius;
            }
        }

        /// Live platform radius from current geometry: mesh half-extent * current world scale.
        /// Uses the smaller of X/Z so the "safe" disc never overstates the floor on a
        /// non-square platform. Independent of any rotation ArenaTilt applies.
        private float CurrentArenaRadius()
        {
            if (_arena == null) return _fullRadius;
            Vector3 s = _arena.lossyScale;
            float rx = _meshRadiusX * Mathf.Abs(s.x);
            float rz = _meshRadiusZ * Mathf.Abs(s.z);
            return Mathf.Min(rx, rz);
        }

        private void BeginRound()
        {
            _roundStartTime = Time.time;
            _currentRadius = _fullRadius;
            _running = true;
            _lastNavRebuild = Time.time;
            _navRebuiltAtMin = false;

            _surfaces = Object.FindObjectsByType<NavMeshSurface>(FindObjectsSortMode.None);

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

            float targetRadius = ComputeTargetRadius();
            ApplyPlatformScale(targetRadius);

            // Keep the live radius reading from real geometry so bots/hazards always track
            // the floor exactly (also correct if some other system nudges the scale).
            _currentRadius = _arena != null ? CurrentArenaRadius() : targetRadius;
            _center = _arena != null ? _arena.position : _center;

            ThrottledNavRebuild(targetRadius);

            // Repurposed ring: a thin bright rim highlight drawn AT the live platform edge,
            // plus a translucent red danger band covering the floor about to vanish. The band's
            // inner edge is the WARN-LOOKAHEAD radius (where the floor will be in ~2.5s), mapped
            // onto the live measured radius so it tracks exactly even if other systems nudge scale.
            if (_ring != null)
            {
                _ring.Configure(_center, _fullRadius); // keep centre tracking the platform
                float warnInner = ComputeWarnInnerRadius(targetRadius);
                _ring.UpdateVisual(_currentRadius, warnInner, _fullRadius);
            }
        }

        /// Radius the danger band's INNER edge should sit at: the floor's projected position
        /// WarnLookahead seconds from now. Computed from the same shrink curve and rescaled to
        /// the live measured radius (currentRadius / targetRadius) so the band hugs the real lip
        /// regardless of mesh/scale quirks. Clamped to the minimum so it never undershoots the
        /// resting floor, and never exceeds the current edge (the band has zero width once settled).
        private float ComputeWarnInnerRadius(float targetRadius)
        {
            float minRadius = _fullRadius * MinRadiusFraction;
            float elapsed = Time.time - _roundStartTime;

            // Target radius WarnLookahead seconds into the future, on the same SmoothStep curve.
            float futureElapsed = elapsed + WarnLookahead;
            float futureTarget;
            if (futureElapsed <= GracePeriod)
            {
                futureTarget = _fullRadius;
            }
            else
            {
                float t = Mathf.Clamp01((futureElapsed - GracePeriod) / ShrinkDuration);
                futureTarget = Mathf.Lerp(_fullRadius, minRadius, Mathf.SmoothStep(0f, 1f, t));
            }

            // Rescale the projected (target-space) radius onto the live measured radius so the band
            // stays glued to the real edge. targetRadius is never ~0 here (>= minRadius > 0).
            float scale = targetRadius > 0.0001f ? (_currentRadius / targetRadius) : 1f;
            float inner = futureTarget * scale;

            return Mathf.Clamp(inner, minRadius, _currentRadius);
        }

        /// Hold full for the grace window, then ease down to the minimum over the shrink
        /// window, then hold the minimum. Returns the desired platform radius this frame.
        private float ComputeTargetRadius()
        {
            float elapsed = Time.time - _roundStartTime;
            float minRadius = _fullRadius * MinRadiusFraction;

            if (elapsed <= GracePeriod) return _fullRadius;

            float t = Mathf.Clamp01((elapsed - GracePeriod) / ShrinkDuration);
            // SmoothStep so the squeeze eases in and out rather than a linear creep.
            return Mathf.Lerp(_fullRadius, minRadius, Mathf.SmoothStep(0f, 1f, t));
        }

        /// Scale the Arena transform's X and Z so the visible floor matches the target radius,
        /// keeping Y (thickness) untouched. The MeshCollider follows the transform implicitly,
        /// so physics shrinks with the visual. localRotation is left alone so ArenaTilt's lean
        /// composes cleanly (scale and rotation are independent transform channels).
        private void ApplyPlatformScale(float targetRadius)
        {
            if (_arena == null) return;
            // Convert desired world radius back into the transform scale that produces it.
            // worldRadius = meshHalfExtent * worldScale  ⇒  worldScale = worldRadius / meshHalfExtent.
            // Parented arenas: dividing by the parent's lossy scale keeps localScale correct.
            float worldScaleX = targetRadius / Mathf.Max(0.0001f, _meshRadiusX);
            float worldScaleZ = targetRadius / Mathf.Max(0.0001f, _meshRadiusZ);

            Transform parent = _arena.parent;
            float parentX = parent != null ? Mathf.Abs(parent.lossyScale.x) : 1f;
            float parentZ = parent != null ? Mathf.Abs(parent.lossyScale.z) : 1f;

            Vector3 s = _arena.localScale;
            s.x = worldScaleX / Mathf.Max(0.0001f, parentX);
            s.z = worldScaleZ / Mathf.Max(0.0001f, parentZ);
            // Y untouched — thickness stays constant.
            _arena.localScale = s;
        }

        /// Rebuild the NavMesh on a throttled cadence (NOT every frame) while the platform
        /// shrinks, plus one final rebuild when it settles at the minimum, so bots stay on the
        /// closing floor without per-frame baking hitches. Mirrors GroundColliderFixer's pattern.
        private void ThrottledNavRebuild(float targetRadius)
        {
            if (_surfaces == null || _surfaces.Length == 0) return;

            bool atMin = Mathf.Approximately(targetRadius, _fullRadius * MinRadiusFraction)
                         || targetRadius <= _fullRadius * MinRadiusFraction + 0.01f;
            bool shrinking = (Time.time - _roundStartTime) > GracePeriod && !_navRebuiltAtMin;

            if (!shrinking) return;

            if (Time.time - _lastNavRebuild >= NavRebuildInterval)
            {
                RebuildNavMeshes();
                _lastNavRebuild = Time.time;
            }

            // One guaranteed final bake once we reach the minimum so the resting mesh is exact.
            if (atMin && !_navRebuiltAtMin)
            {
                RebuildNavMeshes();
                _navRebuiltAtMin = true;
            }
        }

        private void RebuildNavMeshes()
        {
            for (int i = 0; i < _surfaces.Length; i++)
            {
                if (_surfaces[i] == null) continue;
                _surfaces[i].BuildNavMesh(); // synchronous; uses the surface's baked settings
            }
        }

        private void RestoreArena()
        {
            if (_arena != null) _arena.localScale = _arenaRestScale;
            _running = false;
        }
    }
}
