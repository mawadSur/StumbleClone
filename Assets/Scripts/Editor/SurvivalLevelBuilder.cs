#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using StumbleClone.Core;
using StumbleClone.Obstacles;
using StumbleClone.Level;

namespace StumbleClone.EditorTools
{
    public static class SurvivalLevelBuilder
    {
        [MenuItem("StumbleClone/Build Survival Level")]
        public static void Build()
        {
            var root = new GameObject("SurvivalLevel");
            Undo.RegisterCreatedObjectUndo(root, "Build Survival Level");

            BuildLighting(root.transform);
            BuildArena(root.transform);
            BuildSweepers(root.transform);
            BuildSafeZone(root.transform);
            BuildSpawnPoints(root.transform);
            BuildKillZone(root.transform);
            BuilderUtils.AttachByName(root.transform, "BotSpawner", "StumbleClone.Bots.BotSpawner", configure: comp =>
            {
                BuilderUtils.SetPrivate(comp, "mode", LevelMode.Survival);
                BuilderUtils.SetPrivate(comp, "botCount", GameConstants.DefaultBotsPerLevel);
            });
            BuilderUtils.AttachByName(root.transform, "SurvivalManager", "StumbleClone.Game.SurvivalManager");
            BuilderUtils.AttachByName(root.transform, "LevelSelfStart", "StumbleClone.Game.LevelSelfStart", configure: comp =>
            {
                BuilderUtils.SetPrivate(comp, "mode", LevelMode.Survival);
            });

            Selection.activeGameObject = root;
            Debug.Log("[SurvivalLevelBuilder] Built. Save as Assets/Scenes/Level_Survival.unity. Bake NavMesh from Window > AI > Navigation.");
        }

        private static void BuildLighting(Transform parent)
        {
            var sun = new GameObject("Sun");
            sun.transform.SetParent(parent, false);
            sun.transform.rotation = Quaternion.Euler(55f, 20f, 0f);
            var light = sun.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.1f;
        }

        private static void BuildArena(Transform parent)
        {
            var arena = BuilderUtils.CreatePrimitive(PrimitiveType.Cylinder, "Arena", parent, new Vector3(0f, 0f, 0f), new Vector3(30f, 0.5f, 30f), Color.HSVToRGB(0.12f, 0.4f, 0.95f));
            BuilderUtils.MarkGround(arena);
        }

        private static void BuildSweepers(Transform parent)
        {
            for (int i = 0; i < 4; i++)
            {
                float angle = i * 90f;
                var pivot = new GameObject($"Sweeper_{i}");
                pivot.transform.SetParent(parent, false);
                pivot.transform.position = new Vector3(0f, 1.0f, 0f);
                pivot.transform.rotation = Quaternion.Euler(0f, angle, 0f);
                var sb = pivot.AddComponent<SpinningBar>();
                BuilderUtils.SetPrivate(sb, "degreesPerSecond", 60f + i * 5f);

                var arm = BuilderUtils.CreatePrimitive(PrimitiveType.Cylinder, "Arm", pivot.transform, new Vector3(0f, 0f, 7f), new Vector3(0.5f, 7f, 0.5f), Color.HSVToRGB((i * 0.17f) % 1f, 0.7f, 0.9f));
                arm.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
                arm.AddComponent<SpinningBarChild>();
            }
        }

        private static void BuildSafeZone(Transform parent)
        {
            var safe = new GameObject("SafeZoneCenter");
            safe.transform.SetParent(parent, false);
            safe.transform.position = new Vector3(0f, 0.3f, 0f);
            var marker = BuilderUtils.CreatePrimitive(PrimitiveType.Cylinder, "SafeMarker", safe.transform, Vector3.zero, new Vector3(2.5f, 0.05f, 2.5f), new Color(0.4f, 0.95f, 0.4f, 0.7f));
            // Visual only — remove collider so racers don't catch on it.
            var col = marker.GetComponent<Collider>();
            if (col != null) Object.DestroyImmediate(col);
        }

        private static void BuildSpawnPoints(Transform parent)
        {
            var group = new GameObject("SpawnPoints");
            group.transform.SetParent(parent, false);
            int count = 8;
            float radius = 13f;
            for (int i = 0; i < count; i++)
            {
                float t = (i / (float)count) * Mathf.PI * 2f;
                var sp = new GameObject($"Spawn_{i}");
                sp.transform.SetParent(group.transform, false);
                sp.transform.position = new Vector3(Mathf.Cos(t) * radius, 1f, Mathf.Sin(t) * radius);
                sp.tag = GameConstants.TagRespawnPoint;
            }
        }

        private static void BuildKillZone(Transform parent)
        {
            var go = BuilderUtils.CreatePrimitive(PrimitiveType.Cube, "WorldKillZone", parent, new Vector3(0f, -10f, 0f), new Vector3(200f, 1f, 200f), new Color(0.6f, 0.1f, 0.1f, 0.4f));
            var col = go.GetComponent<Collider>();
            if (col != null) col.isTrigger = true;
            go.tag = GameConstants.TagKillzone;
            var killZone = go.AddComponent<KillZone>();
            BuilderUtils.SetPrivate(killZone, "fallbackMode", LevelMode.Survival);
        }
    }
}
#endif
