using StumbleClone.Core;
using Unity.AI.Navigation;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace StumbleClone.Level
{
    /// Fixes a serialization-level trap in the cylinder arenas (Last Standing / Survival).
    ///
    /// Unity's Cylinder primitive ships with a CAPSULE collider, not a mesh collider. When the
    /// arena cylinder is scaled flat-and-wide (e.g. 40 x 0.4 x 40), the capsule's radius (20)
    /// dwarfs its half-height (0.4), so the collider degenerates into a ~20-unit SPHERE that
    /// bulges up to y≈20. The player spawns at ~(11, 1, 0) — INSIDE that sphere — and PhysX
    /// ejects the overlapping dynamic body, flinging it off the map. (Bots are unaffected: they
    /// ride the NavMesh as kinematic agents and never depenetrate.)
    ///
    /// This runs automatically on every gameplay scene load and swaps any ground-layer
    /// CapsuleCollider for a MeshCollider that follows the real (flat) mesh — so the already-baked
    /// scenes are corrected at runtime with no re-bake required. It runs in the sceneLoaded
    /// callback, before the first physics step, so there is no spawn-frame ejection.
    public static class GroundColliderFixer
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded; // guard against double-subscribe
            SceneManager.sceneLoaded += OnSceneLoaded;
            FixScene(SceneManager.GetActiveScene());
        }

        private static void OnSceneLoaded(Scene scene, LoadSceneMode mode) => FixScene(scene);

        private static void FixScene(Scene scene)
        {
            if (!scene.IsValid() || !scene.name.StartsWith("Level")) return;

            var capsules = Object.FindObjectsByType<CapsuleCollider>(FindObjectsSortMode.None);
            int fixedCount = 0;
            for (int i = 0; i < capsules.Length; i++)
            {
                CapsuleCollider cap = capsules[i];
                if (cap == null || cap.gameObject.layer != GameConstants.LayerGround) continue;

                GameObject go = cap.gameObject;
                var mf = go.GetComponent<MeshFilter>();
                if (mf == null || mf.sharedMesh == null) continue;
                if (go.GetComponent<MeshCollider>() != null) continue; // already corrected

                // DestroyImmediate (not Destroy): the capsule must be gone synchronously so it's
                // out of the physics sim before the first FixedUpdate AND excluded from the
                // NavMesh rebuild that follows in this same call (Destroy is deferred to
                // end-of-frame, which would re-bake the dome and re-eject the player).
                Object.DestroyImmediate(cap);

                var mc = go.AddComponent<MeshCollider>();
                mc.sharedMesh = mf.sharedMesh; // flat disc, matches the visual surface
                fixedCount++;
            }

            if (fixedCount > 0)
            {
                Debug.Log($"[GroundColliderFixer] '{scene.name}': replaced {fixedCount} degenerate ground CapsuleCollider(s) with MeshCollider(s) — player will no longer be ejected at spawn.");
                RebuildNavMeshes(scene);
            }
        }

        // The NavMesh was baked from PHYSICS COLLIDERS while the arena still had its degenerate
        // sphere collider, so it's a dome bulging to y≈20 — bots spawn in the air on it and die.
        // Now that the colliders are flat, rebuild every NavMeshSurface so the mesh sits on the
        // real ground. Runs in the sceneLoaded callback, before BotSpawner.Start samples it.
        private static void RebuildNavMeshes(Scene scene)
        {
            var surfaces = Object.FindObjectsByType<NavMeshSurface>(FindObjectsSortMode.None);
            int rebuilt = 0;
            for (int i = 0; i < surfaces.Length; i++)
            {
                if (surfaces[i] == null) continue;
                surfaces[i].BuildNavMesh(); // synchronous; uses the surface's baked settings
                rebuilt++;
            }
            if (rebuilt > 0)
                Debug.Log($"[GroundColliderFixer] '{scene.name}': rebuilt {rebuilt} NavMesh surface(s) on the corrected flat ground — bots now spawn on the arena.");
            else
                Debug.LogWarning($"[GroundColliderFixer] '{scene.name}': no NavMeshSurface found to rebuild — bots may still be off-mesh. Re-bake the NavMesh in-editor.");
        }
    }
}
