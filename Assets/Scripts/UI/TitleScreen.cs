using StumbleClone.Core;
using StumbleClone.Game;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace StumbleClone.UI
{
    /// A real title gate shown over the MainMenu: branded title, name entry, and a START button
    /// that saves the player's name (for the leaderboard) and reveals the menu beneath. Self-
    /// instantiates whenever MainMenu loads — no scene wiring, no rebuild required.
    public sealed class TitleScreen : MonoBehaviour
    {
        private const string MenuScene = "MainMenu";
        private TMP_InputField _nameInput;
        private TMP_Text _difficultyLabel;
        private TMP_Text _skinLabel;
        private TMP_Text _buyLabel;
        private Image _buyImage;
        private TMP_Text _tokenLabel;
        private string _previewSkin; // the skin shown in the shop row (may differ from the equipped one)
        private GameObject _overlay;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            SceneManager.sceneLoaded += OnSceneLoaded;
            if (SceneManager.GetActiveScene().name == MenuScene) Create();
        }

        private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (scene.name == MenuScene) Create();
        }

        private static void Create()
        {
            if (FindFirstObjectByType<TitleScreen>() != null) return;
            new GameObject("TitleScreen").AddComponent<TitleScreen>();
        }

        private void Start()
        {
            _overlay = RuntimeUI.Overlay("TitleOverlay", 100);
            var bg = RuntimeUI.Panel(_overlay.transform, "Bg", UITheme.SurfaceDeep,
                Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);

            var title = RuntimeUI.Label(bg.transform, "STUMBLE KIDS", 120,
                new Vector2(0.5f, 0.82f), Vector2.zero, new Vector2(1500f, 200f));
            title.fontStyle = FontStyles.Bold;
            title.color = UITheme.Gold;

            RuntimeUI.Label(bg.transform, "Knockout party arena — last one standing wins", 38,
                new Vector2(0.5f, 0.71f), Vector2.zero, new Vector2(1400f, 80f));

            // Name entry: label sits just above its input box. (Anchors below are tuned so the
            // input, SKIN, BOTS, PLAY and LEADERBOARD bands never overlap at the 1080 reference.)
            RuntimeUI.Label(bg.transform, "YOUR NAME", 28,
                new Vector2(0.5f, 0.63f), Vector2.zero, new Vector2(600f, 46f));
            _nameInput = RuntimeUI.InputField(bg.transform, "Player", LeaderboardStore.GetPlayerName(),
                new Vector2(0.5f, 0.63f), new Vector2(0f, -56f), new Vector2(470f, 64f));

            // Token balance chip, top-right — earned by winning rounds, spent in the shop below.
            _tokenLabel = RuntimeUI.Label(bg.transform, "", 40,
                new Vector2(1f, 1f), new Vector2(-40f, -34f), new Vector2(440f, 60f), TextAlignmentOptions.Right);
            _tokenLabel.fontStyle = FontStyles.Bold;
            _tokenLabel.color = UITheme.Gold;

            // Skin shop row: the SKIN button (left) cycles a preview through the catalog; the
            // BUY/EQUIP button (right) acts on the previewed skin — buying it if locked, or equipping
            // it if owned. Owning is persisted (SkinInventory); equipping writes SkinStore.
            _previewSkin = SkinStore.Current;
            var skinBtn = RuntimeUI.Button(bg.transform, "", UITheme.Secondary,
                new Vector2(0.5f, 0.475f), new Vector2(-120f, 0f), new Vector2(380f, 64f), OnCycleSkin);
            _skinLabel = skinBtn.GetComponentInChildren<TMP_Text>();

            var buyBtn = RuntimeUI.Button(bg.transform, "", UITheme.Secondary,
                new Vector2(0.5f, 0.475f), new Vector2(215f, 0f), new Vector2(210f, 64f), OnBuyOrEquip);
            _buyLabel = buyBtn.GetComponentInChildren<TMP_Text>();
            _buyImage = buyBtn.GetComponent<Image>();

            RefreshTokens();
            RefreshSkinRow();

            // Daily login reward — the "come back tomorrow" hook. Claimed once per UTC day on the
            // first menu visit; pays into the token wallet and flashes a toast under the chip.
            if (DailyRewardStore.RewardAvailable)
            {
                int amount = DailyRewardStore.TryClaim(out int streak);
                if (amount > 0)
                {
                    RefreshTokens();
                    var toast = RuntimeUI.Label(bg.transform, $"DAILY BONUS  +{amount}   (Day {streak} streak!)", 30,
                        new Vector2(1f, 1f), new Vector2(-40f, -96f), new Vector2(560f, 48f), TextAlignmentOptions.Right);
                    toast.color = UITheme.Gold;
                    toast.fontStyle = FontStyles.Bold;
                }
            }

            // Bot difficulty — tap to cycle Easy / Normal / Hard. Persisted for every round.
            var diffBtn = RuntimeUI.Button(bg.transform, "BOTS: " + BotDifficulty.Label,
                UITheme.Neutral,
                new Vector2(0.5f, 0.39f), Vector2.zero, new Vector2(460f, 64f), OnCycleDifficulty);
            _difficultyLabel = diffBtn.GetComponentInChildren<TMP_Text>();

            // PLAY drops straight into the deathmatch (the focused mode) — no second menu.
            // Single primary CTA (pink); leaderboard is the subordinate secondary action (purple).
            RuntimeUI.Button(bg.transform, "PLAY", UITheme.Primary,
                new Vector2(0.5f, 0.27f), Vector2.zero, new Vector2(440f, 92f), OnPlay);
            RuntimeUI.Button(bg.transform, "LEADERBOARD", UITheme.Secondary,
                new Vector2(0.5f, 0.13f), Vector2.zero, new Vector2(440f, 66f), OnLeaderboard);

            OverlayIntro.Play(_overlay);
        }

        private void OnCycleDifficulty()
        {
            BotDifficulty.Cycle();
            if (_difficultyLabel != null) _difficultyLabel.text = "BOTS: " + BotDifficulty.Label;
        }

        // Advance the previewed skin only — equipping/buying is the BUY/EQUIP button's job.
        private void OnCycleSkin()
        {
            _previewSkin = SkinCatalog.Next(_previewSkin);
            RefreshSkinRow();
        }

        // Act on the previewed skin: equip it if owned, otherwise buy it (and equip on success).
        private void OnBuyOrEquip()
        {
            if (SkinInventory.IsOwned(_previewSkin))
            {
                SkinStore.Current = _previewSkin; // equip
            }
            else if (SkinInventory.TryBuy(_previewSkin))
            {
                SkinStore.Current = _previewSkin; // bought — equip immediately
                RefreshTokens();
            }
            // Not owned and not affordable: no-op (the label already shows the price).
            RefreshSkinRow();
        }

        private void RefreshTokens()
        {
            if (_tokenLabel != null) _tokenLabel.text = "TOKENS: " + TokenWallet.Balance;
        }

        private void RefreshSkinRow()
        {
            string id = _previewSkin;
            bool owned = SkinInventory.IsOwned(id);
            bool equipped = owned && SkinStore.Current == id;
            string name = SkinCatalog.DisplayFor(id);

            if (_skinLabel != null)
                _skinLabel.text = "SKIN: " + name + (owned ? (equipped ? "  - ON" : "") : "  - LOCKED");

            if (_buyLabel != null)
            {
                if (!owned) _buyLabel.text = "BUY " + SkinInventory.PriceOf(id);
                else if (equipped) _buyLabel.text = "EQUIPPED";
                else _buyLabel.text = "EQUIP";
            }

            if (_buyImage != null)
            {
                bool actionable = !owned ? TokenWallet.CanAfford(SkinInventory.PriceOf(id)) : !equipped;
                _buyImage.color = actionable ? UITheme.Secondary : UITheme.Neutral;
            }
        }

        private void OnPlay()
        {
            if (_nameInput != null) LeaderboardStore.SetPlayerName(_nameInput.text);
            if (GameManager.Instance != null) GameManager.Instance.LoadLevel(LevelMode.LastStanding);
            else if (_overlay != null) _overlay.SetActive(false); // editor-direct fallback
        }

        private void OnLeaderboard()
        {
            if (LeaderboardUI.Instance != null) LeaderboardUI.Instance.Open();
        }
    }
}
