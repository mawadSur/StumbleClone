using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UI;
using TMPro;
using StumbleClone.Core;
using StumbleClone.Game;

namespace StumbleClone.UI
{
    /// End-of-level screen for a LOSS (a human win defers to VictoryScreen). Activated by
    /// GameEvents.LevelEnded. Reads GameManager.Instance.lastResult to render a clean, themed
    /// results card built entirely in code via RuntimeUI: placement ('#N / 8' or 'Eliminated'),
    /// run time, mode, and the token-payout line. Offers one consistent button row of actions —
    /// Continue (advance), Menu (return), Leaderboard, Share.
    ///
    /// The serialized refs (panel + the old result/stats labels and Continue/Menu buttons) are
    /// kept for scene compatibility: the panel still hosts the card, the Continue/Menu listeners
    /// still fire the same handlers, but the old two-label layout is hidden so the card is the
    /// single source of truth (no more text from two layout systems stacking).
    public class EndScreenUI : MonoBehaviour
    {
        [SerializeField] private GameObject panel;
        [SerializeField] private TMP_Text resultText;
        [SerializeField] private TMP_Text statsText;
        [SerializeField] private Button continueButton;
        [SerializeField] private Button menuButton;

        private LevelMode lastMode = LevelMode.Race;
        private int lastRank;

        // ---- Code-built results card (parented to the serialized panel) ----------
        // Built once, lazily, so it works without a scene rebuild and never clashes with the
        // serialized widgets (which we hide). All labels/buttons live on this card.
        private bool _cardBuilt;
        private TMP_Text _newBestBadge;
        private TMP_Text _placementLabel;
        private TMP_Text _statsLabel;     // mode + run time
        private TMP_Text _tokensLabel;    // token payout (+N TOKENS) and doubler note

        private void OnEnable()
        {
            GameEvents.LevelEnded += HandleLevelEnded;
            GameEvents.LevelStarted += HandleLevelStarted;

            if (continueButton != null) continueButton.onClick.AddListener(OnContinueClicked);
            if (menuButton != null) menuButton.onClick.AddListener(OnMenuClicked);

            if (panel != null) panel.SetActive(false);
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

            BuildCard();

            int totalRacers = RacerRegistry.All.Count;
            if (totalRacers <= 0) totalRacers = 8;

            bool won = false;
            int rank = 0;
            float duration = 0f;
            int tokens = 0;
            bool doublerUsed = false;
            bool newBest = false;

            if (GameManager.Instance != null && GameManager.Instance.lastResult != null)
            {
                var res = GameManager.Instance.lastResult;
                won = res.playerWon;
                rank = res.playerRank;
                duration = res.duration;
                tokens = res.tokensAwarded;
                doublerUsed = res.doublerUsed;
                newBest = IsNewBest(lastMode, res.score);
            }
            else
            {
                Debug.LogWarning("GameManager not found in scene; using fallback behavior");
            }

            lastRank = rank;

            // ---- Placement headline ----
            if (_placementLabel != null)
            {
                if (won) _placementLabel.text = "YOU WON!";
                else if (rank > 0) _placementLabel.text = "#" + rank + " / " + totalRacers;
                else _placementLabel.text = "Eliminated";
            }

            // ---- Mode + run time ----
            if (_statsLabel != null)
            {
                _statsLabel.text = LevelProgress.DisplayName(lastMode) + "   •   " + FormatTime(duration);
            }

            // ---- Token payout (PRESERVED) ----
            // Reads the actual granted amount from the run result (rank-scaled consolation; the win
            // path is handled by VictoryScreen). Keeps the '+N TOKENS' line and the doubler note.
            if (_tokensLabel != null)
            {
                if (tokens > 0)
                {
                    string note = doublerUsed ? "  <size=70%>(2x DOUBLER)</size>" : "";
                    _tokensLabel.text = "+" + tokens + " TOKENS" + note;
                    _tokensLabel.gameObject.SetActive(true);
                }
                else
                {
                    _tokensLabel.text = "";
                    _tokensLabel.gameObject.SetActive(false);
                }
            }

            if (_newBestBadge != null) _newBestBadge.gameObject.SetActive(newBest);
        }

