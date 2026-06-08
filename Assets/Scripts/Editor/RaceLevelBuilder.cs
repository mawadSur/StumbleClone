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

            // CONTINUOUS floor: each section's box starts exactly where the previous ended.
            // Track runs toward -Z. Floor boxes: y-center 0, y-size 1 (top surface at y=0.5).
            // Width 14 throughout. Spawns/checkpoints stay inside solid floor segments so
            // NavMeshAgents (bots) can path the whole way and respawns land on ground.
            BuildStartPlatform(root.transform);                       // spans 0..-20
            AddSpawnPoints(root.transform, new Vector3(0f, 1.2f, -18f));
            AddCheckpoint(root.transform, new Vector3(0f, 0.6f, -15f), 0);

            BuildBarSection(root.transform);                          // spans -20..-50
            AddCheckpoint(root.transform, new Vector3(0f, 0.6f, -46f), 1);

            BuildHammerSection(root.transform);                       // spans -50..-86
            AddCheckpoint(root.transform, new Vector3(0f, 0.6f, -82f), 2);

            BuildRamSection(root.transform);                          // spans -86..-120
            AddCheckpoint(root.transform, new Vector3(0f, 0.6f, -116f), 3);

            Transform finishTransform = BuildFinishPlatform(root.transform); // spans -120..-148
            AddCheckpoint(root.transform, new Vector3(0f, 0.6f, -124f), 4);

            BuildWorldKillZone(root.transform);
            BuildBotSpawner(root.transform, finishTransform);
            BuildRaceManager(root.transform);
            BuilderUtils.AttachByName(root.transform, "LevelSelfStart", "StumbleClone.Game.LevelSelfStart", configure: comp =>
            {
                BuilderUtils.SetPrivate(comp, "mode", LevelMode.Race);
            });

            Selection.activeGameObject = root;
            Debug.Log("[RaceLevelBuilder] Built continuous race track (finish at Z=-146). Next: open Window > AI > Navigation, bake NavMesh, then save scene as Assets/Scenes/Level_Race.unity.");
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

        // StartPlatform: center Z=-10, size (14,1,20) -> spans 0..-20.
        private static void BuildStartPlatform(Transform parent)
        {
            var plat = BuilderUtils.CreateBox("StartPlatform", parent, new Vector3(0f, 0f, -10f), new Vector3(14f, 1f, 20f), Hue(0));
            BuilderUtils.MarkGround(plat);
        }

        // BarSection floor: center Z=-35, size (14,1,30) -> spans -20..-50.
        private static void BuildBarSection(Transform parent)
        {
            var floor = BuilderUtils.CreateBox("Section1_Floor", parent, new Vector3(0f, 0f, -35f), new Vector3(14f, 1f, 30f), Hue(1));
            BuilderUtils.MarkGround(floor);

            float[] zs = { -30f, -42f };
            for (int i = 0; i < zs.Length; i++)
            {
                float z = zs[i];
                BuilderUtils.CreateBox($"BarPost_{i}", parent, new Vector3(0f, 1.5f, z), new Vector3(0.4f, 2f, 0.4f), Color.gray);
                var barRoot = new GameObject($"SpinningBar_{i}");
                barRoot.transform.SetParent(parent, false);
                barRoot.transform.position = new Vector3(0f, 1.5f, z);
                barRoot.AddComponent<SpinningBar>();
                var bar = BuilderUtils.CreatePrimitive(PrimitiveType.Cylinder, "BarBody", barRoot.transform, Vector3.zero, new Vector3(0.6f, 5f, 0.6f), Hue(1));
                bar.transform.localRotation = Quaternion.Euler(0f, 0f, 90f);
                bar.AddComponent<SpinningBarChild>();
            }
        }

        // HammerSection floor: center Z=-68, size (14,1,36) -> spans -50..-86.
        private static void BuildHammerSection(Transform parent)
        {
            var floor = BuilderUtils.CreateBox("Section2_Floor", parent, new Vector3(0f, 0f, -68f), new Vector3(14f, 1f, 36f), Hue(2));
            BuilderUtils.MarkGround(floor);

            float[] zs = { -58f, -68f, -78f };
            for (int i = 0; i < zs.Length; i++)
            {
                float z = zs[i];
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
        }

        // RamSection floor (replaces the old floorless moving-platform pit):
        // center Z=-103, size (14,1,34) -> spans -86..-120. Solid floor so bots can path it.
        // 3 SlidingRam hazards shove racers toward the side edges; alternating push side +
        // staggered Z forces the player to weave. SlidingRam.Configure is never called here
        // (that's an arena-spawner hook), so _travelDir stays zero and the ram does NOT self-move
        // or self-rotate. We orient transform.forward toward a side edge; on contact the base
        // ArenaObstacle.TryPush falls back to transform.forward, giving the sideways shove.
        private static void BuildRamSection(Transform parent)
        {
            var floor = BuilderUtils.CreateBox("Section3_Floor", parent, new Vector3(0f, 0f, -103f), new Vector3(14f, 1f, 34f), Hue(3));
            BuilderUtils.MarkGround(floor);

            float[] zs = { -92f, -103f, -114f };
            for (int i = 0; i < zs.Length; i++)
            {
                float z = zs[i];
                // Alternate which side the ram starts on and which way it shoves.
                bool fromLeft = (i % 2) == 0;
                float startX = fromLeft ? -5f : 5f;
                Vector3 pushDir = fromLeft ? Vector3.right : Vector3.left; // toward the opposite side edge

                var ram = new GameObject($"SlidingRam_{i}");
                ram.transform.SetParent(parent, false);
                ram.transform.position = new Vector3(startX, 1f, z);
                ram.transform.rotation = Quaternion.LookRotation(pushDir, Vector3.up);

                var slide = ram.AddComponent<SlidingRam>();
                // Race track is continuous and persistent: keep the ram alive for the whole run
                // and give it a strong, snappy sideways shove. (No travel points exist on this
                // component — it self-computes from arenaCenter only when an arena spawner calls
                // Configure, which never happens on the Race track.)
                BuilderUtils.SetPrivate(slide, "lifetime", 9999f);
                BuilderUtils.SetPrivate(slide, "pushForce", 14f + i * 2f); // phase/intensity stagger
                BuilderUtils.SetPrivate(slide, "pushCooldown", 0.2f);

                var body = BuilderUtils.CreatePrimitive(PrimitiveType.Cube, "RamBody", ram.transform, Vector3.zero, new Vector3(2.5f, 2f, 2.5f), Hue(3));
                // The visible cube must not carry its own collider — the ram's BoxCollider on the
                // root does the pushing; a child collider would block the runner instead of shoving.
                var childCol = body.GetComponent<Collider>();
                if (childCol != null) UnityEngine.Object.DestroyImmediate(childCol);
            }
        }

        // FinishPlatform: center Z=-134, size (14,1,28) -> spans -120..-148.
        // Finish line ON the floor at Z=-146 (over the FinishPlatform), reachable by a runner.
        // Returns the finish GameObject's transform so BotSpawner can target it.
        private static Transform BuildFinishPlatform(Transform parent)
        {
            var plat = BuilderUtils.CreateBox("FinishPlatform", parent, new Vector3(0f, 0f, -134f), new Vector3(14f, 1f, 28f), Hue(4));
            BuilderUtils.MarkGround(plat);

            var finishVisual = BuilderUtils.CreateBox("FinishVisual", parent, new Vector3(0f, 2.5f, -146f), new Vector3(14f, 5f, 0.3f), Color.yellow);
            var col = finishVisual.GetComponent<Collider>();
            if (col != null) col.isTrigger = true;
            finishVisual.AddComponent<FinishLine>();
            finishVisual.tag = GameConstants.TagFinish;
            return finishVisual.transform;
        }

        private static void BuildWorldKillZone(Transform parent)
        {
            // Falling off the SIDE of the 14-wide track drops racers into this big trigger.
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
                // Orient toward the finish: -Z is "forward" down the track.
                sp.transform.rotation = Quaternion.LookRotation(Vector3.back, Vector3.up);
                sp.tag = GameConstants.TagRespawnPoint;
            }
        }

        private static void BuildBotSpawner(Transform parent, Transform finishTransform)
        {
            BuilderUtils.AttachByName(parent, "BotSpawner", "StumbleClone.Bots.BotSpawner", configure: comp =>
            {
                BuilderUtils.SetPrivate(comp, "mode", LevelMode.Race);
                BuilderUtils.SetPrivate(comp, "botCount", GameConstants.DefaultBotsPerLevel);
                BuilderUtils.SetPrivate(comp, "spawnPointOffset", 1); // player owns spawn point 0
                // Wire the bot goal AND recovery anchor (recovery falls back to finishLine).
                BuilderUtils.SetPrivate(comp, "finishLine", finishTransform);
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

        /// Replace a primitive's auto-added collider with a MeshCollider that follows the mesh.
        /// REQUIRED for Cylinder grounds: Unity's Cylinder primitive ships a CapsuleCollider, and
        /// scaling it flat-and-wide (arena discs) degenerates it into a giant sphere the player
        /// spawns inside — PhysX then flings the player off the map.
        public static void UseMeshGroundCollider(GameObject go)
        {
            var mf = go.GetComponent<MeshFilter>();
            if (mf == null || mf.sharedMesh == null) return;
            var existing = go.GetComponent<Collider>();
            if (existing != null) UnityEngine.Object.DestroyImmediate(existing);
            var mc = go.AddComponent<MeshCollider>();
            mc.sharedMesh = mf.sharedMesh;
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
