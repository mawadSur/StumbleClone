#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TMPro;
using StumbleClone.Bots;
using StumbleClone.CameraRig;
using StumbleClone.Core;
using StumbleClone.Game;
using StumbleClone.Player;
using StumbleClone.UI;
using Unity.AI.Navigation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

namespace StumbleClone.EditorTools
{
    /// One-click MVP bootstrap. Materializes the tags/layers, materials, prefabs,
    /// level scenes (with NavMesh baked), MainMenu scene, and build-settings list
    /// so the user can press Play immediately after opening the project.
    public static class MvpBootstrap
    {
        // ---- Paths (everything under Assets/) ---------------------------------
        private const string MaterialsDir = "Assets/Materials";
        private const string PrefabsDir   = "Assets/Prefabs";
        private const string ScenesDir    = "Assets/Scenes";

        private const string MatPlayerPath   = "Assets/Materials/M_Player.mat";
        private const string MatBotPath      = "Assets/Materials/M_Bot.mat";
        private const string MatGroundPath   = "Assets/Materials/M_Ground.mat";
        private const string MatObstaclePath = "Assets/Materials/M_Obstacle.mat";

        private const string PlayerPrefabPath = "Assets/Prefabs/Player.prefab";
        private const string BotPrefabPath    = "Assets/Prefabs/Bot.prefab";

        private const string MainMenuScenePath     = "Assets/Scenes/MainMenu.unity";
        private const string RaceScenePath         = "Assets/Scenes/Level_Race.unity";
        private const string SurvivalScenePath     = "Assets/Scenes/Level_Survival.unity";
        private const string LastStandingScenePath = "Assets/Scenes/Level_LastStanding.unity";

        private const string InputActionsPath = "Assets/InputActions/PlayerInputActions.inputactions";

        // ---- Public entry -----------------------------------------------------

        [MenuItem("StumbleClone/Bootstrap MVP")]
        public static void Run()
        {
            int stepIndex = 0;
            try
            {
                LogStep(++stepIndex, "Configure Tags & Layers");
                ConfigureTagsAndLayers();

                LogStep(++stepIndex, "Ensure asset folders exist");
                EnsureFolders();

                LogStep(++stepIndex, "Build URP materials");
                BuildMaterials();

                LogStep(++stepIndex, "Build Player.prefab");
                BuildPlayerPrefab();

                LogStep(++stepIndex, "Build Bot.prefab");
                BuildBotPrefab();

                LogStep(++stepIndex, "Build Race level scene");
                BuildLevelScene(LevelMode.Race, RaceScenePath, RaceLevelBuilder.Build, "RaceLevel");

                LogStep(++stepIndex, "Build Survival level scene");
                BuildLevelScene(LevelMode.Survival, SurvivalScenePath, SurvivalLevelBuilder.Build, "SurvivalLevel");

                LogStep(++stepIndex, "Build LastStanding level scene");
                BuildLevelScene(LevelMode.LastStanding, LastStandingScenePath, LastStandLevelBuilder.Build, "LastStandLevel");

                LogStep(++stepIndex, "Build MainMenu scene");
                BuildMainMenuScene();

                LogStep(++stepIndex, "Update EditorBuildSettings");
                ConfigureBuildSettings();

                LogStep(++stepIndex, "Save and refresh AssetDatabase");
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();

                if (!Application.isBatchMode)
                {
                    EditorSceneManager.OpenScene(MainMenuScenePath, OpenSceneMode.Single);
                }

                Debug.Log("[Bootstrap] Done.");
                if (Application.isBatchMode) EditorApplication.Exit(0);
            }
            catch (Exception e)
            {
                Debug.LogError($"[Bootstrap] FAILED at step {stepIndex}: {e}");
                if (Application.isBatchMode) EditorApplication.Exit(1);
                else throw;
            }
        }