        /// Format a run duration as M:SS (or SS.s under a minute) for the results card.
        private static string FormatTime(float seconds)
        {
            if (seconds <= 0f) return "—";
            if (seconds < 60f) return seconds.ToString("0.0") + "s";
            int m = Mathf.FloorToInt(seconds / 60f);
            int s = Mathf.FloorToInt(seconds % 60f);
            return m + ":" + s.ToString("00");
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

        /// Builds the themed results card once, parented to the serialized panel: a NEW BEST badge,
        /// the placement headline, a mode + run-time line, the token-payout line, and ONE row of
        /// four actions (Continue / Menu / Leaderboard / Share). The serialized result/stats labels
        /// and Continue/Menu buttons are hidden so the card is the single visible layout. No-op if
        /// the panel is missing, or once already built.
        private void BuildCard()
        {
            if (panel == null || _cardBuilt) return;
            _cardBuilt = true;

            // Hide the legacy scene-wired widgets so we don't render two stacked layouts. Their
            // Continue/Menu click listeners stay attached (harmless while hidden); the card's own
            // buttons drive the same handlers.
            if (resultText != null) resultText.gameObject.SetActive(false);
            if (statsText != null) statsText.gameObject.SetActive(false);
            if (continueButton != null) continueButton.gameObject.SetActive(false);
            if (menuButton != null) menuButton.gameObject.SetActive(false);

            Transform p = panel.transform;

            // NEW BEST badge — top of the card.
            _newBestBadge = RuntimeUI.Label(p, "NEW BEST!", 52,
                new Vector2(0.5f, 0.5f), new Vector2(0f, 300f), new Vector2(700f, 80f));
            _newBestBadge.fontStyle = FontStyles.Bold;
            _newBestBadge.color = UITheme.Gold;
            _newBestBadge.gameObject.SetActive(false);

            // Placement headline — gold, large, centered.
            _placementLabel = RuntimeUI.Label(p, "Eliminated", 100,
                new Vector2(0.5f, 0.5f), new Vector2(0f, 170f), new Vector2(900f, 150f));
            _placementLabel.fontStyle = FontStyles.Bold;
            _placementLabel.color = UITheme.Gold;

            // Mode + run-time subtitle — muted.
            _statsLabel = RuntimeUI.Label(p, "", 40,
                new Vector2(0.5f, 0.5f), new Vector2(0f, 70f), new Vector2(900f, 60f));
            _statsLabel.color = UITheme.OnSurfaceMuted;
            _statsLabel.richText = true;

            // Token payout line — gold, its own band beneath the stats.
            _tokensLabel = RuntimeUI.Label(p, "", 46,
                new Vector2(0.5f, 0.5f), new Vector2(0f, -10f), new Vector2(900f, 64f));
            _tokensLabel.fontStyle = FontStyles.Bold;
            _tokensLabel.color = UITheme.Gold;
            _tokensLabel.richText = true;
            _tokensLabel.gameObject.SetActive(false);

            // ---- One consistent action row (Continue / Menu / Leaderboard / Share) ----
            // Four equal buttons centered on a single row beneath the card body.
            const float btnW = 300f;
            const float btnH = 84f;
            const float gap = 24f;
            float step = btnW + gap;
            float rowY = -200f;
            float startX = -1.5f * step; // centers four columns around x=0

            RuntimeUI.Button(p, "CONTINUE", UITheme.Primary,
                new Vector2(0.5f, 0.5f), new Vector2(startX + 0 * step, rowY),
                new Vector2(btnW, btnH), OnContinueClicked);
            RuntimeUI.Button(p, "MENU", UITheme.Neutral,
                new Vector2(0.5f, 0.5f), new Vector2(startX + 1 * step, rowY),
                new Vector2(btnW, btnH), OnMenuClicked);
            RuntimeUI.Button(p, "LEADERBOARD", UITheme.Secondary,
                new Vector2(0.5f, 0.5f), new Vector2(startX + 2 * step, rowY),
                new Vector2(btnW, btnH), OnLeaderboardClicked);
            RuntimeUI.Button(p, "SHARE", UITheme.Secondary,
                new Vector2(0.5f, 0.5f), new Vector2(startX + 3 * step, rowY),
                new Vector2(btnW, btnH), OnShareClicked);
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
            string text = $"I placed {place} in the {LevelProgress.DisplayName(mode)} in StumbleKids! Can you beat me?";
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
