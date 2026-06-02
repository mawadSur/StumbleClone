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

        private const string MasterKey  = Prefix + "master";
        private const string MusicKey   = Prefix + "music";
        private const string SfxKey     = Prefix + "sfx";
        private const string LookKey    = Prefix + "look";

        private const float DefaultVolume = 0.8f;
        private const float DefaultLook   = 1f;

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
    }
}