        /// Rebuilds ONLY the scenes (+ tags/layers/materials/build-settings) and re-wires them
        /// to the EXISTING Player/Bot prefabs — it does NOT rebuild the prefabs, so imported FBX
        /// character variants are preserved (Run() rebuilds them as capsules). Use this to
        /// materialize level-builder changes — spawn points, KillZone mode, LevelSelfStart,
        /// bot offset, NavMesh bake — without regressing the characters.
        ///
        /// Headless (GUI closed):
        ///   Unity.exe -batchmode -projectPath "&lt;win&gt;" \
        ///     -executeMethod StumbleClone.EditorTools.MvpBootstrap.RebuildScenesOnly -logFile &lt;log&gt;
        [MenuItem("StumbleClone/Rebuild Scenes Only (keep prefabs)")]
        public static void RebuildScenesOnly()
        {
            int stepIndex = 0;
            try
            {
                LogStep(++stepIndex, "Configure Tags & Layers");
                ConfigureTagsAndLayers();

                LogStep(++stepIndex, "Ensure asset folders exist");
                EnsureFolders();

                LogStep(++stepIndex, "Build URP materials");
                BuildMaterials();

                LogStep(++stepIndex, "Skipping prefab build — preserving existing FBX character prefabs");

                LogStep(++stepIndex, "Build Race level scene");
                BuildLevelScene(LevelMode.Race, RaceScenePath, RaceLevelBuilder.Build, "RaceLevel");

                LogStep(++stepIndex, "Build Survival level scene");
                BuildLevelScene(LevelMode.Survival, SurvivalScenePath, SurvivalLevelBuilder.Build, "SurvivalLevel");

                LogStep(++stepIndex, "Build LastStanding level scene");
                BuildLevelScene(LevelMode.LastStanding, LastStandingScenePath, LastStandLevelBuilder.Build, "LastStandLevel");

                LogStep(++stepIndex, "Build MainMenu scene");
                BuildMainMenuScene();

                LogStep(++stepIndex, "Update EditorBuildSettings");
                ConfigureBuildSettings();

                LogStep(++stepIndex, "Save and refresh AssetDatabase");
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();

                if (!Application.isBatchMode)
                    EditorSceneManager.OpenScene(LastStandingScenePath, OpenSceneMode.Single);

                Debug.Log("[Bootstrap] Rebuild Scenes Only: Done.");
                if (Application.isBatchMode) EditorApplication.Exit(0);
            }
            catch (Exception e)
            {
                Debug.LogError($"[Bootstrap] Rebuild Scenes Only FAILED at step {stepIndex}: {e}");
                if (Application.isBatchMode) EditorApplication.Exit(1);
                else throw;
            }
        }

        private static void LogStep(int n, string what) => Debug.Log($"[Bootstrap] step {n}: {what}");

        // =======================================================================
        // Step A — Tags & Layers
        // =======================================================================

        private static void ConfigureTagsAndLayers()
        {
            var tagAssets = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset");
            if (tagAssets == null || tagAssets.Length == 0)
            {
                throw new InvalidOperationException("TagManager.asset could not be loaded.");
            }

            var so = new SerializedObject(tagAssets[0]);

            string[] requiredTags =
            {
                GameConstants.TagPlayer,
                GameConstants.TagBot,
                GameConstants.TagFinish,
                GameConstants.TagKillzone,
                GameConstants.TagPushPad,
                GameConstants.TagRespawnPoint,
            };

            var tagsProp = so.FindProperty("tags");
            foreach (var t in requiredTags)
            {
                if (!TagExists(tagsProp, t))
                {
                    tagsProp.InsertArrayElementAtIndex(tagsProp.arraySize);
                    var elem = tagsProp.GetArrayElementAtIndex(tagsProp.arraySize - 1);
                    elem.stringValue = t;
                }
            }

            var layersProp = so.FindProperty("layers");
            // Slots 0..7 are reserved/built-in; only write 8..12 if currently empty.
            SetLayerIfEmpty(layersProp, GameConstants.LayerPlayer,   "Player");
            SetLayerIfEmpty(layersProp, GameConstants.LayerBot,      "Bot");
            SetLayerIfEmpty(layersProp, GameConstants.LayerObstacle, "Obstacle");
            SetLayerIfEmpty(layersProp, GameConstants.LayerGround,   "Ground");
            SetLayerIfEmpty(layersProp, GameConstants.LayerKillzone, "Killzone");

            so.ApplyModifiedPropertiesWithoutUndo();
            AssetDatabase.SaveAssets();
        }

        private static bool TagExists(SerializedProperty tagsProp, string tag)
        {
            for (int i = 0; i < tagsProp.arraySize; i++)
            {
                if (tagsProp.GetArrayElementAtIndex(i).stringValue == tag) return true;
            }
            return false;
        }

        private static void SetLayerIfEmpty(SerializedProperty layersProp, int index, string name)
        {
            if (index < 0 || index >= layersProp.arraySize) return;
            var slot = layersProp.GetArrayElementAtIndex(index);
            if (string.IsNullOrEmpty(slot.stringValue))
            {
                slot.stringValue = name;
            }
        }

        // =======================================================================
        // Helper — folders
        // =======================================================================

        private static void EnsureFolders()
        {
            EnsureFolder("Assets/Materials");
            EnsureFolder("Assets/Prefabs");
            EnsureFolder("Assets/Scenes");
            EnsureFolder("Assets/InputActions");
        }

        private static void EnsureFolder(string assetPath)
        {
            if (AssetDatabase.IsValidFolder(assetPath)) return;
            string parent = Path.GetDirectoryName(assetPath).Replace('\\', '/');
            string leaf = Path.GetFileName(assetPath);
            if (!AssetDatabase.IsValidFolder(parent)) EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, leaf);
        }

        // =======================================================================
        // Step B — Materials
        // =======================================================================

