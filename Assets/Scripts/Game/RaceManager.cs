using System.Collections.Generic;
using System.Linq;
using StumbleClone.Core;
using UnityEngine;

namespace StumbleClone.Game
{
    public class RaceManager : MonoBehaviour
    {
        [SerializeField] private Transform finishLine;
        [SerializeField] private int finishersToEnd = 4;
        [SerializeField] private bool finishOnPlayerFinish = false;

        public int PlayerRank { get; private set; } = 1;

        private readonly List<IRacer> _finishers = new List<IRacer>(16);
        private float _levelStartTime;
        private bool _ended;

        private void OnEnable()
        {
            _finishers.Clear();
            _ended = false;
            PlayerRank = 1;
            _levelStartTime = Time.time;

            GameEvents.RacerFinished += HandleRacerFinished;
            GameEvents.LevelStarted += HandleLevelStarted;
        }

        private void OnDisable()
        {
            GameEvents.RacerFinished -= HandleRacerFinished;
            GameEvents.LevelStarted -= HandleLevelStarted;
        }

        private void HandleLevelStarted(LevelMode mode)
        {
            _levelStartTime = Time.time;
            _finishers.Clear();
            _ended = false;
        }

        private void HandleRacerFinished(IRacer racer)
        {
            if (racer == null || _ended) return;
            if (_finishers.Contains(racer)) return;

            _finishers.Add(racer);
            int rank = _finishers.Count;
            GameEvents.RaiseRacerRankChanged(racer, rank);

            bool playerFinished = racer.IsPlayer;
            bool reachedCap = _finishers.Count >= finishersToEnd;
            bool allFinished = AllRegisteredFinished();

            if (reachedCap || allFinished || (playerFinished && finishOnPlayerFinish))
            {
                EndLevel();
            }
        }

        private bool AllRegisteredFinished()
        {
            var all = RacerRegistry.All;
            if (all.Count == 0) return false;
            for (int i = 0; i < all.Count; i++)
            {
                if (!all[i].IsFinished) return false;
            }
            return true;
        }

        private void EndLevel()
        {
            _ended = true;
            IRacer winner = _finishers.Count > 0 ? _finishers[0] : null;
            GameEvents.RaiseLevelEnded(winner);
        }

        private void Update()
        {
            if (_ended || finishLine == null) return;

            var player = RacerRegistry.Player;
            if (player == null || player.IsFinished) return;

            var all = RacerRegistry.All;
            float playerDist = SqrDistXZ(player.Transform.position, finishLine.position);
            int rank = _finishers.Count + 1;

            for (int i = 0; i < all.Count; i++)
            {
                var r = all[i];
                if (r == null || r.IsPlayer || r.IsFinished || !r.IsAlive) continue;
                float d = SqrDistXZ(r.Transform.position, finishLine.position);
                if (d < playerDist) rank++;
            }

            PlayerRank = rank;
        }

        private static float SqrDistXZ(Vector3 a, Vector3 b)
        {
            float dx = a.x - b.x;
            float dz = a.z - b.z;
            return dx * dx + dz * dz;
        }
    }
}
