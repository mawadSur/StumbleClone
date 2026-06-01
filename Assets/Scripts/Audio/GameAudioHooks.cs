using System.Collections;
using StumbleClone.Core;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace StumbleClone.Audio
{
    /// Plays SFX in response to global game events — no per-object wiring. Created and kept alive
    /// by AudioManager. Because GameManager calls GameEvents.Reset() on every scene load (which
    /// nulls all delegates, including ours), we re-subscribe one frame after each load so the
    /// hooks survive scene changes.
    public sealed class GameAudioHooks : MonoBehaviour
    {
        private void Awake()
        {
            SceneManager.sceneLoaded += OnSceneLoaded;
            Subscribe();
        }

        private void OnDestroy()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            Unsubscribe();
        }

        private void OnSceneLoaded(Scene scene, LoadSceneMode mode) => StartCoroutine(ReSubscribeNextFrame());

        private IEnumerator ReSubscribeNextFrame()
        {
            // Wait until after GameManager.HandleSceneLoaded -> GameEvents.Reset() has run.
            yield return null;
            Subscribe();
        }

        private void Subscribe()
        {
            Unsubscribe(); // never double-subscribe
            GameEvents.LevelStarted += OnStarted;
            GameEvents.RacerEliminated += OnEliminated;
            GameEvents.RacerFinished += OnFinished;
            GameEvents.LevelEnded += OnEnded;
        }

        private void Unsubscribe()
        {
            GameEvents.LevelStarted -= OnStarted;
            GameEvents.RacerEliminated -= OnEliminated;
            GameEvents.RacerFinished -= OnFinished;
            GameEvents.LevelEnded -= OnEnded;
        }

        private void OnStarted(LevelMode mode) => AudioManager.Play(Sfx.Start);
        private void OnEliminated(IRacer r) => AudioManager.Play(Sfx.Eliminate, 0.9f);
        private void OnFinished(IRacer r) { if (r != null && r.IsPlayer) AudioManager.Play(Sfx.Win); }
        private void OnEnded(IRacer winner) => AudioManager.Play(Sfx.Win, 0.8f);
    }
}
