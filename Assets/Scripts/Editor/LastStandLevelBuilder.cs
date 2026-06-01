#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using StumbleClone.Core;
using StumbleClone.Obstacles;
using StumbleClone.Level;

namespace StumbleClone.EditorTools
{
    public static class LastStandLevelBuilder
    {
        [MenuItem("StumbleClone/Build Last Stand Level")]
        public static void Build()
        {
            var root = new GameObject("LastStandLevel");
            Undo.RegisterCreatedObjectUndo(root, "Build Last Stand Level");

            BuildLighting(root.transform);
            BuildArena(root.transform);
            BuildSpawnPoints(root.transform);
            BuildFallKillZone(root.transform);

            BuilderUtils.AttachByName(root.transform, "BotSpawner", "StumbleClone.Bots.BotSpawner", configure: comp =>
            {
                BuilderUtils.SetPrivate(comp, "mode", LevelMode.LastStanding);
                BuilderUtils.SetPrivate(comp, "botCount", GameConstants.DefaultBotsPerLevel);
                BuilderUtils.SetPrivate(comp, "spawnPointOffset", 1); // player owns point 0
            });
            BuilderUtils.AttachByName(root.transform, "LastStandingManager", "StumbleClone.Game.LastStandingManager");

            // Lets the scene run a full round (obstacle spawner, spectate overlay, win
            // check) when Played directly in the editor, with no MainMenu/GameManager.
            BuilderUtils.AttachByName(root.transform, "LevelSelfStart", "StumbleClone.Game.LevelSelfStart", configure: comp =>
            {
                BuilderUtils.SetPrivate(comp, "mode", LevelMode.LastStanding);
            });

            Selection.activeGameObject = root;
            Debug.Log("[LastStandLevelBuilder] Built. Save as Assets/Scenes/Level_LastStanding.unity. Hazards spawn from the rim in telegraphed patterns; last racer alive wins.");
        }

        private static void BuildLighting(Transform parent)
        {
            var sun = new GameObject("Sun");
            sun.transform.SetParent(parent, false);
            sun.transform.rotation = Quaternion.Euler(45f, 45f, 0f);
            var light = sun.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.0f;
        }

        private static void BuildArena(Transform parent)
        {
            var arena = BuilderUtils.CreatePrimitive(PrimitiveType.Cylinder, "Arena", parent, new Vector3(0f, 0f, 0f), new Vector3(40f, 0.4f, 40f), Color.HSVToRGB(0.6f, 0.3f, 0.9f));
            BuilderUtils.MarkGround(arena);
            BuilderUtils.UseMeshGroundCollider(arena); // cylinder CapsuleCollider would eject the player
        }

        private static void BuildSpawnPoints(Transform parent)
        {
            var group = new GameObject("SpawnPoints");
            group.transform.SetParent(parent, false);
            // Ring well inside the radius-20 arena so nobody starts on the lip / in the
            // rim where ObstacleSpawner launches hazards (radius ~18). Player takes
            // point 0; bots fill 1..7 (BotSpawner.spawnPointOffset = 1).
            int count = 8;
            float radius = 11f;
            for (int i = 0; i < count; i++)
            {
                float t = (i / (float)count) * Mathf.PI * 2f;
                var sp = new GameObject($"Spawn_{i}");
                sp.transform.SetParent(group.transform, false);
                sp.transform.position = new Vector3(Mathf.Cos(t) * radius, 1f, Mathf.Sin(t) * radius);
                sp.tag = GameConstants.TagRespawnPoint;
            }
        }

        private static void BuildFallKillZone(Transform parent)
        {
            // Low killzone catches any racer that falls off the arena edge.
            var go = BuilderUtils.CreatePrimitive(PrimitiveType.Cube, "FallKillZone", parent, new Vector3(0f, -10f, 0f), new Vector3(200f, 1f, 200f), new Color(0.6f, 0.1f, 0.1f, 0.4f));
            var col = go.GetComponent<Collider>();
            if (col != null) col.isTrigger = true;
            go.tag = GameConstants.TagKillzone;
            var killZone = go.AddComponent<KillZone>();
            // So a directly-played scene (no GameManager) still eliminates instead of
            // respawning the fallen player on top.
            BuilderUtils.SetPrivate(killZone, "fallbackMode", LevelMode.LastStanding);
        }
    }
}
#endif
