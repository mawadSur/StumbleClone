#if UNITY_EDITOR
using System;
using UnityEditor;
using UnityEngine;
using StumbleClone.Core;
using StumbleClone.Obstacles;
using StumbleClone.Level;

namespace StumbleClone.EditorTools
{
    public static class RaceLevelBuilder
    {
        [MenuItem("StumbleClone/Build Race Level")]
        public static void Build()
        {
            var root = new GameObject("RaceLevel");
            Undo.RegisterCreatedObjectUndo(root, "Build Race Level");

            BuildLighting(root.transform);
            float currentZ = 0f;

            currentZ = BuildStartPlatform(root.transform, currentZ);
            AddCheckpoint(root.transform, new Vector3(0f, 0.6f, currentZ - 15f), 0);
            AddSpawnPoints(root.transform, new Vector3(0f, 1.2f, currentZ - 18f));

            currentZ = BuildSpinningBarSection(root.transform, currentZ);
            AddCheckpoint(root.transform, new Vector3(0f, 0.6f, currentZ - 2f), 1);

            currentZ = BuildHammerSection(root.transform, currentZ);
            AddCheckpoint(root.transform, new Vector3(0f, 0.6f, currentZ - 2f), 2);

            currentZ = BuildMovingPlatformSection(root.transform, currentZ);
            AddCheckpoint(root.transform, new Vector3(0f, 0.6f, currentZ - 2f), 3);

            currentZ = BuildFinishPlatform(root.transform, currentZ);

            BuildWorldKillZone(root.transform);
            BuildBotSpawner(root.transform);
            BuildRaceManager(root.transform);
            BuilderUtils.AttachByName(root.transform, "LevelSelfStart", "StumbleClone.Game.LevelSelfStart", configure: comp =>
            {
                BuilderUtils.SetPrivate(comp, "mode", LevelMode.Race);
            });

            Selection.activeGameObject = root;
            Debug.Log("[RaceLevelBuilder] Built. Next: open Window > AI > Navigation, bake NavMesh, then save scene as Assets/Scenes/Level_Race.unity.");
        }

        private static void BuildLighting(Transform parent)
        {
            var sun = new GameObject("Sun");
            sun.transform.SetParent(parent, false);
            sun.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
            var light = sun.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.1f;
        }

        private static float BuildStartPlatform(Transform parent, float startZ)
        {
            float len = 20f;
            var plat = BuilderUtils.CreateBox("StartPlatform", parent, new Vector3(0f, 0f, startZ - len * 0.5f), new Vector3(14f, 1f, len), Hue(0));
            BuilderUtils.MarkGround(plat);
            return startZ - len;
        }

        private static float BuildSpinningBarSection(Transform parent, float startZ)
        {
            float len = 30f;
            var floor = BuilderUtils.CreateBox("Section1_Floor", parent, new Vector3(0f, 0f, startZ - len * 0.5f), new Vector3(12f, 1f, len), Hue(1));
            BuilderUtils.MarkGround(floor);

            for (int i = 0; i < 2; i++)
            {
                float z = startZ - 8f - i * 12f;
                var post = BuilderUtils.CreateBox($"BarPost_{i}", parent, new Vector3(0f, 1.5f, z), new Vector3(0.4f, 2f, 0.4f), Color.gray);
                var barRoot = new GameObject($"SpinningBar_{i}");
                barRoot.transform.SetParent(parent, false);
                barRoot.transform.position = new Vector3(0f, 1.5f, z);
                barRoot.AddComponent<SpinningBar>();
                var bar = BuilderUtils.CreatePrimitive(PrimitiveType.Cylinder, "BarBody", barRoot.transform, Vector3.zero, new Vector3(0.6f, 5f, 0.6f), Hue(1));
                bar.transform.localRotation = Quaternion.Euler(0f, 0f, 90f);
                bar.AddComponent<SpinningBarChild>();
            }

            // Gap with killzone underneath comes naturally from WorldKillZone at Y=-25.
            float gapStart = startZ - len;
            var gapMarker = new GameObject("Section1_GapMarker");
            gapMarker.transform.SetParent(parent, false);
            gapMarker.transform.position = new Vector3(0f, 0f, gapStart - 3f);
            return gapStart - 6f; // 6m gap to jump
        }

