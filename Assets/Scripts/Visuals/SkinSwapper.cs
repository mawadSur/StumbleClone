using UnityEngine;

namespace StumbleClone.Visuals
{
    /// Swaps a racer's visual model ("Character" child) for a different skin at runtime. Every skin
    /// prefab (Resources/Skins/&lt;id&gt;, built by the SkinSetup editor tool) is a Quaternius model on
    /// the shared "CharacterArmature" skeleton with an Animator already bound to the shared
    /// locomotion controller + its own avatar — so the replacement animates exactly like the
    /// original. This is a whole-model swap (not a mesh-only swap), which keeps skeleton/bindposes
    /// correct for any model.
    ///
    /// Must run before the racer's animator driver (PlayerAnimator/BotAnimator) caches its Animator
    /// — CharacterSkin does this via an early DefaultExecutionOrder.
    public static class SkinSwapper
    {
        /// Replace the "Character" child of root with the skin prefab. Returns false (and changes
        /// nothing) if the id is empty or the prefab can't be loaded, so a bad id never breaks the
        /// existing model.
        public static bool Apply(Transform root, string skinId)
        {
            if (root == null || string.IsNullOrEmpty(skinId)) return false;

            GameObject prefab = Resources.Load<GameObject>("Skins/" + skinId);
            if (prefab == null)
            {
                Debug.LogWarning($"[SkinSwapper] No skin prefab at Resources/Skins/{skinId} — keeping current model.");
                return false;
            }

            Transform old = root.Find("Character");
            Vector3 lp = old != null ? old.localPosition : Vector3.zero;
            Quaternion lr = old != null ? old.localRotation : Quaternion.identity;
            Vector3 ls = old != null ? old.localScale : Vector3.one;

            // Hide + remove the old model first so the racer's animator driver (which fetches the
            // first ACTIVE Animator in children) binds to the new one, not the outgoing model.
            if (old != null)
            {
                old.gameObject.SetActive(false);
                old.name = "Character_Old";
                Object.Destroy(old.gameObject);
            }

            GameObject inst = Object.Instantiate(prefab, root);
            inst.name = "Character";
            inst.transform.localPosition = lp;
            inst.transform.localRotation = lr;
            inst.transform.localScale = ls;
            return true;
        }
    }
}
