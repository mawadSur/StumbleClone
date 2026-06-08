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

        private void Awake()
        {
            // The kill volume doubles as a visible translucent-RED box baked into each level scene
            // (the level builders gave the trigger cube a red URP material). Players found that red
            // floor under the arena distracting on a fall, so hide its renderer at runtime — falling
            // now reveals the night sky / scene below instead of a red box. The trigger collider is
            // left untouched, so falling off still eliminates / respawns exactly as before.
            var rend = GetComponent<Renderer>();
            if (rend != null) rend.enabled = false;
        }

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

            // Resolve the mode FIRST, then let the mode decide elimination-vs-respawn for
            // BOTH the player and bots. Resolution chain: GameManager (menu flow) → the
            // scene's own LevelSelfStart (Play-scene-directly flow) → serialized fallback.
            // Without the LevelSelfStart hop, a directly-played LastStanding scene would
            // fall back to Race and *respawn* a racer on top instead of eliminating them.
            LevelMode mode;
            if (GameManager.Instance != null)
                mode = GameManager.Instance.currentMode;
            else if (LevelSelfStart.Active != null)
                mode = LevelSelfStart.Active.Mode;
            else
                mode = fallbackMode;

            // Last-alive elimination modes: falling off = out for good (one life), for the
            // player AND bots alike. The spectate/Play-Again flow takes over from there.
            if (mode == LevelMode.LastStanding || mode == LevelMode.Survival)
            {
                racer.Eliminate();
                return;
            }

            // Race (and any other non-elimination mode): respawn at the most recent
            // checkpoint for BOTH the player and bots — a Race track with hazards that knock
            // you off is meant to be recoverable via checkpoints, not instantly fatal.
            Vector3 respawn = defaultRespawnPoint;
            var cp = Checkpoint.For(racer);
            if (cp != null) respawn = cp.RespawnPosition;
            racer.Respawn(respawn);
        }
    }
}
