using System;
using System.Collections.Generic;
using StumbleClone.Obstacles;

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
        public static event Action<string, SpawnDirection> WaveTelegraphed; // (patternName, rim direction)

        /// Raised when a chaos round modifier fires — carries the modifier id string
        /// so HUD, analytics, and quest system can react.
        public static event Action<string> RoundModifierActivated;

        public static void RaiseLevelStarted(LevelMode mode) => LevelStarted?.Invoke(mode);
        public static void RaiseLevelEnded(IRacer winner) => LevelEnded?.Invoke(winner);
        public static void RaiseRacerFinished(IRacer r) => RacerFinished?.Invoke(r);
        public static void RaiseRacerEliminated(IRacer r) => RacerEliminated?.Invoke(r);
        public static void RaiseRacerRankChanged(IRacer r, int rank) => RacerRankChanged?.Invoke(r, rank);
        public static void RaiseSurvivalTimerTick(float seconds) => SurvivalTimerTick?.Invoke(seconds);

        /// Raised when a wave begins telegraphing, before its hazards spawn. `direction` is the
        /// wave's leading rim octant so audio/UI can play a directional "tell".
        public static void RaiseWaveTelegraphed(string patternName, SpawnDirection direction)
            => WaveTelegraphed?.Invoke(patternName, direction);
        public static void RaiseRoundModifierActivated(string modifierId) => RoundModifierActivated?.Invoke(modifierId);

        /// Clear all subscribers — call from scene unload guards to avoid stale references.
        public static void Reset()
        {
            LevelStarted = null;
            LevelEnded = null;
            RacerFinished = null;
            RacerEliminated = null;
            RacerRankChanged = null;
            SurvivalTimerTick = null;
            WaveTelegraphed = null;
            RoundModifierActivated = null;
        }
    }
}
