using StumbleClone.Core;
using UnityEngine;

namespace StumbleClone.Bots
{
    public sealed class RaceBotBehavior : BotBehavior
    {
        private readonly Transform _finishLine;
        private readonly float _jitterRadius;
        private readonly float _obstacleProbeDistance;
        private Vector3 _jitterOffset;

        public RaceBotBehavior(Transform finishLine, float jitterRadius = 1.5f, float obstacleProbeDistance = 2f)
        {
            _finishLine = finishLine;
            _jitterRadius = jitterRadius;
            _obstacleProbeDistance = obstacleProbeDistance;
        }

        public override void OnAttach(BotController bot)
        {
            Vector2 jitter = Random.insideUnitCircle * _jitterRadius;
            _jitterOffset = new Vector3(jitter.x, 0f, jitter.y);
        }

        public override void Tick(BotController bot)
        {
            if (_finishLine == null) return;

            Vector3 target = _finishLine.position + _jitterOffset;
            bot.SetDestination(target);

            Transform t = bot.Transform;
            Vector3 origin = t.position + Vector3.up * 0.6f;
            if (Physics.Raycast(origin, t.forward, out RaycastHit hit, _obstacleProbeDistance))
            {
                if (hit.collider != null && hit.collider.CompareTag("Obstacle") && bot.IsGrounded())
                {
                    bot.Jump();
                }
            }
        }
    }
}
