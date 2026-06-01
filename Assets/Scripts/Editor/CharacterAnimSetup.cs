using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace StumbleClone.EditorTools
{
    /// One-shot setup: builds a shared locomotion AnimatorController from the
    /// Quaternius character clips and assigns Animators (controller + avatar) to
    /// the Player and Bot prefabs. All Quaternius characters share the identical
    /// "CharacterArmature" skeleton, so a single controller drives every model.
    public static class CharacterAnimSetup
    {
        private const string ControllerPath = "Assets/Animators/CharacterLocomotion.controller";
        private const string ClipSourceFbx = "Assets/Art/Quaternius/Characters/BlueSoldier_Male.fbx";
        private const string PlayerPrefab = "Assets/Prefabs/Player.prefab";
        private const string PlayerFbx = "Assets/Art/Quaternius/Characters/BlueSoldier_Male.fbx";
        private const string BotPrefab = "Assets/Prefabs/Bot.prefab";
        private const string BotFbx = "Assets/Art/Quaternius/Characters/Casual_Male.fbx";

        private const string DoneKey = "StumbleClone_AnimSetupDone_v3";

        [InitializeOnLoadMethod]
        private static void AutoRun()
        {
            if (EditorPrefs.GetBool(DoneKey, false)) return;
            Debug.Log("[AnimSetup] AutoRun firing (synchronous)...");
            try { Run(); EditorPrefs.SetBool(DoneKey, true); }
            catch (System.Exception e) { Debug.LogError("[AnimSetup] " + e); }
        }

        [MenuItem("StumbleClone/Setup Character Animations")]
        public static void Run()
        {
            var clips = AssetDatabase.LoadAllAssetsAtPath(ClipSourceFbx)
                .OfType<AnimationClip>()
                .Where(c => !c.name.StartsWith("__preview"))
                .ToArray();

            AnimationClip Find(string suffix) =>
                clips.FirstOrDefault(c => c.name == "CharacterArmature|" + suffix)
                ?? clips.FirstOrDefault(c => c.name.EndsWith("|" + suffix))
                ?? clips.FirstOrDefault(c => c.name.EndsWith(suffix));

            var idle = Find("Idle");
            var walk = Find("Walk");
            var run = Find("Run");
            var jump = Find("Jump");
            var hit = Find("RecieveHit") ?? Find("Defeat") ?? Find("Death");

            if (idle == null || run == null || jump == null)
            {
                Debug.LogError($"[AnimSetup] Missing core clips. Found: {string.Join(", ", clips.Select(c => c.name))}");
                return;
            }
            if (walk == null) walk = run;

            // Fresh controller
            AssetDatabase.DeleteAsset(ControllerPath);
            System.IO.Directory.CreateDirectory("Assets/Animators");
            var controller = AnimatorController.CreateAnimatorControllerAtPath(ControllerPath);

            controller.AddParameter("Speed", AnimatorControllerParameterType.Float);
            controller.AddParameter("Grounded", AnimatorControllerParameterType.Bool);
            controller.AddParameter("Jump", AnimatorControllerParameterType.Trigger);
            controller.AddParameter("Fall", AnimatorControllerParameterType.Bool);
            controller.AddParameter("KnockedDown", AnimatorControllerParameterType.Trigger);

            var sm = controller.layers[0].stateMachine;

            // Locomotion blend tree (Idle -> Walk -> Run on Speed 0..1)
            BlendTree loco;
            var locoState = controller.CreateBlendTreeInController("Locomotion", out loco, 0);
            loco.blendType = BlendTreeType.Simple1D;
            loco.blendParameter = "Speed";
            loco.useAutomaticThresholds = false;
            loco.AddChild(idle, 0f);
            loco.AddChild(walk, 0.5f);
            loco.AddChild(run, 1f);
            sm.defaultState = locoState;

            // Jump
            var jumpState = sm.AddState("Jump");
            jumpState.motion = jump;
            var toJump = sm.AddAnyStateTransition(jumpState);
            toJump.AddCondition(AnimatorConditionMode.If, 0, "Jump");
            toJump.hasExitTime = false;
            toJump.duration = 0.08f;
            toJump.canTransitionToSelf = false;
            var jumpBack = jumpState.AddTransition(locoState);
            jumpBack.hasExitTime = true;
            jumpBack.exitTime = 0.75f;
            jumpBack.duration = 0.2f;

            // Knocked down (hit reaction)
            if (hit != null)
            {
                var hitState = sm.AddState("KnockedDown");
                hitState.motion = hit;
                var toHit = sm.AddAnyStateTransition(hitState);
                toHit.AddCondition(AnimatorConditionMode.If, 0, "KnockedDown");
                toHit.hasExitTime = false;
                toHit.duration = 0.1f;
                toHit.canTransitionToSelf = false;
                var hitBack = hitState.AddTransition(locoState);
                hitBack.hasExitTime = true;
                hitBack.exitTime = 0.85f;
                hitBack.duration = 0.25f;
            }

            EditorUtility.SetDirty(controller);
            AssetDatabase.SaveAssets();

            AssignToPrefab(PlayerPrefab, PlayerFbx, controller, false);
            AssignToPrefab(BotPrefab, BotFbx, controller, true);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log($"[AnimSetup] Done. Clips -> Idle:{idle.name} Walk:{walk.name} Run:{run.name} Jump:{jump.name} Hit:{(hit!=null?hit.name:"none")}");
        }

        private static void AssignToPrefab(string prefabPath, string fbxPath, AnimatorController controller, bool addBotAnimator)
        {
            var root = PrefabUtility.LoadPrefabContents(prefabPath);
            var character = root.transform.Find("Character");
            if (character == null)
            {
                Debug.LogError($"[AnimSetup] No 'Character' child in {prefabPath}");
                PrefabUtility.UnloadPrefabContents(root);
                return;
            }
            var anim = character.GetComponent<Animator>();
            if (anim == null) anim = character.gameObject.AddComponent<Animator>();
            anim.runtimeAnimatorController = controller;
            anim.applyRootMotion = false;
            anim.cullingMode = AnimatorCullingMode.CullUpdateTransforms;

            var avatar = AssetDatabase.LoadAllAssetsAtPath(fbxPath).OfType<Avatar>().FirstOrDefault();
            if (avatar != null) anim.avatar = avatar;

            if (addBotAnimator && root.GetComponent<StumbleClone.Bots.BotAnimator>() == null)
                root.AddComponent<StumbleClone.Bots.BotAnimator>();

            PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
            PrefabUtility.UnloadPrefabContents(root);
            Debug.Log($"[AnimSetup] Assigned Animator to {prefabPath} (avatar: {(avatar!=null?avatar.name:"none")})");
        }
    }
}
