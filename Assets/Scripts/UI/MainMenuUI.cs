using UnityEngine;
using UnityEngine.UI;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace StumbleClone.UI
{
    /// Wires up the main menu buttons. Attach to a GameObject inside the MainMenu
    /// canvas and drag the Play/Quit buttons and the level-select panel into the
    /// serialized fields.
    public class MainMenuUI : MonoBehaviour
    {
        [SerializeField] private Button playButton;
        [SerializeField] private Button quitButton;
        [SerializeField] private GameObject levelSelectPanel;

        private void Awake()
        {
            if (playButton != null)
            {
                playButton.onClick.AddListener(OnPlayClicked);
            }
            else
            {
                Debug.LogWarning("MainMenuUI: playButton is not assigned.");
            }

            if (quitButton != null)
            {
                quitButton.onClick.AddListener(OnQuitClicked);
            }
            else
            {
                Debug.LogWarning("MainMenuUI: quitButton is not assigned.");
            }

            if (levelSelectPanel != null)
            {
                levelSelectPanel.SetActive(false);
            }

            ThemeBinder.StyleButton(playButton, UITheme.Primary);
            ThemeBinder.StyleButton(quitButton, UITheme.Neutral);
        }

        private void OnDestroy()
        {
            if (playButton != null) playButton.onClick.RemoveListener(OnPlayClicked);
            if (quitButton != null) quitButton.onClick.RemoveListener(OnQuitClicked);
        }

        private void OnPlayClicked()
        {
            if (levelSelectPanel == null)
            {
                Debug.LogWarning("MainMenuUI: levelSelectPanel is not assigned.");
                return;
            }
            levelSelectPanel.SetActive(true);
        }

        private void OnQuitClicked()
        {
#if UNITY_EDITOR
            EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }
    }
}
