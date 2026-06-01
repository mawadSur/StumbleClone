using System.Collections;
using StumbleClone.Core;
using UnityEngine;

namespace StumbleClone.Game
{
    /// Makes a level scene fully playable when it is opened and Played DIRECTLY in the
    /// editor — i.e. without coming through MainMenu, so no GameManager exists to drive
    /// the round. In that case nothing would ever raise GameEvents.LevelStarted, so the
    /// per-mode manager (LastStandingManager/RaceManager/SurvivalManager) would never
    /// stand up its subsystems (obstacle spawner, spectate overlay, HUD timers, …).
    ///
    /// When a GameManager IS present (the normal menu → LoadLevel flow) this stays
    /// completely dormant and lets GameManager own startup, so there is no double-start.
    ///
    /// It also serves as the authoritative per-scene LevelMode source for components like
    /// KillZone that need to know the mode even when GameManager is absent.
    ///
    /// Added to every level scene by the *LevelBuilder editor scripts with the matching mode.
    [DisallowMultipleComponent]
    public sealed class LevelSelfStart : MonoBehaviour
    {
        [SerializeField] private LevelMode mode = LevelMode.LastStanding;

        /// The mode this scene represents. Always valid, GameManager present or not.
        public LevelMode Mode => mode;

        /// The self-start instance for the currently loaded level scene, if any.
        public static LevelSelfStart Active { get; private set; }

        private void Awake()
        {
            Active = this;
        }

        private void OnDestroy()
        {
            if (Active == this) Active = null;
        }

        private IEnumerator Start()
        {
            // Menu flow owns startup; GameManager will raise LevelStarted itself.
            if (GameManager.Instance != null) yield break;

            // Racers register themselves in OnEnable during this same scene load, so we
            // must NOT clear the registry or reset the event bus here (that's only correct
            // on a GameManager-driven scene transition). Just let every object finish its
            // Awake/OnEnable/Start, then kick the round off once.
            yield return null;
            GameEvents.RaiseLevelStarted(mode);
        }
    }
}
