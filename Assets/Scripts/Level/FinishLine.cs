using UnityEngine;
using StumbleClone.Core;

namespace StumbleClone.Level
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Collider))]
    public class FinishLine : MonoBehaviour
    {
        private void Reset()
        {
            gameObject.tag = GameConstants.TagFinish;
            var col = GetComponent<Collider>();
            if (col != null) col.isTrigger = true;
        }

        private void OnTriggerEnter(Collider other)
        {
            var racer = other.GetComponentInParent<IRacer>();
            if (racer == null) return;
            if (racer.IsFinished || !racer.IsAlive) return;
            racer.Finish();
        }
    }
}
