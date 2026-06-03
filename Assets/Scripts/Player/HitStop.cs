using System.Collections;
using StumbleClone.Game;
using UnityEngine;

namespace StumbleClone.Player
{
    /// Tiny global "hit-stop" helper: on a meaningful impact (a real knockback or a landed push)
    /// it briefly drops <see cref="Time.timeScale"/> to near zero, then restores it. The pause is
    /// measured in UNSCALED time so the freeze lasts a fixed wall-clock duration regardless of the
    /// slowed scale. The effect is purely game-feel juice — it never alters gameplay state.
    ///
    /// No scene wiring: a hidden, DontDestroyOnLoad runner is created on first use to host the
    /// coroutine. Re-entrant calls extend the freeze by restarting against the original timeScale,
    /// so overlapping hits never permanently strand the game at a low timeScale.
    ///
    /// Honors the ReducedMotion accessibility setting: when enabled, Do() is a no-op so the game
    /// never visibly stutters for players sensitive to motion/timing jolts.
    public static class HitStop
    {
        private static Runner _runner;
        // The timeScale to return to once the freeze ends. Captured the first time a freeze starts
        // (i.e. while time is still at normal scale) so stacked hits all restore to the real value,
        // never to the already-slowed scale.
        private static float _restoreScale = 1f;

        /// Briefly freeze game time for <paramref name="seconds"/> of unscaled (wall-clock) time,
        /// then restore. Intended range ~40-80ms. No-op when ReducedMotion is on, when not in play
        /// mode, or for non-positive durations. Safe to call repeatedly; overlapping calls extend
        /// the freeze rather than stacking slow-downs.
        public static void Do(float seconds)
        {
            if (seconds <= 0f) return;
            if (SettingsStore.ReducedMotion) return;
            if (!Application.isPlaying) return;

            EnsureRunner();
            _runner.Begin(seconds);
        }

        private static void EnsureRunner()
        {
            if (_runner != null) return;
            var go = new GameObject("~HitStopRunner") { hideFlags = HideFlags.HideAndDontSave };
            Object.DontDestroyOnLoad(go);
            _runner = go.AddComponent<Runner>();
        }

        /// Persistent hidden MonoBehaviour that owns the freeze coroutine.
        private sealed class Runner : MonoBehaviour
        {
            private Coroutine _active;

            public void Begin(float seconds)
            {
                // Only capture the restore target when no freeze is currently running, so a second
                // hit landing mid-freeze doesn't memoize the slowed scale as the "normal" one.
                if (_active == null)
                    _restoreScale = Time.timeScale;
                else
                    StopCoroutine(_active);

                _active = StartCoroutine(Freeze(seconds));
            }

            private IEnumerator Freeze(float seconds)
            {
                Time.timeScale = 0.05f;
                yield return new WaitForSecondsRealtime(seconds);
                Time.timeScale = _restoreScale;
                _active = null;
            }
        }
    }
}
