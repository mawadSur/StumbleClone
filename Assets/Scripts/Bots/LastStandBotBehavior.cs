using StumbleClone.Core;
using StumbleClone.Obstacles;
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

        // Per-bot skill 0..1 — higher = dodges hazards more reliably, charges from farther.
        private readonly float _skill;
        private readonly float _dodgeScanRadius;
        private readonly Collider[] _scanBuffer = new Collider[12];

        public LastStandBotBehavior(
            Transform arenaCenter,
            float arenaRadius,
            float safeRingFraction = 0.8f,
            float chargeRange = 5f,
            float contactRange = GameConstants.DefaultPushRange,
            float skill = 0.7f,
            float dodgeScanRadius = 5.5f)
        {
            _arenaCenter = arenaCenter;
            _arenaRadius = arenaRadius;
            _safeRingFraction = safeRingFraction;
            _chargeRange = chargeRange;
            _contactRange = contactRange;
            _skill = Mathf.Clamp01(skill);
            _dodgeScanRadius = dodgeScanRadius;
        }

        public override void Tick(BotController bot)
        {
            Transform t = bot.Transform;
            Vector3 pos = t.position;

            // 1) Survival first: dodge incoming arena hazards. Reaction reliability scales with
            //    skill, so weaker bots get clipped more often (the field isn't a uniform blob).
            ArenaObstacle hazard = FindNearestHazard(pos, out float hazardDistSqr);
            float reactRadius = Mathf.Lerp(_dodgeScanRadius * 0.55f, _dodgeScanRadius, _skill);
            if (hazard != null && hazardDistSqr <= reactRadius * reactRadius)
            {
                Vector3 away = pos - hazard.transform.position; away.y = 0f;
                if (away.sqrMagnitude < 0.01f) away = (_arenaCenter != null ? pos - _arenaCenter.position : -t.forward);
                away.Normalize();

                // Sidestep away from the hazard, but bias back toward center so the dodge
                // doesn't fling the bot off the rim.
                Vector3 dodge = pos + away * 3.5f;
                if (_arenaCenter != null) dodge = Vector3.Lerp(dodge, _arenaCenter.position, 0.35f);
                bot.SetDestination(dodge);

                // Hop over a very close low hazard (boulders/rams) — skill gates the timing.
                float jumpSqr = (_dodgeScanRadius * 0.4f) * (_dodgeScanRadius * 0.4f);
                if (hazardDistSqr <= jumpSqr && Random.value < _skill) bot.Jump();
                return;
            }

            // 2) Don't drift off the edge.
            float distFromCenter = _arenaCenter != null
                ? Vector3.Distance(new Vector3(pos.x, _arenaCenter.position.y, pos.z), _arenaCenter.position)
                : 0f;
            float safeRing = _arenaRadius * _safeRingFraction;
            if (_arenaCenter != null && distFromCenter > safeRing)
            {
                bot.SetDestination(_arenaCenter.position);
                return;
            }

            // 3) Hunt the nearest racer and shove them. Skill widens the charge range (more aggressive).
            float charge = _chargeRange * Mathf.Lerp(0.7f, 1.25f, _skill);
            IRacer target = FindNearestTarget(bot, pos);
            if (target != null)
            {
                Vector3 targetPos = target.Transform.position;
                float sqr = (targetPos - pos).sqrMagnitude;
                if (sqr <= charge * charge)
                {
                    bot.SetDestination(targetPos);
                    if (sqr <= _contactRange * _contactRange) bot.TryPush(target);
                    return;
                }
            }

            if (_arenaCenter != null) bot.SetDestination(_arenaCenter.position);
        }

        private ArenaObstacle FindNearestHazard(Vector3 pos, out float bestSqr)
        {
            bestSqr = float.MaxValue;
            ArenaObstacle best = null;
            // Scan broadly and filter by component — obstacles are primitives without a fixed layer.
            int hits = Physics.OverlapSphereNonAlloc(pos, _dodgeScanRadius, _scanBuffer, ~0, QueryTriggerInteraction.Collide);
            for (int i = 0; i < hits; i++)
            {
                Collider c = _scanBuffer[i];
                if (c == null) continue;
                var obs = c.GetComponentInParent<ArenaObstacle>();
                if (obs == null) continue;
                float sqr = (obs.transform.position - pos).sqrMagnitude;
                if (sqr < bestSqr) { bestSqr = sqr; best = obs; }
            }
            return best;
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