        private static float BuildHammerSection(Transform parent, float startZ)
        {
            float len = 36f;
            var floor = BuilderUtils.CreateBox("Section2_Floor", parent, new Vector3(0f, 0f, startZ - len * 0.5f), new Vector3(12f, 1f, len), Hue(2));
            BuilderUtils.MarkGround(floor);

            for (int i = 0; i < 3; i++)
            {
                float z = startZ - 8f - i * 10f;
                var pivot = new GameObject($"SwingingHammer_{i}");
                pivot.transform.SetParent(parent, false);
                pivot.transform.position = new Vector3(0f, 6f, z);
                var hammer = pivot.AddComponent<SwingingHammer>();
                BuilderUtils.SetPrivate(hammer, "phaseOffset", i * 0.7f);
                var arm = BuilderUtils.CreatePrimitive(PrimitiveType.Cube, "Arm", pivot.transform, new Vector3(0f, -2.5f, 0f), new Vector3(0.4f, 5f, 0.4f), Color.gray);
                arm.AddComponent<SwingingHammerChild>();
                var head = BuilderUtils.CreatePrimitive(PrimitiveType.Cube, "Head", pivot.transform, new Vector3(0f, -5.5f, 0f), new Vector3(2.5f, 1.5f, 2.5f), Hue(2));
                head.AddComponent<SwingingHammerChild>();
            }
            return startZ - len;
        }

        private static float BuildMovingPlatformSection(Transform parent, float startZ)
        {
            float len = 30f;
            // No floor here — racers must use moving platforms over a kill drop.
            for (int i = 0; i < 3; i++)
            {
                float z = startZ - 6f - i * 9f;
                var a = new GameObject($"MP_PointA_{i}").transform;
                a.SetParent(parent, false);
                a.position = new Vector3(-5f, 0.5f, z);
                var b = new GameObject($"MP_PointB_{i}").transform;
                b.SetParent(parent, false);
                b.position = new Vector3(5f, 0.5f, z);

                var plat = BuilderUtils.CreatePrimitive(PrimitiveType.Cube, $"MovingPlatform_{i}", parent, new Vector3(0f, 0.5f, z), new Vector3(4f, 0.4f, 4f), Hue(3));
                BuilderUtils.MarkGround(plat);
                var mp = plat.AddComponent<MovingPlatform>();
                BuilderUtils.SetPrivate(mp, "pointA", a);
                BuilderUtils.SetPrivate(mp, "pointB", b);
                BuilderUtils.SetPrivate(mp, "speed", 0.8f + i * 0.15f);
                BuilderUtils.SetPrivate(mp, "phaseOffset", i * 1.1f);

                // Trigger volume above the platform handles parenting.
                var trig = new GameObject("ParentTrigger");
                trig.transform.SetParent(plat.transform, false);
                trig.transform.localPosition = new Vector3(0f, 1f, 0f);
                var box = trig.AddComponent<BoxCollider>();
                box.isTrigger = true;
                box.size = new Vector3(4f, 1.6f, 4f);
                var relay = trig.AddComponent<MovingPlatformTriggerRelay>();
                relay.target = mp;
            }
            return startZ - len;
        }

        private static float BuildFinishPlatform(Transform parent, float startZ)
        {
            float len = 20f;
            var plat = BuilderUtils.CreateBox("FinishPlatform", parent, new Vector3(0f, 0f, startZ - len * 0.5f), new Vector3(14f, 1f, len), Hue(4));
            BuilderUtils.MarkGround(plat);

            float finishZ = startZ - 50f;
            var finishVisual = BuilderUtils.CreateBox("FinishVisual", parent, new Vector3(0f, 2.5f, finishZ), new Vector3(14f, 5f, 0.3f), Color.yellow);
            var fl = finishVisual.AddComponent<FinishLine>();
            var col = finishVisual.GetComponent<Collider>();
            if (col != null) col.isTrigger = true;
            finishVisual.tag = GameConstants.TagFinish;
            return finishZ;
        }

        private static void BuildWorldKillZone(Transform parent)
        {
            var go = BuilderUtils.CreatePrimitive(PrimitiveType.Cube, "WorldKillZone", parent, new Vector3(0f, -20f, -100f), new Vector3(400f, 1f, 600f), new Color(0.6f, 0.1f, 0.1f, 0.4f));
            var col = go.GetComponent<Collider>();
            if (col != null) col.isTrigger = true;
            go.tag = GameConstants.TagKillzone;
            var killZone = go.AddComponent<KillZone>();
            BuilderUtils.SetPrivate(killZone, "fallbackMode", LevelMode.Race);
        }