        private static void BuildMaterials()
        {
            CreateOrUpdateMaterial(MatPlayerPath,   new Color(0.20f, 0.45f, 0.95f));
            CreateOrUpdateMaterial(MatBotPath,      new Color(0.90f, 0.25f, 0.25f));
            CreateOrUpdateMaterial(MatGroundPath,   new Color(0.55f, 0.55f, 0.55f));
            CreateOrUpdateMaterial(MatObstaclePath, new Color(0.95f, 0.80f, 0.20f));
        }

        private static Material CreateOrUpdateMaterial(string path, Color color)
        {
            var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (existing != null)
            {
                existing.shader = shader;
                SetColor(existing, color);
                EditorUtility.SetDirty(existing);
                return existing;
            }
            var mat = new Material(shader);
            SetColor(mat, color);
            AssetDatabase.CreateAsset(mat, path);
            return mat;
        }

        private static void SetColor(Material mat, Color color)
        {
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", color);
            else if (mat.HasProperty("_Color")) mat.SetColor("_Color", color);
            else mat.color = color;
        }

        // =======================================================================
        // Step C — Player.prefab
        // =======================================================================

        private static void BuildPlayerPrefab()
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            try
            {
                go.name = "Player";
                go.tag = GameConstants.TagPlayer;
                go.layer = GameConstants.LayerPlayer;

                // The default capsule primitive is 2m tall with pivot at center.
                // Move it so the pivot sits at the feet (Y=0 = ground).
                // We do this by parenting the visual mesh; simpler: leave pivot
                // at center and rely on CapsuleCollider center already at 0.
                // The capsule primitive's CapsuleCollider has Height=2, Radius=0.5
                // with center at (0,0,0). Player code expects pivot near feet, so
                // shift the mesh up by 1 and set collider center to (0,1,0).
                var meshFilter = go.GetComponent<MeshFilter>();
                var renderer = go.GetComponent<MeshRenderer>();
                if (meshFilter != null && renderer != null)
                {
                    // Move the visual capsule up so its base aligns with pivot.
                    // We do this by reparenting: move the mesh to a child, then
                    // strip mesh components from the root.
                    var visual = new GameObject("Visual");
                    visual.transform.SetParent(go.transform, false);
                    visual.transform.localPosition = new Vector3(0f, 1f, 0f);
                    var vmf = visual.AddComponent<MeshFilter>();
                    var vmr = visual.AddComponent<MeshRenderer>();
                    vmf.sharedMesh = meshFilter.sharedMesh;
                    var mat = AssetDatabase.LoadAssetAtPath<Material>(MatPlayerPath);
                    if (mat != null) vmr.sharedMaterial = mat;
                    UnityEngine.Object.DestroyImmediate(renderer);
                    UnityEngine.Object.DestroyImmediate(meshFilter);
                }

                var capsule = go.GetComponent<CapsuleCollider>();
                if (capsule != null)
                {
                    capsule.height = 2f;
                    capsule.radius = 0.5f;
                    capsule.center = new Vector3(0f, 1f, 0f);
                }

                var rb = go.AddComponent<Rigidbody>();
                rb.mass = 1f;
                rb.useGravity = true;
                rb.isKinematic = false;
                rb.interpolation = RigidbodyInterpolation.Interpolate;
                rb.collisionDetectionMode = CollisionDetectionMode.Continuous;
                rb.constraints = RigidbodyConstraints.FreezeRotation;

                // Input System wiring
                var inputActions = AssetDatabase.LoadAssetAtPath<InputActionAsset>(InputActionsPath);
                if (inputActions == null)
                {
                    Debug.LogWarning($"[Bootstrap] InputActionAsset not found at {InputActionsPath} — PlayerInput will be added with no actions.");
                }
                var playerInput = go.AddComponent<PlayerInput>();
                if (inputActions != null)
                {
                    playerInput.actions = inputActions;
                }
                playerInput.defaultActionMap = "Gameplay";
                playerInput.notificationBehavior = PlayerNotifications.SendMessages;

                go.AddComponent<PlayerInputHandler>();
                var playerCtrl = go.AddComponent<PlayerController>();
                SetSerializedField(playerCtrl, "racerId", 0);
                SetSerializedField(playerCtrl, "displayName", "Player");

                go.AddComponent<PushInteraction>();
                go.AddComponent<PlayerAnimator>();

                // Save prefab.
                PrefabUtility.SaveAsPrefabAsset(go, PlayerPrefabPath);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(go);
            }
        }

        // =======================================================================
        // Step D — Bot.prefab
        // =======================================================================

        private static void BuildBotPrefab()
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            try
            {
                go.name = "Bot";
                go.tag = GameConstants.TagBot;
                go.layer = GameConstants.LayerBot;

                var meshFilter = go.GetComponent<MeshFilter>();
                var renderer = go.GetComponent<MeshRenderer>();
                if (meshFilter != null && renderer != null)
                {
                    var visual = new GameObject("Visual");
                    visual.transform.SetParent(go.transform, false);
                    visual.transform.localPosition = new Vector3(0f, 1f, 0f);
                    var vmf = visual.AddComponent<MeshFilter>();
                    var vmr = visual.AddComponent<MeshRenderer>();
                    vmf.sharedMesh = meshFilter.sharedMesh;
                    var mat = AssetDatabase.LoadAssetAtPath<Material>(MatBotPath);
                    if (mat != null) vmr.sharedMaterial = mat;
                    UnityEngine.Object.DestroyImmediate(renderer);
                    UnityEngine.Object.DestroyImmediate(meshFilter);
                }

                var capsule = go.GetComponent<CapsuleCollider>();
                if (capsule != null)
                {
                    capsule.height = 2f;
                    capsule.radius = 0.5f;
                    capsule.center = new Vector3(0f, 1f, 0f);
                }

                var rb = go.AddComponent<Rigidbody>();
                rb.mass = 1f;
                rb.useGravity = true;
                rb.isKinematic = true; // BotController flips this during knockback
                rb.interpolation = RigidbodyInterpolation.Interpolate;
                rb.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
                rb.constraints = RigidbodyConstraints.FreezeRotation;

                var agent = go.AddComponent<NavMeshAgent>();
                agent.radius = 0.5f;
                agent.height = 2f;
                agent.speed = 6f;
                agent.acceleration = 12f;
                agent.angularSpeed = 240f;
                agent.baseOffset = 0f;

                go.AddComponent<BotController>();

                PrefabUtility.SaveAsPrefabAsset(go, BotPrefabPath);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(go);
            }
        }

        // =======================================================================
        // Step E — Level scenes
        // =======================================================================

        private static void BuildLevelScene(LevelMode mode, string scenePath, Action builderFn, string rootName)
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);

            // Invoke the level builder which populates the active scene.
            builderFn();

            var groundMat = AssetDatabase.LoadAssetAtPath<Material>(MatGroundPath);
            var obstacleMat = AssetDatabase.LoadAssetAtPath<Material>(MatObstaclePath);

            var roots = scene.GetRootGameObjects();
            GameObject levelRoot = roots.FirstOrDefault(r => r.name == rootName) ?? roots.FirstOrDefault(r => r.name.EndsWith("Level"));

            // Apply ground material to anything on the Ground layer
            if (groundMat != null)
            {
                foreach (var rend in UnityEngine.Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None))
                {
                    if (rend.gameObject.layer == GameConstants.LayerGround)
                    {
                        rend.sharedMaterial = groundMat;
                    }
                }
            }

            // Wire BotSpawner: botPrefab + spawnPoints
            var spawner = UnityEngine.Object.FindFirstObjectByType<BotSpawner>();
            if (spawner != null)
            {
                var botPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(BotPrefabPath);
                var spawnPoints = CollectSpawnPoints(levelRoot);
                var soSp = new SerializedObject(spawner);
                soSp.FindProperty("botPrefab").objectReferenceValue = botPrefab;
                AssignTransformArray(soSp.FindProperty("spawnPoints"), spawnPoints);

                // Mode-specific references that the spawner reads to build behaviors.
                Transform finishLine = FindByName(levelRoot, "FinishVisual")?.transform;
                Transform safeAnchor = FindByName(levelRoot, "SafeZoneCenter")?.transform;
                Transform arenaCenter = FindByName(levelRoot, "Arena")?.transform;

                if (finishLine != null) soSp.FindProperty("finishLine").objectReferenceValue = finishLine;
                if (safeAnchor != null) soSp.FindProperty("safeAnchor").objectReferenceValue = safeAnchor;
                if (arenaCenter != null) soSp.FindProperty("arenaCenter").objectReferenceValue = arenaCenter;
                if (mode == LevelMode.LastStanding) soSp.FindProperty("arenaRadius").floatValue = 18f;
                soSp.ApplyModifiedPropertiesWithoutUndo();
            }

            // Wire mode managers
            switch (mode)
            {
                case LevelMode.Race:
                {
                    var rm = UnityEngine.Object.FindFirstObjectByType<RaceManager>();
                    var finishLine = FindByName(levelRoot, "FinishVisual");
                    if (rm != null && finishLine != null)
                    {
                        var soRm = new SerializedObject(rm);
                        soRm.FindProperty("finishLine").objectReferenceValue = finishLine.transform;
                        soRm.ApplyModifiedPropertiesWithoutUndo();
                    }
                    break;
                }
                case LevelMode.Survival:
                {
                    var sm = UnityEngine.Object.FindFirstObjectByType<SurvivalManager>();
                    var safe = FindByName(levelRoot, "SafeZoneCenter");
                    if (sm != null && safe != null)
                    {
                        var soSm = new SerializedObject(sm);
                        soSm.FindProperty("safeZoneCenter").objectReferenceValue = safe.transform;
                        soSm.ApplyModifiedPropertiesWithoutUndo();
                    }
                    break;
                }
                case LevelMode.LastStanding:
                {
                    var lm = UnityEngine.Object.FindFirstObjectByType<LastStandingManager>();
                    var arena = FindByName(levelRoot, "Arena");
                    if (lm != null && arena != null)
                    {
                        var soLm = new SerializedObject(lm);
                        soLm.FindProperty("arenaCenter").objectReferenceValue = arena.transform;
                        soLm.ApplyModifiedPropertiesWithoutUndo();
                    }
                    break;
                }
            }

            // Spawn the Player at the first spawn point.
            Vector3 playerPos = new Vector3(0f, 2f, 0f);
            var spawnGroup = FindByName(levelRoot, "SpawnPoints");
            if (spawnGroup != null && spawnGroup.transform.childCount > 0)
            {
                playerPos = spawnGroup.transform.GetChild(0).position;
            }

            var playerPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPrefabPath);
            GameObject playerInstance = null;
            if (playerPrefab != null)
            {
                playerInstance = (GameObject)PrefabUtility.InstantiatePrefab(playerPrefab);
                playerInstance.name = "Player";
                playerInstance.transform.position = playerPos;
            }

            // Set up the Main Camera with ThirdPersonCamera.
            var camGo = GameObject.FindGameObjectWithTag("MainCamera");
            if (camGo == null)
            {
                camGo = new GameObject("Main Camera");
                camGo.tag = "MainCamera";
                camGo.AddComponent<Camera>();
                camGo.AddComponent<AudioListener>();
            }
            var tpc = camGo.GetComponent<ThirdPersonCamera>();
            if (tpc == null) tpc = camGo.AddComponent<ThirdPersonCamera>();
            if (playerInstance != null)
            {
                var soTpc = new SerializedObject(tpc);
                soTpc.FindProperty("target").objectReferenceValue = playerInstance.transform;
                soTpc.ApplyModifiedPropertiesWithoutUndo();
            }

            // Bake NavMesh on every ground surface.
            BakeNavMeshSurfaces();

            // Build the in-level HUD, pause overlay, and end screen (+ EventSystem).
            BuildLevelHud(mode);

            // Save the scene.
            EnsureFolder("Assets/Scenes");
            EditorSceneManager.SaveScene(scene, scenePath);
        }

        // =======================================================================
        // Step E2 — In-level HUD / pause / end screen
        // =======================================================================

        private static void BuildLevelHud(LevelMode mode)
        {
            // EventSystem (New Input System UI module) so pause/end-screen buttons receive clicks.
            if (UnityEngine.Object.FindFirstObjectByType<EventSystem>() == null)
            {
                var esGo = new GameObject("EventSystem");
                esGo.AddComponent<EventSystem>();
                esGo.AddComponent<InputSystemUIInputModule>();
            }

            var canvasGo = new GameObject("HUDCanvas");
            var canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = canvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;
            canvasGo.AddComponent<GraphicRaycaster>();

            var topLeft = new Vector2(0f, 1f);
            var topCenter = new Vector2(0.5f, 1f);
            var topRight = new Vector2(1f, 1f);

            switch (mode)
            {
                case LevelMode.Race:
                {
                    var rank = CreateHudText(canvasGo.transform, "RankText", "1 / 8",
                        topLeft, topLeft, topLeft, new Vector2(180f, -50f), new Vector2(300f, 70f), 40, TextAlignmentOptions.Left);
                    var timer = CreateHudText(canvasGo.transform, "TimerText", "00:00",
                        topCenter, topCenter, topCenter, new Vector2(0f, -50f), new Vector2(320f, 70f), 40, TextAlignmentOptions.Center);
                    var hud = canvasGo.AddComponent<RaceHUD>();
                    var so = new SerializedObject(hud);
                    so.FindProperty("rankText").objectReferenceValue = rank;
                    so.FindProperty("timerText").objectReferenceValue = timer;
                    so.ApplyModifiedPropertiesWithoutUndo();
                    break;
                }
                case LevelMode.Survival:
                {
                    var alive = CreateHudText(canvasGo.transform, "AliveText", "Alive: 8",
                        topLeft, topLeft, topLeft, new Vector2(180f, -50f), new Vector2(320f, 70f), 40, TextAlignmentOptions.Left);
                    var timer = CreateHudText(canvasGo.transform, "TimerText", "00:00",
                        topCenter, topCenter, topCenter, new Vector2(0f, -50f), new Vector2(320f, 70f), 40, TextAlignmentOptions.Center);
                    var hud = canvasGo.AddComponent<SurvivalHUD>();
                    var so = new SerializedObject(hud);
                    so.FindProperty("aliveCountText").objectReferenceValue = alive;
                    so.FindProperty("timerText").objectReferenceValue = timer;
                    so.ApplyModifiedPropertiesWithoutUndo();
                    break;
                }
                case LevelMode.LastStanding:
                {
                    var alive = CreateHudText(canvasGo.transform, "AliveText", "Alive: 8",
                        topLeft, topLeft, topLeft, new Vector2(180f, -50f), new Vector2(320f, 70f), 40, TextAlignmentOptions.Left);
                    var indicator = CreateImage(canvasGo.transform, "ZoneIndicator", new Color(0.2f, 0.9f, 0.2f, 0.9f),
                        topRight, topRight, topRight, new Vector2(-60f, -60f), new Vector2(64f, 64f));
                    var hud = canvasGo.AddComponent<LastStandHUD>();
                    var so = new SerializedObject(hud);
                    so.FindProperty("aliveCountText").objectReferenceValue = alive;
                    so.FindProperty("zoneIndicator").objectReferenceValue = indicator;
                    so.ApplyModifiedPropertiesWithoutUndo();
                    break;
                }
            }

            BuildPauseOverlay(canvasGo.transform);
            BuildEndScreen(canvasGo.transform);
        }

        private static void BuildPauseOverlay(Transform canvas)
        {
            var panel = CreateFullscreenPanel(canvas, "PausePanel", new Color(0f, 0f, 0f, 0.75f));
            var title = CreateHudText(panel.transform, "PauseTitle", "PAUSED",
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, 180f), new Vector2(600f, 120f), 72, TextAlignmentOptions.Center);
            title.fontStyle = FontStyles.Bold;
            var resume = CreateButton(panel.transform, "ResumeButton", "Resume", new Vector2(0f, 40f), new Vector2(360f, 80f));
            var menu = CreateButton(panel.transform, "MenuButton", "Main Menu", new Vector2(0f, -60f), new Vector2(360f, 80f));
            var quit = CreateButton(panel.transform, "QuitButton", "Quit", new Vector2(0f, -160f), new Vector2(360f, 80f));

            var pauseGo = new GameObject("PauseMenu");
            pauseGo.transform.SetParent(canvas, false);
            var pause = pauseGo.AddComponent<PauseMenuUI>();
            var so = new SerializedObject(pause);
            so.FindProperty("panel").objectReferenceValue = panel;
            so.FindProperty("resumeButton").objectReferenceValue = resume;
            so.FindProperty("menuButton").objectReferenceValue = menu;
            so.FindProperty("quitButton").objectReferenceValue = quit;
            so.ApplyModifiedPropertiesWithoutUndo();

            panel.SetActive(false);
        }

        private static void BuildEndScreen(Transform canvas)
        {
            var panel = CreateFullscreenPanel(canvas, "EndPanel", new Color(0f, 0f, 0f, 0.8f));
            var result = CreateHudText(panel.transform, "ResultText", "RESULT",
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, 200f), new Vector2(1000f, 150f), 80, TextAlignmentOptions.Center);
            result.fontStyle = FontStyles.Bold;
            var stats = CreateHudText(panel.transform, "StatsText", string.Empty,
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, 90f), new Vector2(800f, 80f), 40, TextAlignmentOptions.Center);
            var cont = CreateButton(panel.transform, "ContinueButton", "Continue", new Vector2(0f, -40f), new Vector2(360f, 80f));
            var menu = CreateButton(panel.transform, "EndMenuButton", "Main Menu", new Vector2(0f, -150f), new Vector2(360f, 80f));

            var endGo = new GameObject("EndScreen");
            endGo.transform.SetParent(canvas, false);
            var end = endGo.AddComponent<EndScreenUI>();
            var so = new SerializedObject(end);
            so.FindProperty("panel").objectReferenceValue = panel;
            so.FindProperty("resultText").objectReferenceValue = result;
            so.FindProperty("statsText").objectReferenceValue = stats;
            so.FindProperty("continueButton").objectReferenceValue = cont;
            so.FindProperty("menuButton").objectReferenceValue = menu;
            so.ApplyModifiedPropertiesWithoutUndo();

            panel.SetActive(false);
        }

        private static TextMeshProUGUI CreateHudText(Transform parent, string name, string content,
            Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot, Vector2 anchoredPos, Vector2 size, int fontSize, TextAlignmentOptions align)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = anchorMin;
            rt.anchorMax = anchorMax;
            rt.pivot = pivot;
            rt.anchoredPosition = anchoredPos;
            rt.sizeDelta = size;
            var tmp = go.AddComponent<TextMeshProUGUI>();
            tmp.text = content;
            tmp.fontSize = fontSize;
            tmp.color = Color.white;
            tmp.alignment = align;
            return tmp;
        }

        private static Image CreateImage(Transform parent, string name, Color color,
            Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot, Vector2 anchoredPos, Vector2 size)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = anchorMin;
            rt.anchorMax = anchorMax;
            rt.pivot = pivot;
            rt.anchoredPosition = anchoredPos;
            rt.sizeDelta = size;
            var img = go.GetComponent<Image>();
            img.color = color;
            return img;
        }

        private static GameObject CreateFullscreenPanel(Transform parent, string name, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            var img = go.GetComponent<Image>();
            img.color = color;
            return go;
        }

        private static Transform[] CollectSpawnPoints(GameObject levelRoot)
        {
            var group = FindByName(levelRoot, "SpawnPoints");
            if (group == null) return Array.Empty<Transform>();
            var list = new List<Transform>(group.transform.childCount);
            foreach (Transform c in group.transform) list.Add(c);
            return list.ToArray();
        }

        private static GameObject FindByName(GameObject root, string name)
        {
            if (root == null)
            {
                // Fall back to global search
                var all = UnityEngine.Object.FindObjectsByType<Transform>(FindObjectsSortMode.None);
                foreach (var t in all) if (t.name == name) return t.gameObject;
                return null;
            }
            return FindByNameRecursive(root.transform, name);
        }

        private static GameObject FindByNameRecursive(Transform t, string name)
        {
            if (t.name == name) return t.gameObject;
            for (int i = 0; i < t.childCount; i++)
            {
                var hit = FindByNameRecursive(t.GetChild(i), name);
                if (hit != null) return hit;
            }
            return null;
        }

        private static void AssignTransformArray(SerializedProperty arrayProp, Transform[] values)
        {
            arrayProp.ClearArray();
            for (int i = 0; i < values.Length; i++)
            {
                arrayProp.InsertArrayElementAtIndex(i);
                arrayProp.GetArrayElementAtIndex(i).objectReferenceValue = values[i];
            }
        }

        private static void BakeNavMeshSurfaces()
        {
            // Find every Renderer on the Ground layer; attach a NavMeshSurface
            // to its GameObject (or its top-level parent) and bake.
            var groundRenderers = UnityEngine.Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None)
                .Where(r => r.gameObject.layer == GameConstants.LayerGround)
                .ToArray();

            var surfaceHosts = new HashSet<GameObject>();
            foreach (var r in groundRenderers)
            {
                surfaceHosts.Add(r.gameObject);
            }

            // If nothing on the Ground layer, fall back to anything named "Arena" or containing "Ground"/"Platform".
            if (surfaceHosts.Count == 0)
            {
                foreach (var t in UnityEngine.Object.FindObjectsByType<Transform>(FindObjectsSortMode.None))
                {
                    string n = t.name;
                    if (n == "Arena" || n.StartsWith("Ground") || n.Contains("Platform") || n.Contains("Floor"))
                    {
                        if (t.GetComponent<Renderer>() != null) surfaceHosts.Add(t.gameObject);
                    }
                }
            }

            foreach (var host in surfaceHosts)
            {
                var surface = host.GetComponent<NavMeshSurface>();
                if (surface == null) surface = host.AddComponent<NavMeshSurface>();
                surface.collectObjects = CollectObjects.All;
                surface.useGeometry = NavMeshCollectGeometry.PhysicsColliders;
                surface.layerMask = ~0;
                try
                {
                    surface.BuildNavMesh();
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"[Bootstrap] NavMesh bake failed on {host.name}: {e.Message}");
                }
            }
        }

        // =======================================================================
        // Step F — MainMenu scene
        // =======================================================================

        private static void BuildMainMenuScene()
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);

            // _Bootstrap GameObject with GameManager + LevelManager
            var bootstrap = new GameObject("_Bootstrap");
            bootstrap.AddComponent<GameManager>();
            bootstrap.AddComponent<LevelManager>();

            // EventSystem
            var esGo = new GameObject("EventSystem");
            esGo.AddComponent<EventSystem>();
            // Prefer the New Input System UI module so the Unity 6 default input mode works out of the box.
            esGo.AddComponent<InputSystemUIInputModule>();

            // Canvas
            var canvasGo = new GameObject("MainMenuCanvas");
            var canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = canvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;
            canvasGo.AddComponent<GraphicRaycaster>();

            // Title
            var title = CreateTmpText(canvasGo.transform, "Title", "STUMBLE CLONE",
                new Vector2(0f, 200f), new Vector2(900f, 160f), 96, Color.white);
            title.alignment = TextAlignmentOptions.Center;
            title.fontStyle = FontStyles.Bold;

            // Play button
            var playBtn = CreateButton(canvasGo.transform, "PlayButton", "Play",
                new Vector2(0f, -40f), new Vector2(360f, 80f));
            // Quit button
            var quitBtn = CreateButton(canvasGo.transform, "QuitButton", "Quit",
                new Vector2(0f, -150f), new Vector2(360f, 80f));

            // Level select panel
            var panelGo = new GameObject("LevelSelectPanel", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            panelGo.transform.SetParent(canvasGo.transform, false);
            var panelRt = (RectTransform)panelGo.transform;
            panelRt.anchorMin = new Vector2(0.5f, 0.5f);
            panelRt.anchorMax = new Vector2(0.5f, 0.5f);
            panelRt.pivot = new Vector2(0.5f, 0.5f);
            panelRt.anchoredPosition = Vector2.zero;
            panelRt.sizeDelta = new Vector2(720f, 540f);
            var panelImg = panelGo.GetComponent<Image>();
            panelImg.color = new Color(0f, 0f, 0f, 0.7f);

            var raceBtn         = CreateButton(panelGo.transform, "RaceButton",        "Obstacle Race",  new Vector2(0f,  170f), new Vector2(520f, 70f));
            var survivalBtn     = CreateButton(panelGo.transform, "SurvivalButton",    "Last Survivor",  new Vector2(0f,   70f), new Vector2(520f, 70f));
            var lastStandingBtn = CreateButton(panelGo.transform, "LastStandingButton","Battle Royale",  new Vector2(0f,  -30f), new Vector2(520f, 70f));
            var backBtn         = CreateButton(panelGo.transform, "BackButton",        "Back",           new Vector2(0f, -170f), new Vector2(520f, 70f));

            // UI controller GameObject
            var uiGo = new GameObject("UI");
            var mainMenu = uiGo.AddComponent<MainMenuUI>();
            var levelSelect = uiGo.AddComponent<LevelSelectUI>();
            uiGo.AddComponent<UIBootstrapper>();

            var soMm = new SerializedObject(mainMenu);
            soMm.FindProperty("playButton").objectReferenceValue = playBtn;
            soMm.FindProperty("quitButton").objectReferenceValue = quitBtn;
            soMm.FindProperty("levelSelectPanel").objectReferenceValue = panelGo;
            soMm.ApplyModifiedPropertiesWithoutUndo();

            var soLs = new SerializedObject(levelSelect);
            soLs.FindProperty("raceButton").objectReferenceValue = raceBtn;
            soLs.FindProperty("survivalButton").objectReferenceValue = survivalBtn;
            soLs.FindProperty("lastStandingButton").objectReferenceValue = lastStandingBtn;
            soLs.FindProperty("backButton").objectReferenceValue = backBtn;
            soLs.ApplyModifiedPropertiesWithoutUndo();

            // Panel starts inactive (MainMenuUI.Awake hides it anyway, but this avoids a one-frame flash).
            panelGo.SetActive(false);

            EnsureFolder("Assets/Scenes");
            EditorSceneManager.SaveScene(scene, MainMenuScenePath);
        }

        private static Button CreateButton(Transform parent, string name, string label, Vector2 anchoredPos, Vector2 size)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button));
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = new Vector2(0.5f, 0.5f);
            rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = anchoredPos;
            rt.sizeDelta = size;

            var img = go.GetComponent<Image>();
            img.color = new Color(0.15f, 0.35f, 0.65f, 1f);

            var text = CreateTmpText(go.transform, "Label", label, Vector2.zero, size, 36, Color.white);
            text.alignment = TextAlignmentOptions.Center;

            return go.GetComponent<Button>();
        }

        private static TextMeshProUGUI CreateTmpText(Transform parent, string name, string content,
            Vector2 anchoredPos, Vector2 size, int fontSize, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = new Vector2(0.5f, 0.5f);
            rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = anchoredPos;
            rt.sizeDelta = size;
            var tmp = go.AddComponent<TextMeshProUGUI>();
            tmp.text = content;
            tmp.fontSize = fontSize;
            tmp.color = color;
            tmp.alignment = TextAlignmentOptions.Center;
            // Use the TMP default font asset if available (TMP Essentials must be imported).
            // If unavailable, TMP_Settings.defaultFontAsset will be null and TMP shows a warning,
            // but layout still works.
            return tmp;
        }

        // =======================================================================
        // Step G — Build settings
        // =======================================================================

        private static void ConfigureBuildSettings()
        {
            EditorBuildSettings.scenes = new[]
            {
                new EditorBuildSettingsScene(MainMenuScenePath,     true),
                new EditorBuildSettingsScene(RaceScenePath,         true),
                new EditorBuildSettingsScene(SurvivalScenePath,     true),
                new EditorBuildSettingsScene(LastStandingScenePath, true),
            };
        }

        // =======================================================================
        // Utility — SerializedObject field setter
        // =======================================================================

        private static void SetSerializedField(UnityEngine.Object target, string propName, object value)
        {
            if (target == null) return;
            var so = new SerializedObject(target);
            var p = so.FindProperty(propName);
            if (p == null)
            {
                Debug.LogWarning($"[Bootstrap] SerializedProperty '{propName}' not found on {target.GetType().Name}.");
                return;
            }
            switch (p.propertyType)
            {
                case SerializedPropertyType.Integer:
                    p.intValue = Convert.ToInt32(value);
                    break;
                case SerializedPropertyType.String:
                    p.stringValue = value?.ToString() ?? string.Empty;
                    break;
                case SerializedPropertyType.Float:
                    p.floatValue = Convert.ToSingle(value);
                    break;
                case SerializedPropertyType.Boolean:
                    p.boolValue = Convert.ToBoolean(value);
                    break;
                case SerializedPropertyType.ObjectReference:
                    p.objectReferenceValue = value as UnityEngine.Object;
                    break;
                default:
                    Debug.LogWarning($"[Bootstrap] Unhandled property type {p.propertyType} for {propName}.");
                    break;
            }
            so.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
#endif
