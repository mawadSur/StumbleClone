using UnityEngine;

namespace StumbleClone.Animation
{
    /// Shared helper: decides whether an Animator actually has clips to play, and wires up the
    /// ProceduralCharacterAnimator fallback when it doesn't. Used by PlayerAnimator/BotAnimator.
    public static class AnimatorClipUtil
    {
        /// True only if the controller exposes at least one real AnimationClip. A controller whose
        /// states reference clips that don't exist (the current dangling-motion situation) reports
        /// zero clips here, so we know to fall back.
        public static bool HasRealClips(Animator a)
        {
            if (a == null || a.runtimeAnimatorController == null) return false;
            var clips = a.runtimeAnimatorController.animationClips;
            return clips != null && clips.Length > 0;
        }

        /// Disable the clipless Animator (nothing to play, frees the transform) and attach a
        /// ProceduralCharacterAnimator to the host, pointed at the Animator's mesh transform.
        public static ProceduralCharacterAnimator AttachFallback(MonoBehaviour host, Animator a)
        {
            Transform visual = a != null ? a.transform : host.transform;
            if (a != null) a.enabled = false;

            var proc = host.GetComponent<ProceduralCharacterAnimator>();
            if (proc == null) proc = host.gameObject.AddComponent<ProceduralCharacterAnimator>();
            proc.SetVisual(visual);
            return proc;
        }
    }
}
