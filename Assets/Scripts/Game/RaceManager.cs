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

        // Last-resort anti-hang net: if a round runs longer than this (seconds from level
        // start), end it regardless. Generous so it never trips a legitimately slow race.
        [SerializeField] private float maxRoundDuration = 180f;

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
            GameEvents.RacerEliminated += HandleRacerEliminated;
            GameEvents.LevelStarted += HandleLevelStarted;
        }

        private void OnDisable()
        {
            GameEvents.RacerFinished -= HandleRacerFinished;
            GameEvents.RacerEliminated -= HandleRacerEliminated;
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

            // A human player who finishes should always get a result rather than stand at the
            // line waiting for bots. This ends a single-player Race the moment the player crosses,
            // independent of the serialized finishOnPlayerFinish toggle (which is also honored).
            bool playerFinished = racer.IsPlayer;
            bool reachedCap = _finishers.Count >= finishersToEnd;
            bool allFinished = AllRegisteredFinished();

            // End on whichever happens first: cap reached, everyone alive finished, or the player finished.
            if (reachedCap || allFinished || playerFinished || finishOnPlayerFinish)
            {
                EndLevel();
            }
        }

        /// Safety net: an elimination can leave too few racers able to ever reach
        /// finishersToEnd. Re-run the end check so the round can't hang waiting for
        /// finishers that will never arrive.
        private void HandleRacerEliminated(IRacer racer)
        {
            if (_ended) return;

            // End if enough have already finished, or if nobody alive remains who could finish.
            if (_finishers.Count >= finishersToEnd || NoFinishableRacersRemain())
            {
                EndLevel();
            }
        }

        /// True when every still-ALIVE registered racer has finished. Eliminated racers
        /// (IsAlive == false) are treated as "done" so an elimination can no longer block
        /// the round from ending. Returns false when no racers are registered.
        private bool AllRegisteredFinished()
        {
            var all = RacerRegistry.All;
            if (all == null || all.Count == 0) return false;
            for (int i = 0; i < all.Count; i++)
            {
                var r = all[i];
                if (r == null) continue;
                // Only a racer that is still alive AND not yet finished can keep the round open.
                if (r.IsAlive && !r.IsFinished) return false;
            }
            return true;
        }

        /// True when no still-alive, not-yet-finished racer remains — i.e. nobody left
        /// who could ever cross the line. Used as a "nobody can finish" safety end-condition.
        /// Distinct from AllRegisteredFinished only in that it also returns true for an
        /// empty registry (a degenerate state we still want to end on).
        private bool NoFinishableRacersRemain()
        {
            var all = RacerRegistry.All;
            if (all == null) return true;
            for (int i = 0; i < all.Count; i++)
            {
                var r = all[i];
                if (r == null) continue;
                if (r.IsAlive && !r.IsFinished) return false;
            }
            return true;
        }

        private void EndLevel()
        {
            if (_ended) return;
            _ended = true;
            GameEvents.RaiseLevelEnded(ResolveWinner());
        }

        /// Pick a winner without ever indexing an empty list: first finisher if any,
        /// otherwise the player, otherwise the first registered racer, otherwise null.
        private IRacer ResolveWinner()
        {
            if (_finishers.Count > 0) return _finishers[0];

            var player = RacerRegistry.Player;
            if (player != null) return player;

            var all = RacerRegistry.All;
            if (all != null)
            {
                for (int i = 0; i < all.Count; i++)
                {
                    if (all[i] != null) return all[i];
                }
            }
            return null;
        }

        private void Update()
        {
            if (_ended) return;

            // Hard backstop: end the round if it has run past the max duration. Checked before
            // the finishLine guard so a missing finish reference can never defeat the anti-hang net.
            if (maxRoundDuration > 0f && Time.time - _levelStartTime >= maxRoundDuration)
            {
                EndLevel();
                return;
            }

            if (finishLine == null) return;

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
