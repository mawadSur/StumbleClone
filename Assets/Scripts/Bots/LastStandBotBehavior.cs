using StumbleClone.Core;
using UnityEngine;

namespace StumbleClone.Bots
{
    public sealed class LastStandBotBehavior : BotBehavior
    {
        private readonly Transform _arenaCenter;
        private readonly float _arenaRadius;
        private readonly float _safeRingFraction;
        private readonly float _chargeRange;
        private readonly float _contactRange;

        public LastStandBotBehavior(
            Transform arenaCenter,
            float arenaRadius,
            float safeRingFraction = 0.8f,
            float chargeRange = 5f,
            float contactRange = GameConstants.DefaultPushRange)
        {
            _arenaCenter = arenaCenter;
            _arenaRadius = arenaRadius;
            _safeRingFraction = safeRingFraction;
            _chargeRange = chargeRange;
            _contactRange = contactRange;
        }

        public override void Tick(BotController bot)
        {
            Transform t = bot.Transform;
            Vector3 pos = t.position;

            float distFromCenter = _arenaCenter != null
                ? Vector3.Distance(new Vector3(pos.x, _arenaCenter.position.y, pos.z), _arenaCenter.position)
                : 0f;

            float safeRing = _arenaRadius * _safeRingFraction;

            if (_arenaCenter != null && distFromCenter > safeRing)
            {
                bot.SetDestination(_arenaCenter.position);
                return;
            }

            IRacer target = FindNearestTarget(bot, pos);
            if (target != null)
            {
                Vector3 targetPos = target.Transform.position;
                float sqr = (targetPos - pos).sqrMagnitude;

                if (sqr <= _chargeRange * _chargeRange)
                {
                    bot.SetDestination(targetPos);

                    if (sqr <= _contactRange * _contactRange)
                    {
                        bot.TryPush(target);
                    }
                    return;
                }
            }

            if (_arenaCenter != null)
            {
                bot.SetDestination(_arenaCenter.position);
            }
        }

        private IRacer FindNearestTarget(BotController self, Vector3 pos)
        {
            IRacer best = null;
            float bestSqr = float.MaxValue;
            var all = RacerRegistry.All;
            for (int i = 0; i < all.Count; i++)
            {
                IRacer r = all[i];
                if (r == null || r == (IRacer)self) continue;
                if (!r.IsAlive || r.IsFinished) continue;

                float sqr = (r.Transform.position - pos).sqrMagnitude;
                if (sqr < bestSqr)
                {
                    bestSqr = sqr;
                    best = r;
                }
            }
            return best;
        }
    }
}
