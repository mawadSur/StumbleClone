using UnityEngine;

namespace StumbleClone.Game
{
    public enum Difficulty { Easy, Normal, Hard }

    /// Player-chosen bot difficulty, persisted in PlayerPrefs so it survives scene loads and
    /// sessions. Difficulty maps to the per-bot SKILL range rolled by BotSpawner; skill already
    /// drives agent speed, charge aggression, and hazard-dodge reliability in the bot behaviors,
    /// so one knob scales the whole field. Set from the main menu (TitleScreen).
    public static class BotDifficulty
    {
        private const string Key = "stumbleclone.botdifficulty";

        public static Difficulty Current
        {
            get => (Difficulty)Mathf.Clamp(PlayerPrefs.GetInt(Key, (int)Difficulty.Normal), 0, 2);
            set { PlayerPrefs.SetInt(Key, (int)value); PlayerPrefs.Save(); }
        }

        /// Range each bot's skill (0..1) is rolled within, so a difficulty still has variety.
        public static void SkillRange(out float min, out float max)
        {
            switch (Current)
            {
                case Difficulty.Easy: min = 0.10f; max = 0.40f; break;
                case Difficulty.Hard: min = 0.75f; max = 1.00f; break;
                default:              min = 0.35f; max = 0.80f; break; // Normal
            }
        }

        /// How hard bots hunt and shove the human player (0..1). Separate from skill so Hard reads
        /// as genuinely *aggressive*: it widens player-lock range, pursuit past the safe ring, and
        /// edge-directed pushes. Easy bots mostly mind themselves; Hard bots gang the player.
        public static float Aggression
        {
            get
            {
                switch (Current)
                {
                    case Difficulty.Easy: return 0.20f;
                    case Difficulty.Hard: return 1.00f;
                    default:              return 0.55f; // Normal
                }
            }
        }

        public static string Label
        {
            get
            {
                switch (Current)
                {
                    case Difficulty.Easy: return "EASY";
                    case Difficulty.Hard: return "HARD";
                    default: return "NORMAL";
                }
            }
        }

        /// Advance Easy -> Normal -> Hard -> Easy and persist; returns the new value.
        public static Difficulty Cycle()
        {
            Current = (Difficulty)(((int)Current + 1) % 3);
            return Current;
        }
    }
}