        private static void AddCheckpoint(Transform parent, Vector3 pos, int order)
        {
            var go = new GameObject($"Checkpoint_{order}");
            go.transform.SetParent(parent, false);
            go.transform.position = pos;
            var col = go.AddComponent<BoxCollider>();
            col.isTrigger = true;
            col.size = new Vector3(14f, 4f, 1f);
            var rp = new GameObject("RespawnPoint").transform;
            rp.SetParent(go.transform, false);
            rp.localPosition = Vector3.up * 1.5f;
            var cp = go.AddComponent<Checkpoint>();
            BuilderUtils.SetPrivate(cp, "respawnPoint", rp);
            BuilderUtils.SetPrivate(cp, "order", order);
        }

        private static void AddSpawnPoints(Transform parent, Vector3 center)
        {
            var group = new GameObject("SpawnPoints");
            group.transform.SetParent(parent, false);
            for (int i = 0; i < 8; i++)
            {
                int row = i / 4;
                int col = i % 4;
                var sp = new GameObject($"Spawn_{i}");
                sp.transform.SetParent(group.transform, false);
                sp.transform.position = center + new Vector3((col - 1.5f) * 2.5f, 0f, -row * 2.5f);
                sp.tag = GameConstants.TagRespawnPoint;
            }
        }

        private static void BuildBotSpawner(Transform parent)
        {
            BuilderUtils.AttachByName(parent, "BotSpawner", "StumbleClone.Bots.BotSpawner", configure: comp =>
            {
                BuilderUtils.SetPrivate(comp, "mode", LevelMode.Race);
                BuilderUtils.SetPrivate(comp, "botCount", GameConstants.DefaultBotsPerLevel);
            });
        }

        private static void BuildRaceManager(Transform parent)
        {
            BuilderUtils.AttachByName(parent, "RaceManager", "StumbleClone.Game.RaceManager");
        }

        private static Color Hue(int i) => Color.HSVToRGB(((i * 0.137f) % 1f + 1f) % 1f, 0.55f, 0.95f);
    }

    internal static class BuilderUtils
    {
        public static GameObject CreateBox(string name, Transform parent, Vector3 pos, Vector3 size, Color color)
        {
            return CreatePrimitive(PrimitiveType.Cube, name, parent, pos, size, color);
        }

        public static GameObject CreatePrimitive(PrimitiveType type, string name, Transform parent, Vector3 pos, Vector3 size, Color color)
        {
            var go = GameObject.CreatePrimitive(type);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.position = pos;
            go.transform.localScale = size;
            var rend = go.GetComponent<Renderer>();
            if (rend != null)
            {
                var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
                var mat = new Material(shader);
                if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", color);
                else mat.color = color;
                rend.sharedMaterial = mat;
            }
            return go;
        }

        public static void MarkGround(GameObject go)
        {
            // Layer assignment uses GameConstants so the runtime grounded-check stays consistent.
            int layer = StumbleClone.Core.GameConstants.LayerGround;
            if (layer >= 0 && layer < 32) go.layer = layer;
        }

        public static void SetPrivate(object target, string fieldName, object value)
        {
            if (target == null) return;
            var t = target.GetType();
            var f = t.GetField(fieldName, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public);
            if (f != null) { f.SetValue(target, value); return; }
            var p = t.GetProperty(fieldName, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public);
            if (p != null && p.CanWrite) p.SetValue(target, value);
        }

        // Attaches a runtime component by full type name; tolerates missing types so editor builders
        // remain useful even when other coders haven't shipped their managers yet.
        public static Component AttachByName(Transform parent, string goName, string fullTypeName, System.Action<Component> configure = null)
        {
            var holder = new GameObject(goName);
            holder.transform.SetParent(parent, false);
            System.Type t = null;
            foreach (var asm in System.AppDomain.CurrentDomain.GetAssemblies())
            {
                t = asm.GetType(fullTypeName, throwOnError: false);
                if (t != null) break;
            }
            if (t == null)
            {
                Debug.LogWarning($"[BuilderUtils] Type '{fullTypeName}' not found yet — created placeholder GameObject '{goName}'. Add the component manually once that script ships.");
                return null;
            }
            var comp = holder.AddComponent(t);
            configure?.Invoke(comp);
            return comp;
        }
    }
}
#endif
