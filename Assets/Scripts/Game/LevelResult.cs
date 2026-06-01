using System;
using StumbleClone.Core;
using UnityEngine;

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
        /// Unified, higher-is-better score used by the leaderboard (per-mode rules below).
        public float score;

        // Scoring weights — tweak here (kept named, not magic, per coding standards).
        private const float RaceParTime = 100000f;   // race score = par - time*100 (faster is better)
        private const float RaceTimeWeight = 100f;
        private const float SurvivalTimeWeight = 100f; // survival: longer alive is better
        private const float LastStandTimeWeight = 20f;  // last-standing tiebreak on time survived
        private const float RankWeight = 10000f;        // placement bonus (1st best)
        private const float WinBonus = 50000f;

        public LevelResult() { }

        public LevelResult(LevelMode mode, IRacer winner, bool playerWon, int playerRank, float duration)
        {
            this.mode = mode;
            this.winner = winner;
            this.playerWon = playerWon;
            this.playerRank = playerRank;
            this.duration = duration;
            this.score = ScoreFor(mode, playerRank, duration, playerWon);
        }

        /// Higher is always better so the leaderboard sorts descending across modes.
        public static float ScoreFor(LevelMode mode, int rank, float duration, bool won)
        {
            float rankBonus = rank > 0 ? Mathf.Max(0f, 9 - rank) * RankWeight : 0f;
            switch (mode)
            {
                case LevelMode.Race:
                    return Mathf.Max(0f, RaceParTime - duration * RaceTimeWeight) + rankBonus;
                case LevelMode.Survival:
                    return duration * SurvivalTimeWeight + (won ? WinBonus : 0f);
                case LevelMode.LastStanding:
                    return rankBonus + duration * LastStandTimeWeight + (won ? WinBonus : 0f);
                default:
                    return duration;
            }
        }
    }
}
