using UnityEngine;
using StumbleClone.Core;

namespace StumbleClone.Obstacles
{
    [DisallowMultipleComponent]
    public class PushPad : MonoBehaviour
    {
        [SerializeField] private float launchForce = 18f;
        [SerializeField] private float horizontalBias = 0f;

        private void Reset()
        {
            gameObject.tag = GameConstants.TagPushPad;
        }

        private void OnTriggerEnter(Collider other)
        {
            var racer = other.GetComponentInParent<IRacer>();
            if (racer == null) return;

            Vector3 force = transform.up * launchForce;
            if (horizontalBias > 0f)
            {
                force += transform.forward * horizontalBias;
            }
            racer.Knockback(force);
        }
    }
}
