using System.Collections;
using StumbleClone.Core;
using StumbleClone.Game;
using TMPro;
using UnityEngine;

namespace StumbleClone.UI
{
    /// Round-start title card + countdown shown over a gameplay scene before play begins, so a
    /// round never starts cold. Built entirely in code (RuntimeUI + UITheme), self-contained, and
    /// self-destructing. GameManager freezes the simulation (Time.timeScale = 0) while this plays,
    /// so the countdown coroutine runs on UNSCALED time (WaitForSecondsRealtime / unscaledDeltaTime)
    /// and invokes its onGo callback only when the count reaches GO! — the moment GameManager
    /// restores timeScale and raises LevelStarted, releasing the player, bots and hazards together.
    ///
    /// Honors the Reduced-Motion accessibility setting: the per-number scale-punch is skipped (the
    /// number simply holds) and the cadence is a touch quicker, with no vestibular-trigger movement.
    public sealed class RoundIntro : MonoBehaviour
    {
        // Cadence (unscaled seconds). Each numeral holds for one beat; GO! flashes briefly before
        // play resumes so the transition reads but never drags.
        private const float CountBeat = 0.8f;
        private const float ReducedMotionBeat = 0.55f;
        private const float GoHold = 0.45f;

        // Per-number scale-punch: the numeral pops in oversized then settles to 1, matching the
        // OverlayIntro ease-out-cubic feel. Reused, single-shot, on unscaled time.
        private const float PunchFrom = 1.55f;
        private const float PunchDuration = 0.32f;

        private GameObject _overlay;
        private System.Action _onGo;

        /// Build the overlay and start the countdown. Invokes <paramref name="onGo"/> exactly once
        /// when the count reaches GO!, then fades the card and destroys itself. Safe to call while
        /// Time.timeScale == 0 — all timing is unscaled.
        public static RoundIntro Show(LevelMode mode, System.Action onGo)
        {
            var go = new GameObject("RoundIntro");
            var intro = go.AddComponent<RoundIntro>();
            intro._onGo = onGo;
            intro.Build(mode);
            return intro;
        }

        private TMP_Text _countLabel;

        private void Build(LevelMode mode)
        {
            // Sort high so the card lands above any in-scene HUD that may have already built itself.
            _overlay = RuntimeUI.Overlay("RoundIntro", 500);

            // Dim full-screen backdrop — focuses the eye on the title/count, fades with the card.
            RuntimeUI.Panel(_overlay.transform, "Backdrop", new Color(0f, 0f, 0f, 0.62f),
                Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);

            (string title, string objective) = ModeText(mode);

            // Mode title — large, gold, upper third.
            var titleLabel = RuntimeUI.Label(_overlay.transform, title, 96,
                new Vector2(0.5f, 0.5f), new Vector2(0f, 250f), new Vector2(1400f, 140f));
            titleLabel.fontStyle = FontStyles.Bold;
            titleLabel.color = UITheme.Gold;

            // Objective line — muted, just below the title.
            var objectiveLabel = RuntimeUI.Label(_overlay.transform, objective, 42,
                new Vector2(0.5f, 0.5f), new Vector2(0f, 150f), new Vector2(1400f, 70f));
            objectiveLabel.color = UITheme.OnSurfaceMuted;

            // Countdown numeral — centre, driven by the coroutine.
            _countLabel = RuntimeUI.Label(_overlay.transform, "", 220,
                new Vector2(0.5f, 0.5f), new Vector2(0f, -80f), new Vector2(700f, 320f));
            _countLabel.fontStyle = FontStyles.Bold;
            _countLabel.color = UITheme.OnSurface;

            // Fade/scale the whole card in (unscaled — plays under the timeScale freeze).
            OverlayIntro.Play(_overlay);

            StartCoroutine(Run());
        }

        private IEnumerator Run()
        {
            bool reducedMotion = SettingsStore.ReducedMotion;
            float beat = reducedMotion ? ReducedMotionBeat : CountBeat;

            for (int n = 3; n >= 1; n--)
            {
                SetCount(n.ToString(), reducedMotion);
                yield return new WaitForSecondsRealtime(beat);
            }

            // GO! — release play first so input/hazards resume exactly on this frame, then let the
            // card linger briefly and fade out without holding up the simulation.
            SetCount("GO!", reducedMotion);
            _onGo?.Invoke();
            _onGo = null;

            if (_countLabel != null) _countLabel.color = UITheme.Success;

            yield return new WaitForSecondsRealtime(GoHold);

            // Fade the card out on unscaled time so it dissolves cleanly now that play has resumed.
            yield return FadeOutAndDestroy();
        }

        /// Set the numeral text and, unless Reduced Motion is on, kick a fresh scale-punch.
        private void SetCount(string text, bool reducedMotion)
        {
            if (_countLabel == null) return;
            _countLabel.text = text;
            if (reducedMotion)
            {
                _countLabel.rectTransform.localScale = Vector3.one;
                return;
            }
            // Single-shot punch driven inline (no extra component) on the active coroutine host.
            StartCoroutine(Punch(_countLabel.rectTransform));
        }

        private static IEnumerator Punch(RectTransform rt)
        {
            float t = 0f;
            while (t < PunchDuration)
            {
                if (rt == null) yield break;
                t += Time.unscaledDeltaTime;
                float k = Mathf.Clamp01(t / PunchDuration);
                float e = 1f - Mathf.Pow(1f - k, 3f); // ease-out cubic, matching OverlayIntro
                rt.localScale = Vector3.one * Mathf.Lerp(PunchFrom, 1f, e);
                yield return null;
            }
            if (rt != null) rt.localScale = Vector3.one;
        }

        private IEnumerator FadeOutAndDestroy()
        {
            CanvasGroup cg = _overlay != null ? _overlay.GetComponent<CanvasGroup>() : null;
            if (_overlay != null && cg == null) cg = _overlay.AddComponent<CanvasGroup>();

            const float fade = 0.25f;
            float t = 0f;
            while (cg != null && t < fade)
            {
                t += Time.unscaledDeltaTime;
                cg.alpha = 1f - Mathf.Clamp01(t / fade);
                yield return null;
            }

            if (_overlay != null) Destroy(_overlay);
            Destroy(gameObject);
        }

        /// Map each mode to its title + one-line objective.
        private static (string, string) ModeText(LevelMode mode)
        {
            switch (mode)
            {
                case LevelMode.Race:
                    return ("RACE", "First to the finish");
                case LevelMode.Survival:
                    return ("SURVIVAL", "Outlast the timer");
                case LevelMode.LastStanding:
                default:
                    return ("KNOCKOUT", "Last one standing wins");
            }
        }
    }
}
