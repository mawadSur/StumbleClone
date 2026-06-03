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

            // PLAY opens the mode picker (Knockout free; Race/Survival are token-unlocks).
            // Single primary CTA (pink). Below it: PERKS shop + LEADERBOARD share a row.
            RuntimeUI.Button(bg.transform, "PLAY", UITheme.Primary,
                new Vector2(0.5f, 0.27f), Vector2.zero, new Vector2(440f, 92f), OnPlay);
            RuntimeUI.Button(bg.transform, "PERKS", UITheme.Secondary,
                new Vector2(0.5f, 0.13f), new Vector2(-125f, 0f), new Vector2(290f, 66f), OpenPerksPanel);
            RuntimeUI.Button(bg.transform, "LEADERBOARD", UITheme.Secondary,
                new Vector2(0.5f, 0.13f), new Vector2(125f, 0f), new Vector2(290f, 66f), OnLeaderboard);

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
            OpenModePanel();
        }

        private void OnLeaderboard()
        {
            if (LeaderboardUI.Instance != null) LeaderboardUI.Instance.Open();
        }

        // ---- Modal panels (mode select, perks shop) -----------------------------

        // A centred dimmed card above the title. Returns the modal root (destroy to close) and the
        // card transform to parent content into.
        private GameObject BuildModal(string titleText, out Transform card)
        {
            var modal = RuntimeUI.Overlay("ModalOverlay", 120);
            RuntimeUI.Panel(modal.transform, "Dim", new Color(0f, 0f, 0f, 0.72f),
                Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            var cardImg = RuntimeUI.Panel(modal.transform, "Card", UITheme.SurfaceDeep,
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(-380f, -400f), new Vector2(380f, 400f));
            var title = RuntimeUI.Label(cardImg.transform, titleText, 56,
                new Vector2(0.5f, 1f), new Vector2(0f, -28f), new Vector2(700f, 80f));
            title.fontStyle = FontStyles.Bold;
            title.color = UITheme.Gold;
            card = cardImg.transform;
            return modal;
        }

        private void OpenModePanel()
        {
            var modal = BuildModal("SELECT MODE", out Transform card);

            var tok = RuntimeUI.Label(card, "TOKENS: " + TokenWallet.Balance, 30,
                new Vector2(0.5f, 1f), new Vector2(0f, -112f), new Vector2(620f, 40f));
            tok.color = UITheme.Gold;

            LevelMode[] modes = { LevelMode.LastStanding, LevelMode.Race, LevelMode.Survival };
            float y = -180f;
            foreach (var modeItem in modes)
            {
                LevelMode mode = modeItem; // capture per iteration
                var btn = RuntimeUI.Button(card, "", UITheme.Secondary,
                    new Vector2(0.5f, 1f), new Vector2(0f, y), new Vector2(640f, 78f), null);
                var lbl = btn.GetComponentInChildren<TMP_Text>();
                var img = btn.GetComponent<Image>();
                SetModeLabel(lbl, img, mode);
                btn.onClick.AddListener(() => OnModeChosen(mode, lbl, img, tok, modal));
                y -= 98f;
            }

            RuntimeUI.Button(card, "BACK", UITheme.Neutral,
                new Vector2(0.5f, 0f), new Vector2(0f, 42f), new Vector2(300f, 64f), () => Destroy(modal));
        }

        private void SetModeLabel(TMP_Text lbl, Image img, LevelMode mode)
        {
            bool unlocked = LevelProgress.IsUnlocked(mode);
            string name = LevelProgress.DisplayName(mode).ToUpper();
            if (lbl != null)
                lbl.text = unlocked ? ("PLAY " + name) : (name + "   -   UNLOCK " + LevelProgress.PriceOf(mode));
            if (img != null)
                img.color = unlocked ? UITheme.Primary
                    : (TokenWallet.CanAfford(LevelProgress.PriceOf(mode)) ? UITheme.Secondary : UITheme.Neutral);
        }

        private void OnModeChosen(LevelMode mode, TMP_Text lbl, Image img, TMP_Text tok, GameObject modal)
        {
            if (LevelProgress.IsUnlocked(mode))
            {
                if (GameManager.Instance != null) { Destroy(modal); GameManager.Instance.LoadLevel(mode); }
                else if (_overlay != null) { Destroy(modal); _overlay.SetActive(false); } // editor-direct fallback
                return;
            }

            // Locked: try to buy the unlock; on success the button flips to PLAY.
            if (LevelProgress.TryUnlock(mode))
            {
                SetModeLabel(lbl, img, mode);
                RefreshTokens();
                if (tok != null) tok.text = "TOKENS: " + TokenWallet.Balance;
            }
        }

        private void OpenPerksPanel()
        {
            var modal = BuildModal("ABILITIES", out Transform card);

            var tok = RuntimeUI.Label(card, "TOKENS: " + TokenWallet.Balance, 30,
                new Vector2(0.5f, 1f), new Vector2(0f, -112f), new Vector2(620f, 40f));
            tok.color = UITheme.Gold;

            var rows = new System.Collections.Generic.List<(string id, TMP_Text lbl, Image img)>();
            TMP_Text doublerLabel = null;

            System.Action refresh = () =>
            {
                foreach (var r in rows) SetPerkLabel(r.lbl, r.img, r.id);
                if (doublerLabel != null) doublerLabel.text = DoublerText();
                if (tok != null) tok.text = "TOKENS: " + TokenWallet.Balance;
                RefreshTokens();
            };

            float y = -168f;
            for (int i = 0; i < AbilityStore.PerkCount; i++)
            {
                string id = AbilityStore.PerkIds[i]; // capture
                var btn = RuntimeUI.Button(card, "", UITheme.Secondary,
                    new Vector2(0.5f, 1f), new Vector2(0f, y), new Vector2(660f, 70f), null);
                var lbl = btn.GetComponentInChildren<TMP_Text>();
                var img = btn.GetComponent<Image>();
                lbl.fontSize = 26f;
                rows.Add((id, lbl, img));
                btn.onClick.AddListener(() => { OnPerkClicked(id); refresh(); });
                y -= 84f;
            }

            // Consumable: Token Doubler.
            var dblBtn = RuntimeUI.Button(card, "", UITheme.Secondary,
                new Vector2(0.5f, 1f), new Vector2(0f, y - 8f), new Vector2(660f, 70f), null);
            doublerLabel = dblBtn.GetComponentInChildren<TMP_Text>();
            doublerLabel.fontSize = 26f;
            dblBtn.onClick.AddListener(() => { AbilityStore.BuyDoubler(); refresh(); });

            refresh();

            RuntimeUI.Button(card, "BACK", UITheme.Neutral,
                new Vector2(0.5f, 0f), new Vector2(0f, 42f), new Vector2(300f, 64f), () => Destroy(modal));
        }

        private static string DoublerText()
            => "TOKEN DOUBLER  x" + AbilityStore.DoublerCount + "   [BUY " + AbilityStore.DoublerPrice + "]";

        private void SetPerkLabel(TMP_Text lbl, Image img, string id)
        {
            int idx = AbilityStore.PerkIndex(id);
            bool owned = AbilityStore.IsPerkOwned(id);
            bool equipped = AbilityStore.EquippedPerk == id;
            string state = !owned ? ("UNLOCK " + AbilityStore.PerkPrice(idx)) : (equipped ? "EQUIPPED" : "EQUIP");
            if (lbl != null) lbl.text = AbilityStore.PerkNames[idx] + " - " + AbilityStore.PerkDesc[idx] + "   [" + state + "]";
            if (img != null)
                img.color = equipped ? UITheme.Primary
                    : (!owned && !TokenWallet.CanAfford(AbilityStore.PerkPrice(idx)) ? UITheme.Neutral : UITheme.Secondary);
        }

        private void OnPerkClicked(string id)
        {
            if (AbilityStore.IsPerkOwned(id)) AbilityStore.EquippedPerk = id;        // equip
            else if (AbilityStore.BuyPerk(id)) AbilityStore.EquippedPerk = id;       // buy then equip
        }
    }
}
