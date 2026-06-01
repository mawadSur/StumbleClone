using System;
using StumbleClone.Core;

namespace StumbleClone.Game
{
    /// One persisted leaderboard row. Serialized to JSON by LeaderboardStore.
    [Serializable]
    public class LeaderboardEntry
    {
        public string playerName;
        public LevelMode mode;
        public float score;
        public float duration;
        public int rank;
        public long unixTimeUtc;
    }
}
