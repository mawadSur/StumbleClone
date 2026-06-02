using UnityEngine;
using UnityEngine.Networking;
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

        // Runtime-built extras (not scene-wired) so they work without a scene rebuild and
        // never clash with the serialized Continue/Menu buttons.
        private TMP_Text newBestBadge;
        private Button leaderboardButton;
        private Button shareButton;
        private int lastRank;

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

            EnsureExtras();

            int totalRacers = RacerRegistry.All.Count;
            if (totalRacers <= 0) totalRacers = 8;

            bool won = false;
            int rank = 0;
            bool newBest = false;

            if (GameManager.Instance != null && GameManager.Instance.lastResult != null)
            {
                won = GameManager.Instance.lastResult.playerWon;
                rank = GameManager.Instance.lastResult.playerRank;
                newBest = IsNewBest(lastMode, GameManager.Instance.lastResult.score);
            }
            else
            {
                Debug.LogWarning("GameManager not found in scene; using fallback behavior");
            }

            lastRank = rank;

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

            if (newBestBadge != null) newBestBadge.gameObject.SetActive(newBest);
        }

        /// True if the just-finished run is the player's best stored score for this mode.
        /// GameManager submits the run's entry (in its own Awake-registered LevelEnded handler)
        /// BEFORE this UI handler runs, so the entry is already in the store. The run is therefore
        /// the new best exactly when its score sits at (or tied at) the top of the player's own
        /// entries for the mode — i.e. nothing of theirs scores higher.
        private static bool IsNewBest(LevelMode mode, float runScore)
        {
            string me = LeaderboardStore.GetPlayerName();
            float playerBest = float.MinValue;
            // GetTop returns up to n entries sorted high-to-low; 50 == the per-mode cap, so this
            // covers every retained entry for the player.
            var top = LeaderboardStore.GetTop(mode, 50);
            for (int i = 0; i < top.Count; i++)
            {
                var e = top[i];
                if (e != null && e.playerName == me) { playerBest = e.score; break; }
            }
            return playerBest == float.MinValue || runScore >= playerBest;
        }

        /// Lazily builds the NEW BEST badge plus the View Leaderboard and Share buttons as
        /// children of the serialized panel. Built once; positioned to avoid the existing
        /// Continue (y=-40) and Menu (y=-150) buttons. No-op if the panel is missing.
        private void EnsureExtras()
        {
            if (panel == null || newBestBadge != null) return;

            // Gold badge above the result text (result sits at y=+200 on a centered panel).
            newBestBadge = RuntimeUI.Label(panel.transform, "NEW BEST!", 56,
                new Vector2(0.5f, 0.5f), new Vector2(0f, 330f), new Vector2(700f, 90f));
            newBestBadge.fontStyle = FontStyles.Bold;
            newBestBadge.color = UITheme.Gold;
            newBestBadge.gameObject.SetActive(false);

            // Secondary actions on a row below the Main Menu button (y=-150), side by side.
            leaderboardButton = RuntimeUI.Button(panel.transform, "VIEW LEADERBOARD", UITheme.Secondary,
                new Vector2(0.5f, 0.5f), new Vector2(-200f, -255f), new Vector2(360f, 80f), OnLeaderboardClicked);
            shareButton = RuntimeUI.Button(panel.transform, "SHARE", UITheme.Secondary,
                new Vector2(0.5f, 0.5f), new Vector2(200f, -255f), new Vector2(360f, 80f), OnShareClicked);
        }

        private void OnLeaderboardClicked()
        {
            if (LeaderboardUI.Instance != null) LeaderboardUI.Instance.Open();
            else Debug.LogWarning("LeaderboardUI.Instance not available (only self-instantiates on MainMenu)");
        }

        private void OnShareClicked()
        {
            Application.OpenURL(BuildShareIntent(lastMode, lastRank));
        }

        // Game URL the share links back to (the deployed WebGL build).
        private const string ShareUrl = "https://stumbleclone.vercel.app";

        /// Builds an X/Twitter web-intent URL pre-filled with a short brag about this run.
        /// OpenURL on this works on WebGL (opens a new tab) and mobile (opens the app/browser),
        /// so it stays cross-platform without per-platform native share plugins.
        private static string BuildShareIntent(LevelMode mode, int rank)
        {
            string place = rank > 0 ? "#" + rank : "the finish";
            string text = $"I placed {place} in the {mode} in StumbleKids! Can you beat me?";
            return "https://twitter.com/intent/tweet?text="
                   + UnityWebRequest.EscapeURL(text)
                   + "&url=" + UnityWebRequest.EscapeURL(ShareUrl);
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
