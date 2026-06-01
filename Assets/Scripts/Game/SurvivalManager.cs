using System.Linq;
using StumbleClone.Core;
using UnityEngine;

namespace StumbleClone.Game
{
    public class SurvivalManager : MonoBehaviour
    {
        [SerializeField] private float totalDuration = 60f;
        [SerializeField] private Transform safeZoneCenter;

        public Transform SafeZoneCenter => safeZoneCenter;
        public float TimeRemaining => _timeRemaining;

        private float _timeRemaining;
        private float _tickAccumulator;
        private bool _ended;

        private void OnEnable()
        {
            _timeRemaining = totalDuration;
            _tickAccumulator = 0f;
            _ended = false;

            GameEvents.RacerEliminated += HandleRacerEliminated;
            GameEvents.LevelStarted += HandleLevelStarted;
        }

        private void OnDisable()
        {
            GameEvents.RacerEliminated -= HandleRacerEliminated;
            GameEvents.LevelStarted -= HandleLevelStarted;
        }

        private void HandleLevelStarted(LevelMode mode)
        {
            _timeRemaining = totalDuration;
            _tickAccumulator = 0f;
            _ended = false;
            GameEvents.RaiseSurvivalTimerTick(_timeRemaining);
        }

        private void HandleRacerEliminated(IRacer racer)
        {
            if (_ended) return;
            CheckEnd();
        }

        private void Update()
        {
            if (_ended) return;

            _timeRemaining -= Time.deltaTime;
            _tickAccumulator += Time.deltaTime;

            if (_tickAccumulator >= 1f)
            {
                _tickAccumulator -= 1f;
                GameEvents.RaiseSurvivalTimerTick(Mathf.Max(0f, _timeRemaining));
            }

            if (_timeRemaining <= 0f)
            {
                _timeRemaining = 0f;
                GameEvents.RaiseSurvivalTimerTick(0f);
                EndLevel();
                return;
            }

            CheckEnd();
        }

        private void CheckEnd()
        {
            if (_ended) return;
            int alive = RacerRegistry.AliveCount;
            if (alive <= 1) EndLevel();
        }

        private void EndLevel()
        {
            if (_ended) return;
            _ended = true;

            var all = RacerRegistry.All;
            IRacer winner = null;
            for (int i = 0; i < all.Count; i++)
            {
                if (all[i].IsAlive) { winner = all[i]; break; }
            }

            GameEvents.RaiseLevelEnded(winner);
        }
    }
}
