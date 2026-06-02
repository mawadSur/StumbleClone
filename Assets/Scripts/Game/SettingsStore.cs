using UnityEngine;

namespace StumbleClone.Game
{
    /// Player-tunable game settings (audio levels + look sensitivity), persisted in PlayerPrefs so
    /// they survive scene loads and sessions. Set from the in-game Settings menu (SettingsUI); read
    /// by AudioManager (to attenuate SFX) and ThirdPersonCamera (to scale look speed). Every getter
    /// clamps to its valid range; every setter writes through to disk immediately.
    public static class SettingsStore
    {
        private const string Prefix = "stumbleclone.settings.";

        private const string MasterKey         = Prefix + "master";
        private const string MusicKey          = Prefix + "music";
        private const string SfxKey            = Prefix + "sfx";
        private const string LookKey           = Prefix + "look";
        private const string ReducedMotionKey  = Prefix + "reducedmotion";
        private const string HighContrastKey   = Prefix + "highcontrasttelegraphs";

        private const float DefaultVolume = 0.8f;
        private const float DefaultLook   = 1f;
        private const bool  DefaultReducedMotion = false;
        private const bool  DefaultHighContrast  = false;

        /// Minimum look-sensitivity multiplier the slider exposes.
        public const float LookMin = 0.5f;

        /// Maximum look-sensitivity multiplier the slider exposes.
        public const float LookMax = 3f;

        /// Overall output level (0..1). Multiplied into every other channel so it acts as a global
        /// trim. Default 0.8.
        public static float MasterVolume
        {
            get => Mathf.Clamp01(PlayerPrefs.GetFloat(MasterKey, DefaultVolume));
            set { PlayerPrefs.SetFloat(MasterKey, Mathf.Clamp01(value)); PlayerPrefs.Save(); }
        }

        /// Background-music level (0..1), before the Master trim. Default 0.8.
        public static float MusicVolume
        {
            get => Mathf.Clamp01(PlayerPrefs.GetFloat(MusicKey, DefaultVolume));
            set { PlayerPrefs.SetFloat(MusicKey, Mathf.Clamp01(value)); PlayerPrefs.Save(); }
        }

        /// Sound-effects level (0..1), before the Master trim. Default 0.8.
        public static float SfxVolume
        {
            get => Mathf.Clamp01(PlayerPrefs.GetFloat(SfxKey, DefaultVolume));
            set { PlayerPrefs.SetFloat(SfxKey, Mathf.Clamp01(value)); PlayerPrefs.Save(); }
        }

        /// Camera look-speed multiplier (LookMin..LookMax). 1 leaves the tuned defaults unchanged;
        /// higher turns faster. Applies to mouse, gamepad, and touch look. Default 1.
        public static float LookSensitivity
        {
            get => Mathf.Clamp(PlayerPrefs.GetFloat(LookKey, DefaultLook), LookMin, LookMax);
            set { PlayerPrefs.SetFloat(LookKey, Mathf.Clamp(value, LookMin, LookMax)); PlayerPrefs.Save(); }
        }

        /// Accessibility: when true, UI entrance animations are minimized (overlays fade in almost
        /// instantly instead of scaling/easing) to reduce vestibular-trigger motion. Stored as a
        /// PlayerPrefs int (0/1). Default false (full motion). Read by OverlayIntro.
        public static bool ReducedMotion
        {
            get => PlayerPrefs.GetInt(ReducedMotionKey, DefaultReducedMotion ? 1 : 0) != 0;
            set { PlayerPrefs.SetInt(ReducedMotionKey, value ? 1 : 0); PlayerPrefs.Save(); }
        }

        /// Accessibility: when true, hazard telegraphs use a bolder, higher-contrast treatment
        /// (strong dark outline ring + brighter fill) so they read without relying on the
        /// yellow→red colour cue alone. Stored as a PlayerPrefs int (0/1). Default false (standard
        /// treatment, which still carries a shape cue). Read by TelegraphIndicator.
        public static bool HighContrastTelegraphs
        {
            get => PlayerPrefs.GetInt(HighContrastKey, DefaultHighContrast ? 1 : 0) != 0;
            set { PlayerPrefs.SetInt(HighContrastKey, value ? 1 : 0); PlayerPrefs.Save(); }
        }
    }
}
