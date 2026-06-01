using UnityEngine;
using StumbleClone.Game;

namespace StumbleClone.UI
{
    /// Safety net so the main menu always has a GameManager available, even
    /// when launching directly into MainMenu during development. Place this on
    /// a root GameObject in the MainMenu scene.
    public class UIBootstrapper : MonoBehaviour
    {
        [SerializeField] private string gameManagerGameObjectName = "GameManager";

        private void Awake()
        {
            EnsureGameManager();
            EnsureLevelManager();
        }

        private void EnsureGameManager()
        {
            if (GameManager.Instance != null) return;
            var existing = FindFirstObjectByType<GameManager>();
            if (existing != null) return;

            var go = new GameObject(gameManagerGameObjectName);
            go.AddComponent<GameManager>();
            Debug.Log("UIBootstrapper: created fallback GameManager.");
        }

        private void EnsureLevelManager()
        {
            if (LevelManager.Instance != null) return;
            var existing = FindFirstObjectByType<LevelManager>();
            if (existing != null) return;

            var go = new GameObject("LevelManager");
            go.AddComponent<LevelManager>();
            Debug.Log("UIBootstrapper: created fallback LevelManager.");
        }
    }
}
