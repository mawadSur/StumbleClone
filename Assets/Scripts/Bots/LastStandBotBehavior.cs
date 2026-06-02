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
        // Per-field aggression 0..1 (from difficulty) — how hard the bot hunts/shoves the player.
        private readonly float _aggression;
        private readonly float _dodgeScanRadius;
        private readonly Collider[] _scanBuffer = new Collider[12];

        public LastStandBotBehavior(
            Transform arenaCenter,
            float arenaRadius,
            float safeRingFraction = 0.8f,
            float chargeRange = 5f,
            float contactRange = GameConstants.DefaultPushRange,
            float skill = 0.7f,
            float aggression = 0.55f,
            float dodgeScanRadius = 5.5f)
        {
            _arenaCenter = arenaCenter;
            _arenaRadius = arenaRadius;
            _safeRingFraction = safeRingFraction;
            _chargeRange = chargeRange;
            _contactRange = contactRange;
            _skill = Mathf.Clamp01(skill);
            _aggression = Mathf.Clamp01(aggression);
            _dodgeScanRadius = dodgeScanRadius;
        }

        public override void Tick(BotController bot)
        {
            Transform t = bot.Transform;
            Vector3 pos = t.position;

            // Knocked off? Scramble back toward the arena centre (BotController handles the air work).
            bot.RecoveryAnchor = _arenaCenter;

            // 1) Survival first: dodge incoming arena hazards. Reaction now scales UP with both
            //    skill and aggression, so the toughest bots see hazards sooner and almost always
            //    sidestep or hop them — aggressive no longer means careless.
            ArenaObstacle hazard = FindNearestHazard(pos, out float hazardDistSqr);
            float reflex = Mathf.Max(_skill, _aggression);
            float reactRadius = Mathf.Lerp(_dodgeScanRadius * 0.7f, _dodgeScanRadius * 1.15f, reflex);
            if (hazard != null && hazardDistSqr <= reactRadius * reactRadius)
            {
                Vector3 away = pos - hazard.transform.position; away.y = 0f;
                if (away.sqrMagnitude < 0.01f) away = (_arenaCenter != null ? pos - _arenaCenter.position : -t.forward);
                away.Normalize();

                Vector3 dodge = pos + away * 3.5f;
                if (_arenaCenter != null) dodge = Vector3.Lerp(dodge, _arenaCenter.position, 0.35f);
                bot.SetDestination(dodge);

                // Hop over close hazards — reliable for skilled/aggressive bots, certain when imminent.
                float jumpRange = _dodgeScanRadius * 0.5f;
                float imminentRange = _dodgeScanRadius * 0.28f;
                if (hazardDistSqr <= jumpRange * jumpRange &&
                    (Random.value < reflex || hazardDistSqr <= imminentRange * imminentRange))
                    bot.Jump();
                return;
            }

            // Prefer the shrinking safe-zone when it's live: feed bots the closing
            // radius + centre so they retreat as the ring contracts. Otherwise fall
            // back to the static arena ring this bot was constructed with.
            bool shrinkActive = ArenaShrinker.Active;
            Vector3 ringCenter = shrinkActive
                ? ArenaShrinker.Center
                : (_arenaCenter != null ? _arenaCenter.position : pos);
            // The shrinker's CurrentSafeRadius is already the *safe* radius (its own
            // fraction baked in), so don't re-apply _safeRingFraction to it.
            float effectiveRadius = shrinkActive ? ArenaShrinker.CurrentSafeRadius : _arenaRadius;
            float safeRing = shrinkActive ? effectiveRadius : _arenaRadius * _safeRingFraction;

            float distFromCenter = Vector3.Distance(
                new Vector3(pos.x, ringCenter.y, pos.z), ringCenter);
            // Aggressive bots will chase past the safe ring (to shove the victim off at the rim).
            float huntRing = Mathf.Lerp(safeRing, effectiveRadius * 0.96f, _aggression);

            // 2) Pick a victim — prefer the human player, increasingly so with aggression.
            IRacer target = SelectTarget(bot, pos, out bool targetIsPlayer);

            // 3) Hunt + shove. Charge range widens hard with skill AND aggression.
            float charge = _chargeRange * Mathf.Lerp(0.85f, 2.3f, Mathf.Max(_skill, _aggression));
            if (target != null)
            {
                Vector3 targetPos = target.Transform.position;
                float sqr = (targetPos - pos).sqrMagnitude;
                bool inRange = sqr <= charge * charge;
                // Commit if the target is reachable and we're allowed this far out (always for the
                // player when aggressive — that's how bots herd you toward the edge).
                if (inRange && (distFromCenter <= huntRing || targetIsPlayer))
                {
                    bot.SetDestination(targetPos);
                    if (sqr <= _contactRange * _contactRange)
                    {
                        // Shove the victim OUTWARD toward the nearest edge — i.e. off the platform.
                        Vector3 outward = _arenaCenter != null ? (targetPos - _arenaCenter.position) : (targetPos - pos);
                        outward.y = 0f;
                        if (outward.sqrMagnitude < 0.01f) outward = targetPos - pos;
                        bot.TryPushToward(target, outward);
                    }
                    return;
                }
            }

            // 4) Nothing to chase — keep to safe ground. Steer to the (shrinking) ring
            //    centre so bots actively pull inward as the zone closes; if already well
            //    inside, holding the centre is harmless.
            if (shrinkActive || _arenaCenter != null) bot.SetDestination(ringCenter);
        }

        /// Choose who to chase. The human player is preferred within a lock range that grows with
        /// aggression — Easy bots only pick the player if they're basically the nearest racer, Hard
        /// bots commit to the player from anywhere in the arena.
        private IRacer SelectTarget(BotController self, Vector3 pos, out bool isPlayer)
        {
            isPlayer = false;
            IRacer player = RacerRegistry.Player;
            if (player != null && player != (IRacer)self && player.IsAlive && !player.IsFinished)
            {
                float pSqr = (player.Transform.position - pos).sqrMagnitude;
                float lockRange = Mathf.Lerp(_chargeRange * 0.8f, _arenaRadius * 2.2f, _aggression);
                if (pSqr <= lockRange * lockRange) { isPlayer = true; return player; }
            }
            return FindNearestTarget(self, pos);
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
