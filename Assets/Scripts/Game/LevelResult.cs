using System;
using StumbleClone.Core;

namespace StumbleClone.Game
{
    [Serializable]
    public class LevelResult
    {
        public LevelMode mode;
        public IRacer winner;
        public bool playerWon;
        public int playerRank;
        public float duration;

        public LevelResult() { }

        public LevelResult(LevelMode mode, IRacer winner, bool playerWon, int playerRank, float duration)
        {
            this.mode = mode;
            this.winner = winner;
            this.playerWon = playerWon;
            this.playerRank = playerRank;
            this.duration = duration;
        }
    }
}
