using StumbleClone.Core;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace StumbleClone.Game
{
    public class LevelManager : MonoBehaviour
    {
        public static LevelManager Instance { get; private set; }

        private const string SceneMainMenu = "MainMenu";
        private const string SceneRace = "Level_Race";
        private const string SceneSurvival = "Level_Survival";
        private const string SceneLastStanding = "Level_LastStanding";

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;
            DontDestroyOnLoad(gameObject);
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        public void Load(LevelMode mode)
        {
            if (GameManager.Instance != null)
            {
                GameManager.Instance.currentMode = mode;
            }

            SceneManager.LoadScene(SceneNameFor(mode));
        }

        public void ReturnToMenu()
        {
            SceneManager.LoadScene(SceneMainMenu);
        }

        private static string SceneNameFor(LevelMode mode)
        {
            switch (mode)
            {
                case LevelMode.Race: return SceneRace;
                case LevelMode.Survival: return SceneSurvival;
                case LevelMode.LastStanding: return SceneLastStanding;
                default: return SceneMainMenu;
            }
        }
    }
}
