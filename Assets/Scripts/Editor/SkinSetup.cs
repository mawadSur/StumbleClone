using System.IO;
using System.Linq;
using StumbleClone.Game;
using StumbleClone.Visuals;
using UnityEditor;
using UnityEngine;

namespace StumbleClone.EditorTools
{
    /// Builds the selectable character skins: for every id in SkinCatalog, generates a prefab at
    /// Resources/Skins/&lt;id&gt; from the matching Quaternius model, with an Animator bound to the
    /// shared locomotion controller + that model's avatar (so all skins animate identically). Also
    /// attaches the CharacterSkin component to Player.prefab (player-selected) and Bot.prefab
    /// (random) so the swap happens automatically at spawn.
    ///
    /// Idempotent: existing skin prefabs and already-attached components are skipped.
    public static class SkinSetup
    {
        private const string ModelDir = "Assets/Art/Quaternius/Characters/";
        private const string OutDir = "Assets/Resources/Skins";
        private const string ControllerPath = "Assets/Animators/CharacterLocomotion.controller";
        private const string PlayerPrefab = "Assets/Prefabs/Player.prefab";
        private const string BotPrefab = "Assets/Prefabs/Bot.prefab";

        [MenuItem("StumbleClone/Build Skins")]
        public static void Run()
        {
            var controller = AssetDatabase.LoadAssetAtPath<UnityEditor.Animations.AnimatorController>(ControllerPath);
            if (controller == null)
            {
                Debug.LogError($"[SkinSetup] No animator controller at {ControllerPath} — run Setup Character Animations first.");
                return;
            }

            Directory.CreateDirectory(OutDir);

            // Most Quaternius models are imported Humanoid but reuse a shared avatar (no own sub-
            // asset). Since every model uses the identical "CharacterArmature" bone hierarchy, the
            // default model's humanoid avatar maps them all — use it whenever a model lacks its own.
            var fallbackAvatar = AssetDatabase
                .LoadAllAssetsAtPath(ModelDir + SkinCatalog.Default + ".fbx")
                .OfType<Avatar>().FirstOrDefault();
            if (fallbackAvatar == null)
                Debug.LogWarning("[SkinSetup] No fallback avatar from the default model — skins without an own avatar may not animate.");

            int built = 0;
            foreach (string id in SkinCatalog.Ids)
            {
                string outPath = $"{OutDir}/{id}.prefab";
                if (AssetDatabase.LoadAssetAtPath<GameObject>(outPath) != null) continue; // already built

                string modelPath = ModelDir + id + ".fbx";
                var fbx = AssetDatabase.LoadAssetAtPath<GameObject>(modelPath);
                if (fbx == null)
                {
                    Debug.LogWarning($"[SkinSetup] Missing model {modelPath} — skipping skin '{id}'.");
                    continue;
                }

                var avatar = AssetDatabase.LoadAllAssetsAtPath(modelPath).OfType<Avatar>().FirstOrDefault();
                if (avatar == null)
                {
                    avatar = fallbackAvatar; // shared CharacterArmature → default model's avatar maps it
                    Debug.Log($"[SkinSetup] '{id}' has no own avatar — using the shared default avatar.");
                }

                var inst = Object.Instantiate(fbx);
                var anim = inst.GetComponent<Animator>();
                if (anim == null) anim = inst.AddComponent<Animator>();
                anim.runtimeAnimatorController = controller;
                if (avatar != null) anim.avatar = avatar;
                anim.applyRootMotion = false;
                anim.cullingMode = AnimatorCullingMode.CullUpdateTransforms;
                inst.name = "Character";

                PrefabUtility.SaveAsPrefabAsset(inst, outPath);
                Object.DestroyImmediate(inst);
                built++;
                Debug.Log($"[SkinSetup] Built skin prefab {outPath} (avatar: {(avatar != null ? avatar.name : "none")})");
            }

            AttachSkinComponent(PlayerPrefab, CharacterSkin.Mode.Player);
            AttachSkinComponent(BotPrefab, CharacterSkin.Mode.RandomBot);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log($"[SkinSetup] Done — {built} skin prefab(s) built.");
        }

        private static void AttachSkinComponent(string prefabPath, CharacterSkin.Mode mode)
        {
            var root = PrefabUtility.LoadPrefabContents(prefabPath);
            if (root == null) { Debug.LogWarning($"[SkinSetup] Could not load {prefabPath}"); return; }

            var existing = root.GetComponent<CharacterSkin>();
            if (existing == null)
            {
                var cs = root.AddComponent<CharacterSkin>();
                var so = new SerializedObject(cs);
                var modeProp = so.FindProperty("mode");
                if (modeProp != null) { modeProp.enumValueIndex = (int)mode; so.ApplyModifiedPropertiesWithoutUndo(); }
                PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
                Debug.Log($"[SkinSetup] Added CharacterSkin ({mode}) to {prefabPath}");
            }

            PrefabUtility.UnloadPrefabContents(root);
        }
    }
}
