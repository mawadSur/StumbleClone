using StumbleClone.Core;
using UnityEngine;
using UnityEngine.AI;

namespace StumbleClone.Bots
{
    public sealed class SurvivalBotBehavior : BotBehavior
    {
        private readonly Transform _safeAnchor;
        private readonly float _killzoneScanRadius;
        private readonly float _wanderRadius;
        private readonly Collider[] _scanBuffer = new Collider[8];

        public SurvivalBotBehavior(Transform safeAnchor, float killzoneScanRadius = 6f, float wanderRadius = 8f)
        {
            _safeAnchor = safeAnchor;
            _killzoneScanRadius = killzoneScanRadius;
            _wanderRadius = wanderRadius;
        }

        public override void Tick(BotController bot)
        {
            Transform t = bot.Transform;
            Vector3 pos = t.position;

            int killMask = 1 << GameConstants.LayerKillzone;
            int hits = Physics.OverlapSphereNonAlloc(pos, _killzoneScanRadius, _scanBuffer, killMask, QueryTriggerInteraction.Collide);

            Vector3 fleeFrom = Vector3.zero;
            float nearestSqr = float.MaxValue;
            bool found = false;

            for (int i = 0; i < hits; i++)
            {
                Collider c = _scanBuffer[i];
                if (c == null) continue;
                Vector3 closest = c.ClosestPoint(pos);
                float sqr = (closest - pos).sqrMagnitude;
                if (sqr < nearestSqr)
                {
                    nearestSqr = sqr;
                    fleeFrom = closest;
                    found = true;
                }
            }

            Vector3 destination;
            if (found)
            {
                Vector3 away = pos - fleeFrom;
                away.y = 0f;
                if (away.sqrMagnitude < 0.01f) away = -t.forward;
                away.Normalize();
                Vector3 anchor = _safeAnchor != null ? _safeAnchor.position : pos;
                destination = anchor + away * 2f;
            }
            else if (_safeAnchor != null)
            {
                destination = _safeAnchor.position;
            }
            else
            {
                destination = RandomNavmeshPoint(pos, _wanderRadius);
            }

            if (NavMesh.SamplePosition(destination, out NavMeshHit navHit, 4f, NavMesh.AllAreas))
            {
                bot.SetDestination(navHit.position);
            }
        }

        private static Vector3 RandomNavmeshPoint(Vector3 center, float radius)
        {
            Vector2 r = Random.insideUnitCircle * radius;
            Vector3 candidate = center + new Vector3(r.x, 0f, r.y);
            if (NavMesh.SamplePosition(candidate, out NavMeshHit hit, radius, NavMesh.AllAreas))
            {
                return hit.position;
            }
            return center;
        }
    }
}
