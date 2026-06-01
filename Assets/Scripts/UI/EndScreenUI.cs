using UnityEngine;
using UnityEngine.UI;
using TMPro;
using StumbleClone.Core;
using StumbleClone.Game;

namespace StumbleClone.UI
{
    /// End-of-level screen. Activated by GameEvents.LevelEnded. Reads
    /// GameManager.Instance.lastResult to render the result text and offers
    /// Continue (advance to next mode) and Menu (return to main menu) buttons.
    public class EndScreenUI : MonoBehaviour
    {
        [SerializeField] private GameObject panel;
        [SerializeField] private TMP_Text resultText;
        [SerializeField] private TMP_Text statsText;
        [SerializeField] private Button continueButton;
        [SerializeField] private Button menuButton;

        private LevelMode lastMode = LevelMode.Race;

        private void OnEnable()
        {
            GameEvents.LevelEnded += HandleLevelEnded;
            GameEvents.LevelStarted += HandleLevelStarted;

            if (continueButton != null) continueButton.onClick.AddListener(OnContinueClicked);
            if (menuButton != null) menuButton.onClick.AddListener(OnMenuClicked);

            ApplyTheme();

            if (panel != null) panel.SetActive(false);
        }

        private void ApplyTheme()
        {
            ThemeBinder.StyleScrim(panel);
            ThemeBinder.StyleText(resultText, UITheme.Gold);
            ThemeBinder.StyleText(statsText, UITheme.OnSurfaceMuted);
            ThemeBinder.StyleButton(continueButton, UITheme.Primary);
            ThemeBinder.StyleButton(menuButton, UITheme.Neutral);
        }

        private void OnDisable()
        {
            GameEvents.LevelEnded -= HandleLevelEnded;
            GameEvents.LevelStarted -= HandleLevelStarted;

            if (continueButton != null) continueButton.onClick.RemoveListener(OnContinueClicked);
            if (menuButton != null) menuButton.onClick.RemoveListener(OnMenuClicked);
        }

        private void HandleLevelStarted(LevelMode mode)
        {
            lastMode = mode;
            if (panel != null) panel.SetActive(false);
        }

        private void HandleLevelEnded(IRacer winner)
        {
            // On a human win, VictoryScreen takes over (dance + leaderboard) — stay hidden.
            if (winner != null && winner.IsPlayer)
            {
                if (panel != null) panel.SetActive(false);
                return;
            }

            if (panel != null) { panel.SetActive(true); OverlayIntro.Play(panel); }

            int totalRacers = RacerRegistry.All.Count;
            if (totalRacers <= 0) totalRacers = 8;

            bool won = false;
            int rank = 0;

            if (GameManager.Instance != null && GameManager.Instance.lastResult != null)
            {
                won = GameManager.Instance.lastResult.playerWon;
                rank = GameManager.Instance.lastResult.playerRank;
            }
            else
            {
                Debug.LogWarning("GameManager not found in scene; using fallback behavior");
            }

            if (resultText != null)
            {
                if (won) resultText.text = "YOU WON!";
                else if (rank > 0) resultText.text = "You finished #" + rank + " / " + totalRacers;
                else resultText.text = "Eliminated";
            }

            if (statsText != null)
            {
                statsText.text = "Mode: " + lastMode;
            }
        }

        private void OnContinueClicked()
        {
            if (GameManager.Instance == null)
            {
                Debug.LogWarning("GameManager not found in scene; using fallback behavior");
                return;
            }

            switch (lastMode)
            {
                case LevelMode.Race:
                    GameManager.Instance.LoadLevel(LevelMode.Survival);
                    break;
                case LevelMode.Survival:
                    GameManager.Instance.LoadLevel(LevelMode.LastStanding);
                    break;
                case LevelMode.LastStanding:
                default:
                    GameManager.Instance.ReturnToMenu();
                    break;
            }
        }

        private void OnMenuClicked()
        {
            if (GameManager.Instance == null)
            {
                Debug.LogWarning("GameManager not found in scene; using fallback behavior");
                return;
            }
            GameManager.Instance.ReturnToMenu();
        }
    }
}
