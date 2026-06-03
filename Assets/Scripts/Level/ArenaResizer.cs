using System.Collections;
using StumbleClone.Core;
using Unity.AI.Navigation;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace StumbleClone.Level
{
    /// Makes the arena PLATFORM much wider at runtime — no editor re-bake required.
    ///
    /// The cylinder arenas (Last Standing / Survival) are built flat-and-wide and the
    /// human player + 7 bots crowd a relatively small disc, so a stray shove sends racers
    /// off the edge far too easily. This widens the *physical* platform so there's more
    /// safe margin, WITHOUT touching any gameplay radius (BotSpawner.arenaRadius,
    /// ArenaShrinker, ObstacleSpawner, LastStandBotBehavior) — the play/hazard/shrink zone
    /// stays exactly where it was; there's simply more solid ground under and around it.
    ///
    /// What it does, on every gameplay scene load (scene name starts with "Level"):
    ///   1. Finds ground-layer arena objects named "Arena" (flattened cylinders) and scales
    ///      ONLY their X and Z by <see cref="WidenFactor"/> (Y/thickness unchanged). Scaling
    ///      the transform scales its MeshCollider with it, so physics follows for free.
    ///   2. Widens the FallKillZone (XZ only) so it still catches every fall, and pushes the
    ///      racer spawn points modestly outward (~1.25x their XZ distance from centre) so the
    ///      field spreads over the bigger platform instead of clumping in the middle.
    ///   3. Rebuilds every NavMeshSurface so bots get the enlarged walkable mesh.
    ///
    /// Ordering note: GroundColliderFixer also runs on scene load and rebuilds the NavMesh.
    /// Whichever of the two static initialisers runs first, this scales the arena in the
    /// sceneLoaded callback AND schedules a one-frame-later coroutine rebuild via a tiny
    /// runner — so the FINAL NavMesh always reflects the scaled arena before BotSpawner.Start
    /// (which runs after sceneLoaded) samples it.
    ///
    /// Idempotent: a per-object marker prevents double-scaling if a scene is re-entered.
    public static class ArenaResizer
    {
        /// XZ multiplier applied to the arena platform (much wider). Y is left untouched.
        private const float WidenFactor = 1.6f;

        /// XZ multiplier applied to spawn-point distance from centre (modest spread).
        private const float SpawnSpreadFactor = 1.25f;

        private const string ArenaObjectName = "Arena";
        private const string FallKillZoneName = "FallKillZone";

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded; // guard against double-subscribe
            SceneManager.sceneLoaded += OnSceneLoaded;
            ResizeScene(SceneManager.GetActiveScene());
        }

        private static void OnSceneLoaded(Scene scene, LoadSceneMode mode) => ResizeScene(scene);

        private static void ResizeScene(Scene scene)
        {
            if (!scene.IsValid() || !scene.name.StartsWith("Level")) return;

            int scaled = ScaleArenas();
            if (scaled == 0) return; // no arena found — do nothing (safe)

            WidenFallKillZone();
            SpreadSpawnPoints();

            // Rebuild now so the mesh reflects the scaled arena even if we ran AFTER
            // GroundColliderFixer's own rebuild...
            RebuildNavMeshes(scene, "immediate");

            // ...and again one frame later via a tiny runner, so that if GroundColliderFixer
            // happened to run AFTER us this frame (re-baking the smaller arena), the final
            // mesh is still the enlarged one. BotSpawner.Start runs after sceneLoaded, so a
            // one-frame-later rebuild still lands before bots sample the NavMesh.
            ArenaResizerRunner.ScheduleDeferredRebuild(scene);
        }

        /// Scales every ground-layer "Arena" cylinder on X/Z only. Returns how many were scaled.
        private static int ScaleArenas()
        {
            int count = 0;
            var renderers = Object.FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None);
            for (int i = 0; i < renderers.Length; i++)
            {
                MeshRenderer mr = renderers[i];
                if (mr == null) continue;
                GameObject go = mr.gameObject;

                // Match the arena: named "Arena", OR a flattened ground-layer cylinder
                // (XZ much larger than Y). Either is sufficient.
                bool nameMatch = go.name == ArenaObjectName;
                bool groundFlatMatch = go.layer == GameConstants.LayerGround && IsFlattenedDisc(go.transform);
                if (!nameMatch && !groundFlatMatch) continue;

                // Idempotent: skip anything we've already widened (re-entered scene).
                if (go.GetComponent<ArenaResizerMarker>() != null) continue;

                Vector3 s = go.transform.localScale;
                go.transform.localScale = new Vector3(s.x * WidenFactor, s.y, s.z * WidenFactor);
                go.AddComponent<ArenaResizerMarker>();
                count++;
            }

            if (count > 0)
                Debug.Log($"[ArenaResizer] Widened {count} arena platform(s) to {WidenFactor:0.0}x XZ (thickness unchanged) — more safe margin, no re-bake. Gameplay/hazard radii left untouched.");
            return count;
        }

        // A "flattened disc" arena has a much larger footprint (XZ) than thickness (Y).
        private static bool IsFlattenedDisc(Transform t)
        {
            Vector3 s = t.localScale;
            float footprint = Mathf.Min(s.x, s.z);
            return footprint > 5f && footprint > s.y * 8f;
        }

        /// Widens the fall killzone on XZ so it still catches every fall over the bigger platform.
        private static void WidenFallKillZone()
        {
            GameObject kz = GameObject.Find(FallKillZoneName);
            if (kz == null) return; // not present (e.g. Survival uses "WorldKillZone") — safe skip
            if (kz.GetComponent<ArenaResizerMarker>() != null) return; // already widened

            Vector3 s = kz.transform.localScale;
            kz.transform.localScale = new Vector3(s.x * WidenFactor, s.y, s.z * WidenFactor);
            kz.AddComponent<ArenaResizerMarker>();
            Debug.Log($"[ArenaResizer] Widened '{FallKillZoneName}' to {WidenFactor:0.0}x XZ so it still catches falls off the enlarged platform.");
        }

        /// Pushes Respawn-tagged spawn points modestly outward from world-XZ centre so racers
        /// spread over the bigger platform. Does NOT change any radius tuning value.
        private static void SpreadSpawnPoints()
        {
            GameObject[] points = GameObject.FindGameObjectsWithTag(GameConstants.TagRespawnPoint);
            if (points == null || points.Length == 0) return; // none reachable — skip (safe)

            int moved = 0;
            for (int i = 0; i < points.Length; i++)
            {
                GameObject p = points[i];
                if (p == null) continue;
                if (p.GetComponent<ArenaResizerMarker>() != null) continue; // idempotent

                Vector3 pos = p.transform.position;
                pos.x *= SpawnSpreadFactor; // scale XZ distance from world centre; keep Y (height)
                pos.z *= SpawnSpreadFactor;
                p.transform.position = pos;
                p.AddComponent<ArenaResizerMarker>();
                moved++;
            }

            if (moved > 0)
                Debug.Log($"[ArenaResizer] Spread {moved} spawn point(s) outward to {SpawnSpreadFactor:0.00}x XZ for the enlarged platform.");
        }

        // Same pattern as GroundColliderFixer.RebuildNavMeshes: synchronous BuildNavMesh on
        // every NavMeshSurface using its own baked settings.
        private static void RebuildNavMeshes(Scene scene, string phase)
        {
            var surfaces = Object.FindObjectsByType<NavMeshSurface>(FindObjectsSortMode.None);
            int rebuilt = 0;
            for (int i = 0; i < surfaces.Length; i++)
            {
                if (surfaces[i] == null) continue;
                surfaces[i].BuildNavMesh();
                rebuilt++;
            }
            if (rebuilt > 0)
                Debug.Log($"[ArenaResizer] '{scene.name}': rebuilt {rebuilt} NavMesh surface(s) on the enlarged arena ({phase}) — bots walk the wider platform.");
            else
                Debug.LogWarning($"[ArenaResizer] '{scene.name}': no NavMeshSurface found to rebuild ({phase}).");
        }

        // Internal hook so the runner (a MonoBehaviour) can reuse the static rebuild logic.
        internal static void RebuildNavMeshesDeferred(Scene scene) => RebuildNavMeshes(scene, "deferred");
    }

    /// Per-object tag-component. Its sole purpose is to mark objects ArenaResizer has already
    /// modified so re-entering a scene (or both bootstrap paths firing) never double-scales.
    [DisallowMultipleComponent]
    internal sealed class ArenaResizerMarker : MonoBehaviour { }

    /// Tiny hidden runner so the static ArenaResizer can run a one-frame-later coroutine.
    /// Survives scene loads (one instance) and rebuilds the NavMesh again after every fixer
    /// on the load frame has had its turn, guaranteeing the enlarged arena is the final state.
    internal sealed class ArenaResizerRunner : MonoBehaviour
    {
        private static ArenaResizerRunner _instance;

        internal static void ScheduleDeferredRebuild(Scene scene)
        {
            if (_instance == null)
            {
                var go = new GameObject("~ArenaResizerRunner") { hideFlags = HideFlags.HideAndDontSave };
                Object.DontDestroyOnLoad(go);
                _instance = go.AddComponent<ArenaResizerRunner>();
            }
            _instance.StartCoroutine(_instance.DeferredRebuild(scene));
        }

        private IEnumerator DeferredRebuild(Scene scene)
        {
            yield return null; // wait one frame so any other scene-load fixer has run its rebuild
            if (scene.IsValid() && scene.isLoaded)
                ArenaResizer.RebuildNavMeshesDeferred(scene);
        }
    }
}
