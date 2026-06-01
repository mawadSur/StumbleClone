using System.Collections.Generic;
using UnityEngine;
using StumbleClone.Core;

namespace StumbleClone.Level
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Collider))]
    public class Checkpoint : MonoBehaviour
    {
        [SerializeField] private Transform respawnPoint;
        [SerializeField] private int order;

        // Stored by IRacer reference because the racer may be player or bot.
        public static readonly Dictionary<IRacer, Checkpoint> LastCheckpoint = new Dictionary<IRacer, Checkpoint>();

        public int Order => order;

        public Vector3 RespawnPosition =>
            respawnPoint != null ? respawnPoint.position : transform.position + Vector3.up * 1.5f;

        public Quaternion RespawnRotation =>
            respawnPoint != null ? respawnPoint.rotation : transform.rotation;

        private void Reset()
        {
            var col = GetComponent<Collider>();
            if (col != null) col.isTrigger = true;
        }

        private void OnTriggerEnter(Collider other)
        {
            var racer = other.GetComponentInParent<IRacer>();
            if (racer == null || !racer.IsAlive) return;

            // Only advance forward — never let a backward pass downgrade respawn point.
            if (LastCheckpoint.TryGetValue(racer, out var existing) && existing != null && existing.order >= order) return;

            LastCheckpoint[racer] = this;
        }

        public static Checkpoint For(IRacer racer)
        {
            if (racer == null) return null;
            LastCheckpoint.TryGetValue(racer, out var cp);
            return cp;
        }

        public static void ClearAll() => LastCheckpoint.Clear();
    }
}
