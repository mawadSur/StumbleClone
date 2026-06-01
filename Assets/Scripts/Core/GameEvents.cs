using System;
using System.Collections.Generic;

namespace StumbleClone.Core
{
    /// Global event bus. Managers publish, UI/AI subscribe.
    /// Reset() should be called on scene unload to clear subscribers.
    public static class GameEvents
    {
        public static event Action<LevelMode> LevelStarted;
        public static event Action<IRacer> LevelEnded;          // winner (or null)
        public static event Action<IRacer> RacerFinished;
        public static event Action<IRacer> RacerEliminated;
        public static event Action<IRacer, int> RacerRankChanged; // (racer, rank starting at 1)
        public static event Action<float> SurvivalTimerTick;    // seconds remaining

        public static void RaiseLevelStarted(LevelMode mode) => LevelStarted?.Invoke(mode);
        public static void RaiseLevelEnded(IRacer winner) => LevelEnded?.Invoke(winner);
        public static void RaiseRacerFinished(IRacer r) => RacerFinished?.Invoke(r);
        public static void RaiseRacerEliminated(IRacer r) => RacerEliminated?.Invoke(r);
        public static void RaiseRacerRankChanged(IRacer r, int rank) => RacerRankChanged?.Invoke(r, rank);
        public static void RaiseSurvivalTimerTick(float seconds) => SurvivalTimerTick?.Invoke(seconds);

        /// Clear all subscribers — call from scene unload guards to avoid stale references.
        public static void Reset()
        {
            LevelStarted = null;
            LevelEnded = null;
            RacerFinished = null;
            RacerEliminated = null;
            RacerRankChanged = null;
            SurvivalTimerTick = null;
        }
    }
}
