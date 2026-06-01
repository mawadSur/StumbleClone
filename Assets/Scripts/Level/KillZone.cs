using UnityEngine;
using StumbleClone.Core;
using StumbleClone.Game;

namespace StumbleClone.Level
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Collider))]
    public class KillZone : MonoBehaviour
    {
        [SerializeField] private Vector3 defaultRespawnPoint = new Vector3(0f, 2f, 0f);
        [SerializeField] private LevelMode fallbackMode = LevelMode.Race;

        private void Reset()
        {
            gameObject.tag = GameConstants.TagKillzone;
            var col = GetComponent<Collider>();
            if (col != null) col.isTrigger = true;
        }

        private void OnTriggerEnter(Collider other)
        {
            var racer = other.GetComponentInParent<IRacer>();
            if (racer == null || !racer.IsAlive || racer.IsFinished) return;

            // The human player gets one life in every mode: falling off eliminates for good
            // (no respawn). The spectate/Play-Again flow takes over from there.
            if (racer.IsPlayer)
            {
                racer.Eliminate();
                return;
            }

            // Resolve the mode robustly: GameManager (menu flow) → the scene's own
            // LevelSelfStart (Play-scene-directly flow) → serialized fallback. Without the
            // LevelSelfStart hop, a directly-played LastStanding scene would fall back to
            // Race and *respawn* the player on top instead of eliminating them.
            LevelMode mode;
            if (GameManager.Instance != null)
                mode = GameManager.Instance.currentMode;
            else if (LevelSelfStart.Active != null)
                mode = LevelSelfStart.Active.Mode;
            else
                mode = fallbackMode;

            // Last-alive elimination modes: falling off = out for good (one life).
            if (mode == LevelMode.LastStanding || mode == LevelMode.Survival)
            {
                racer.Eliminate();
                return;
            }

            // Race: respawn at most recent checkpoint if available.
            Vector3 respawn = defaultRespawnPoint;
            var cp = Checkpoint.For(racer);
            if (cp != null) respawn = cp.RespawnPosition;
            racer.Respawn(respawn);
        }
    }
}
