using StumbleClone.Core;
using StumbleClone.Obstacles;
using UnityEngine;

namespace StumbleClone.Game
{
    /// Knockout arena mode. The threat is a stream of hazards spawned from the platform
    /// rim (see ObstacleSpawner) that escalate with time and eliminations; there is no
    /// timer and no shrinking ring — the round ends when only one racer remains.
    /// Also stands up the runtime ObstacleSpawner and SpectateController so the mode is
    /// self-contained and needs no extra scene wiring beyond the arenaCenter reference.
    public class LastStandingManager : MonoBehaviour
    {
        [SerializeField] private Transform arenaCenter;
        [Tooltip("Radius at which hazards spawn on the rim and aim inward. ~just inside the platform edge.")]
        [SerializeField] private float arenaRadius = 18f;

        public Transform ArenaCenter => arenaCenter;

        /// Rim radius where hazards spawn — read by ArenaShrinker to derive the
        /// shrinking safe radius (it pulls this in slightly to sit on solid ground).
        public float ArenaRadius => arenaRadius;

        private bool _ended;
        private ObstacleSpawner _spawner;

        private void OnEnable()
        {
            _ended = false;
            GameEvents.LevelStarted += HandleLevelStarted;
            GameEvents.RacerEliminated += HandleRacerEliminated;
        }

        private void OnDisable()
        {
            GameEvents.LevelStarted -= HandleLevelStarted;
            GameEvents.RacerEliminated -= HandleRacerEliminated;
        }

        private void HandleLevelStarted(LevelMode mode)
        {
            _ended = false;
            EnsureSubsystems();
            if (_spawner != null) _spawner.Begin(arenaCenter, arenaRadius);
        }

        private void EnsureSubsystems()
        {
            if (_spawner == null)
            {
                var go = new GameObject("ObstacleSpawner");
                go.transform.SetParent(transform, false);
                _spawner = go.AddComponent<ObstacleSpawner>();
            }
            // SpectateController is now self-bootstrapping in every gameplay scene
            // (see SpectateController.Bootstrap), so the mode no longer creates one here.
        }

        private void HandleRacerEliminated(IRacer racer)
        {
            if (_ended) return;
            if (RacerRegistry.AliveCount <= 1) EndLevel();
        }

        private void EndLevel()
        {
            if (_ended) return;
            _ended = true;

            if (_spawner != null) _spawner.StopSpawning();

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
