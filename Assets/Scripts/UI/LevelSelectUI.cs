using UnityEngine;
using UnityEngine.UI;
using StumbleClone.Core;
using StumbleClone.Game;

namespace StumbleClone.UI
{
    /// Panel that lets the player pick a level mode. Lives inside the main menu
    /// scene; visibility is toggled by MainMenuUI.
    public class LevelSelectUI : MonoBehaviour
    {
        [SerializeField] private Button raceButton;
        [SerializeField] private Button survivalButton;
        [SerializeField] private Button lastStandingButton;
        [SerializeField] private Button backButton;

        private void Awake()
        {
            if (raceButton != null) raceButton.onClick.AddListener(OnRaceClicked);
            if (survivalButton != null) survivalButton.onClick.AddListener(OnSurvivalClicked);
            if (lastStandingButton != null) lastStandingButton.onClick.AddListener(OnLastStandingClicked);
            if (backButton != null) backButton.onClick.AddListener(OnBackClicked);

            ThemeBinder.StyleButton(raceButton, UITheme.Secondary);
            ThemeBinder.StyleButton(survivalButton, UITheme.Secondary);
            ThemeBinder.StyleButton(lastStandingButton, UITheme.Secondary);
            ThemeBinder.StyleButton(backButton, UITheme.Neutral);
        }

        private void OnEnable() => OverlayIntro.Play(gameObject);

        private void OnDestroy()
        {
            if (raceButton != null) raceButton.onClick.RemoveListener(OnRaceClicked);
            if (survivalButton != null) survivalButton.onClick.RemoveListener(OnSurvivalClicked);
            if (lastStandingButton != null) lastStandingButton.onClick.RemoveListener(OnLastStandingClicked);
            if (backButton != null) backButton.onClick.RemoveListener(OnBackClicked);
        }

        private void OnRaceClicked() => LoadLevel(LevelMode.Race);
        private void OnSurvivalClicked() => LoadLevel(LevelMode.Survival);
        private void OnLastStandingClicked() => LoadLevel(LevelMode.LastStanding);

        private void OnBackClicked()
        {
            gameObject.SetActive(false);
        }

        private void LoadLevel(LevelMode mode)
        {
            if (GameManager.Instance == null)
            {
                Debug.LogWarning("GameManager not found in scene; using fallback behavior");
                return;
            }
            GameManager.Instance.LoadLevel(mode);
        }
    }
}
