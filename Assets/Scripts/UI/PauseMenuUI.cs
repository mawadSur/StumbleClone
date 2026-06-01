using UnityEngine;
using UnityEngine.UI;
using StumbleClone.Game;
using StumbleClone.Player;

namespace StumbleClone.UI
{
    /// Toggleable pause overlay. Watches the PlayerInputHandler (if any) for a
    /// PausePressed flag and flips Time.timeScale between 0 and 1.
    public class PauseMenuUI : MonoBehaviour
    {
        [SerializeField] private GameObject panel;
        [SerializeField] private Button resumeButton;
        [SerializeField] private Button menuButton;
        [SerializeField] private Button quitButton;

        private PlayerInputHandler inputHandler;
        private bool isPaused;

        private void Start()
        {
            inputHandler = FindFirstObjectByType<PlayerInputHandler>();

            if (resumeButton != null) resumeButton.onClick.AddListener(OnResumeClicked);
            if (menuButton != null) menuButton.onClick.AddListener(OnMenuClicked);
            if (quitButton != null) quitButton.onClick.AddListener(OnQuitClicked);

            ThemeBinder.StyleScrim(panel);
            ThemeBinder.StyleButton(resumeButton, UITheme.Primary);
            ThemeBinder.StyleButton(menuButton, UITheme.Neutral);
            ThemeBinder.StyleButton(quitButton, UITheme.Danger);

            if (panel != null) panel.SetActive(false);
        }

        private void OnDestroy()
        {
            if (resumeButton != null) resumeButton.onClick.RemoveListener(OnResumeClicked);
            if (menuButton != null) menuButton.onClick.RemoveListener(OnMenuClicked);
            if (quitButton != null) quitButton.onClick.RemoveListener(OnQuitClicked);

            // Be polite — if we get destroyed while paused, restore timescale.
            if (isPaused) Time.timeScale = 1f;
        }

        private void Update()
        {
            if (inputHandler != null && inputHandler.PausePressed)
            {
                TogglePause();
            }
        }

        private void TogglePause()
        {
            isPaused = !isPaused;
            Time.timeScale = isPaused ? 0f : 1f;
            if (panel != null)
            {
                panel.SetActive(isPaused);
                if (isPaused) OverlayIntro.Play(panel);
            }
        }

        private void OnResumeClicked()
        {
            if (!isPaused) return;
            TogglePause();
        }

        private void OnMenuClicked()
        {
            Time.timeScale = 1f;
            isPaused = false;
            if (panel != null) panel.SetActive(false);

            if (GameManager.Instance == null)
            {
                Debug.LogWarning("GameManager not found in scene; using fallback behavior");
                return;
            }
            GameManager.Instance.ReturnToMenu();
        }

        private void OnQuitClicked()
        {
            Time.timeScale = 1f;
            if (GameManager.Instance == null)
            {
                Debug.LogWarning("GameManager not found in scene; using fallback behavior");
                Application.Quit();
                return;
            }
            GameManager.Instance.Quit();
        }
    }
}
