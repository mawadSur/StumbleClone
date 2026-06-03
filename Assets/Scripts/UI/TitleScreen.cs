using System.Collections;
using StumbleClone.Audio;
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

            // Two prominent CTAs share the PLAY band (y=0.27): PLAY (pink, solo career) on the left,
            // MULTIPLAYER (gold, host/join by code) on the right. Keeping them on the same row leaves
            // the BOTS (0.39) and PERKS/LEADERBOARD (0.13) bands untouched. Below: PERKS + LEADERBOARD.
            RuntimeUI.Button(bg.transform, "PLAY", UITheme.Primary,
                new Vector2(0.5f, 0.27f), new Vector2(-235f, 0f), new Vector2(420f, 92f), OnPlay);
            RuntimeUI.Button(bg.transform, "MULTIPLAYER", UITheme.Accent,
                new Vector2(0.5f, 0.27f), new Vector2(235f, 0f), new Vector2(420f, 92f), OnMultiplayer);
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
                SkinStore.Current = _previewSkin; // equip — quiet acknowledgement
                AudioManager.Play(Sfx.UiClick);
            }
            else
            {
                int price = SkinInventory.PriceOf(_previewSkin);
                if (SkinInventory.TryBuy(_previewSkin))
                {
                    SkinStore.Current = _previewSkin; // bought — equip immediately
                    RefreshTokens();
                    string name = SkinCatalog.DisplayFor(_previewSkin).ToUpper();
                    CelebratePurchase(name + " UNLOCKED  -" + price);
                }
                else
                {
                    // Not affordable: make the failure visible instead of a silent no-op.
                    FlashLabelDenied(_buyLabel);
                }
            }
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

        // Open the host/join-by-code lobby. Commit the entered name first so the lobby roster and
        // every networked session use the up-to-date name (same contract as OnPlay).
        private void OnMultiplayer()
        {
            if (_nameInput != null) LeaderboardStore.SetPlayerName(_nameInput.text);
            MultiplayerUI.Open();
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
            int price = LevelProgress.PriceOf(mode);
            if (LevelProgress.TryUnlock(mode))
            {
                SetModeLabel(lbl, img, mode);
                RefreshTokens();
                if (tok != null) tok.text = "TOKENS: " + TokenWallet.Balance;
                CelebratePurchase(LevelProgress.DisplayName(mode).ToUpper() + " UNLOCKED  -" + price);
            }
            else
            {
                FlashLabelDenied(lbl); // unaffordable — show the failure
            }
        }

        private void OpenPerksPanel()
        {
            var modal = BuildModal("ABILITIES", out Transform card);

            // This modal carries more rows than the mode picker (4 perks + a doubler + a
            // POWER-UPS section of 3 rows + BACK), so grow the shared card taller and a touch
            // wider. The card is centre-pivoted; content is top-anchored (so the title stays
            // put) — we extend the bottom edge downward to make room. Widening also keeps the
            // longer power-up labels from crowding the price tag.
            var cardRect = card as RectTransform;
            if (cardRect != null)
            {
                cardRect.offsetMin = new Vector2(-400f, -560f); // wider + much taller bottom
                cardRect.offsetMax = new Vector2(400f, 400f);   // keep the top edge (title) fixed
            }

            var tok = RuntimeUI.Label(card, "TOKENS: " + TokenWallet.Balance, 30,
                new Vector2(0.5f, 1f), new Vector2(0f, -108f), new Vector2(660f, 40f));
            tok.color = UITheme.Gold;

            var rows = new System.Collections.Generic.List<(string id, TMP_Text lbl, Image img)>();
            TMP_Text doublerLabel = null;
            var powerupRows = new System.Collections.Generic.List<(string id, TMP_Text lbl)>();

            System.Action refresh = () =>
            {
                foreach (var r in rows) SetPerkLabel(r.lbl, r.img, r.id);
                if (doublerLabel != null) doublerLabel.text = DoublerText();
                foreach (var p in powerupRows) p.lbl.text = PowerupText(p.id);
                if (tok != null) tok.text = "TOKENS: " + TokenWallet.Balance;
                RefreshTokens();
            };

            // Rows are packed tighter (76px pitch, 68px tall) to fit everything; labels stay
            // at 26pt which reads fine on a phone.
            const float rowPitch = 76f;
            const float rowH = 68f;
            const float rowW = 700f;
            float y = -158f;
            for (int i = 0; i < AbilityStore.PerkCount; i++)
            {
                string id = AbilityStore.PerkIds[i]; // capture
                var btn = RuntimeUI.Button(card, "", UITheme.Secondary,
                    new Vector2(0.5f, 1f), new Vector2(0f, y), new Vector2(rowW, rowH), null);
                var lbl = btn.GetComponentInChildren<TMP_Text>();
                var img = btn.GetComponent<Image>();
                lbl.fontSize = 26f;
                rows.Add((id, lbl, img));
                var lblCapture = lbl; // capture for the failure flash
                btn.onClick.AddListener(() => { OnPerkClicked(id, lblCapture); refresh(); });
                y -= rowPitch;
            }

            // Consumable: Token Doubler.
            var dblBtn = RuntimeUI.Button(card, "", UITheme.Secondary,
                new Vector2(0.5f, 1f), new Vector2(0f, y), new Vector2(rowW, rowH), null);
            doublerLabel = dblBtn.GetComponentInChildren<TMP_Text>();
            doublerLabel.fontSize = 26f;
            var dblLabelCapture = doublerLabel; // capture for the failure flash
            dblBtn.onClick.AddListener(() =>
            {
                if (AbilityStore.BuyDoubler()) CelebratePurchase("TOKEN DOUBLER  -" + AbilityStore.DoublerPrice);
                else FlashLabelDenied(dblLabelCapture);
                refresh();
            });
            y -= rowPitch;

            // ---- POWER-UPS section: consumable charges spent at the next round start. ----
            var sectionLabel = RuntimeUI.Label(card, "POWER-UPS  (one charge per round)", 24,
                new Vector2(0.5f, 1f), new Vector2(0f, y - 4f), new Vector2(rowW, 34f));
            sectionLabel.fontStyle = FontStyles.Bold;
            sectionLabel.color = UITheme.Gold;
            y -= 44f;

            for (int i = 0; i < AbilityStore.PowerupCatalogCount; i++)
            {
                string id = AbilityStore.PowerupIds[i]; // capture
                var btn = RuntimeUI.Button(card, "", UITheme.Secondary,
                    new Vector2(0.5f, 1f), new Vector2(0f, y), new Vector2(rowW, rowH), null);
                var lbl = btn.GetComponentInChildren<TMP_Text>();
                lbl.fontSize = 26f;
                powerupRows.Add((id, lbl));
                var lblCapture = lbl; // capture for the failure flash
                btn.onClick.AddListener(() =>
                {
                    if (AbilityStore.BuyPowerup(id))
                        CelebratePurchase(AbilityStore.PowerupNames[AbilityStore.PowerupIndex(id)].ToUpper()
                            + "  -" + AbilityStore.PowerupPrice(id));
                    else
                        FlashLabelDenied(lblCapture);
                    refresh();
                });
                y -= rowPitch;
            }

            refresh();

            RuntimeUI.Button(card, "BACK", UITheme.Neutral,
                new Vector2(0.5f, 0f), new Vector2(0f, 38f), new Vector2(300f, 60f), () => Destroy(modal));
        }

        private static string DoublerText()
            => "TOKEN DOUBLER  x" + AbilityStore.DoublerCount + "   [BUY " + AbilityStore.DoublerPrice + "]";

        // "<Name>  x<count>   [BUY <price>]" — matches the doubler row's compact format.
        private static string PowerupText(string id)
        {
            int idx = AbilityStore.PowerupIndex(id);
            string name = idx >= 0 ? AbilityStore.PowerupNames[idx] : id;
            return name + "  x" + AbilityStore.PowerupCount(id) + "   [BUY " + AbilityStore.PowerupPrice(id) + "]";
        }

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

        private void OnPerkClicked(string id, TMP_Text label)
        {
            if (AbilityStore.IsPerkOwned(id))
            {
                AbilityStore.EquippedPerk = id;        // equip — quiet acknowledgement
                AudioManager.Play(Sfx.UiClick);
                return;
            }

            int price = AbilityStore.PerkPrice(AbilityStore.PerkIndex(id));
            if (AbilityStore.BuyPerk(id))              // buy then equip
            {
                AbilityStore.EquippedPerk = id;
                int idx = AbilityStore.PerkIndex(id);
                CelebratePurchase(AbilityStore.PerkNames[idx].ToUpper() + " UNLOCKED  -" + price);
            }
            else
            {
                FlashLabelDenied(label); // unaffordable — show the failure
            }
        }

        // ---- Purchase feedback helpers ------------------------------------------

        // Flash a gold confirmation toast under the TOKENS chip (reusing the daily-bonus toast
        // pattern from Start), play a click cue, and pulse the chip — the shared celebration for
        // every successful buy (skin, mode unlock, perk, doubler).
        //
        // The toast rides its own top-most overlay (sort 130, above the title=100 and any modal=120)
        // so it stays visible whether the purchase happened on the title row or inside an open
        // shop modal. The overlay is destroyed with the toast once it finishes fading.
        private void CelebratePurchase(string message)
        {
            AudioManager.Play(Sfx.UiClick);

            var toastOverlay = RuntimeUI.Overlay("PurchaseToastOverlay", 130);
            var toast = RuntimeUI.Label(toastOverlay.transform, message, 30,
                new Vector2(1f, 1f), new Vector2(-40f, -96f), new Vector2(560f, 48f), TextAlignmentOptions.Right);
            toast.color = UITheme.Gold;
            toast.fontStyle = FontStyles.Bold;
            toast.raycastTarget = false; // purely decorative — never swallow taps
            StartCoroutine(FadeAndDestroy(toast, toastOverlay, 1.6f));

            if (_tokenLabel != null) StartCoroutine(PulseChip(_tokenLabel.rectTransform));
        }

        // Briefly tint a price label red so an unaffordable tap reads as a denial instead of a
        // silent no-op. The label's text/colour are restored by the caller's Refresh* on the next
        // frame's interactions; we explicitly restore here so repeated taps keep working.
        private void FlashLabelDenied(TMP_Text label)
        {
            if (label == null) return;
            StartCoroutine(FlashRed(label));
        }

        private static IEnumerator FlashRed(TMP_Text label)
        {
            Color original = label.color;
            label.color = UITheme.Danger;
            float t = 0f;
            while (t < 0.35f)
            {
                t += Time.unscaledDeltaTime;
                yield return null;
            }
            if (label != null) label.color = original;
        }

        private IEnumerator PulseChip(RectTransform chip)
        {
            if (chip == null) yield break;
            Vector3 baseScale = Vector3.one;
            float t = 0f;
            const float dur = 0.28f;
            while (t < dur)
            {
                t += Time.unscaledDeltaTime;
                float k = Mathf.Sin(Mathf.Clamp01(t / dur) * Mathf.PI); // 0→1→0 bump
                chip.localScale = baseScale * (1f + 0.22f * k);
                yield return null;
            }
            if (chip != null) chip.localScale = baseScale;
        }

        private IEnumerator FadeAndDestroy(TMP_Text toast, GameObject overlay, float hold)
        {
            if (toast == null) { if (overlay != null) Destroy(overlay); yield break; }
            float t = 0f;
            while (t < hold && toast != null)
            {
                t += Time.unscaledDeltaTime;
                yield return null;
            }
            float f = 0f;
            Color start = toast != null ? toast.color : Color.clear;
            while (f < 0.4f && toast != null)
            {
                f += Time.unscaledDeltaTime;
                Color c = start; c.a = Mathf.Lerp(start.a, 0f, f / 0.4f);
                toast.color = c;
                yield return null;
            }
            if (overlay != null) Destroy(overlay); // tears down the toast label with it
        }
    }
}
