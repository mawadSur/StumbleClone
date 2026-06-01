using System.Collections.Generic;

namespace StumbleClone.Bots
{
    public static class BotNameGenerator
    {
        private static readonly string[] Pool =
        {
            "Wobble", "Tumble", "Bumper", "Crash", "Wiggle", "Flop",
            "Bounce", "Stumble", "Jiggle", "Splat", "Boing", "Squish",
            "Noodle", "Goof", "Klutz", "Doof", "Bonk", "Zoom",
            "Wobblee", "Tipsy",
        };

        private static readonly HashSet<string> Used = new HashSet<string>();
        private static readonly System.Random Rng = new System.Random();

        public static string GetUnique()
        {
            if (Used.Count >= Pool.Length) Reset();

            int guard = 0;
            while (guard < 64)
            {
                string candidate = Pool[Rng.Next(Pool.Length)];
                if (Used.Add(candidate)) return candidate;
                guard++;
            }

            string fallback = "Bot_" + Rng.Next(1000, 9999);
            Used.Add(fallback);
            return fallback;
        }

        public static void Reset()
        {
            Used.Clear();
        }
    }
}
