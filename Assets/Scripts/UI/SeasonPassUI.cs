using System.Collections.Generic;
using StumbleClone.Game;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace StumbleClone.UI
{
    /// The Season Pass modal: a dimmed card (sortingOrder ~125, above the title=100 / shop modal=120)
    /// showing the season's tier track — each reached tier offers a FREE and a PREMIUM claim button —
    /// plus a QUESTS section with progress bars and per-quest claim. Opened from the title screen via
    /// <see cref="Open"/> (the lead wires the button); rebuilds itself live on SeasonPass/QuestSystem
    /// change events so claims update in place. Built entirely in code via RuntimeUI + UITheme so it
    /// needs no scene wiring and reads on a phone.
    ///
    /// Local-only today (SeasonPass/QuestSystem are PlayerPrefs-backed; premium is a stubbed bool that
    /// stays false until a future IAP unlock). No real IAP here.
    public sealed class SeasonPassUI : MonoBehaviour
    {
        private const int SortOrder = 125;

        // Layout constants (1920x1080 reference; CanvasScaler handles phones).
        private const float TierRowPitch = 96f;
        private const float TierRowH = 84f;
        private const float QuestRowPitch = 116f;
        private const float QuestRowH = 100f;

        private GameObject _overlay;
        private RectTransform _tierContent;   // scroll content for the tier track
        private RectTransform _questContent;   // scroll content for the quests

        /// Open (or re-focus) the season pass modal over whatever screen is showing.
        public static void Open()
        {
            var existing = FindAnyObjectByType<SeasonPassUI>();
            if (existing != null) { existing.transform.SetAsLastSibling(); return; }
            new GameObject("SeasonPassUI").AddComponent<SeasonPassUI>();
        }

        private void Start()
        {
            BuildChrome();
            Rebuild();

            SeasonPass.Changed += Rebuild;
            QuestSystem.Changed += Rebuild;
            OverlayIntro.Play(_overlay);
        }

        private void OnDestroy()
        {
            SeasonPass.Changed -= Rebuild;
            QuestSystem.Changed -= Rebuild;
        }

        private void Close() => Destroy(gameObject);

        // ---- static chrome (built once) -----------------------------------------

        private void BuildChrome()
        {
            _overlay = RuntimeUI.Overlay("SeasonPassOverlay", SortOrder);

            // Tapping the dim backdrop closes the modal (tap-outside-to-dismiss).
            var dim = RuntimeUI.Panel(_overlay.transform, "Dim", new Color(0f, 0f, 0f, 0.72f),
                Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            var dimBtn = dim.gameObject.AddComponent<Button>();
            dimBtn.transition = Selectable.Transition.None;
            dimBtn.onClick.AddListener(Close);

            // A wide card — the pass needs two columns of room. Pivot-centred; sized via offsets.
            var card = RuntimeUI.Panel(_overlay.transform, "Card", UITheme.SurfaceDeep,
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(-560f, -460f), new Vector2(560f, 460f));

            var title = RuntimeUI.Label(card.transform, "SEASON " + SeasonPass.SeasonNumber + " PASS", 56,
                new Vector2(0.5f, 1f), new Vector2(0f, -30f), new Vector2(900f, 80f));
            title.fontStyle = FontStyles.Bold;
            title.color = UITheme.Gold;

            // Two scroll columns side by side: TIERS (left) and QUESTS (right).
            _tierContent = BuildColumn(card.transform, "TIERS", -270f, 480f);
            _questContent = BuildColumn(card.transform, "QUESTS", 270f, 480f);

            RuntimeUI.Button(card.transform, "CLOSE", UITheme.Neutral,
                new Vector2(0.5f, 0f), new Vector2(0f, 40f), new Vector2(320f, 64f), Close);
        }

        // A titled, vertically-scrolling column. Returns the content RectTransform to fill with rows;
        // its height is set by the caller after rows are laid out. Uses a RectMask2D viewport + a
        // ScrollRect so long tier/quest lists scroll on a phone.
        private RectTransform BuildColumn(Transform card, string header, float xCenter, float width)
        {
            var headerLbl = RuntimeUI.Label(card, header, 30,
                new Vector2(0.5f, 1f), new Vector2(xCenter, -112f), new Vector2(width, 40f));
            headerLbl.fontStyle = FontStyles.Bold;
            headerLbl.color = UITheme.OnSurfaceMuted;

            // Viewport: anchored under the header, stretching down to just above the CLOSE button.
            var viewport = new GameObject("Viewport", typeof(RectTransform));
            var vpRt = (RectTransform)viewport.transform;
            vpRt.SetParent(card, false);
            vpRt.anchorMin = new Vector2(0.5f, 0f);
            vpRt.anchorMax = new Vector2(0.5f, 1f);
            vpRt.pivot = new Vector2(0.5f, 1f);
            vpRt.sizeDelta = new Vector2(width, 0f);
            vpRt.anchoredPosition = new Vector2(xCenter, 0f);
            vpRt.offsetMin = new Vector2(vpRt.offsetMin.x, 120f); // leave room for CLOSE
            vpRt.offsetMax = new Vector2(vpRt.offsetMax.x, -140f); // below the header
            viewport.AddComponent<Image>().color = new Color(0f, 0f, 0f, 0.18f);
            viewport.AddComponent<RectMask2D>();

            var content = new GameObject("Content", typeof(RectTransform));
            var contentRt = (RectTransform)content.transform;
            contentRt.SetParent(vpRt, false);
            contentRt.anchorMin = new Vector2(0f, 1f);
            contentRt.anchorMax = new Vector2(1f, 1f);
            contentRt.pivot = new Vector2(0.5f, 1f);
            contentRt.offsetMin = new Vector2(0f, contentRt.offsetMin.y);
            contentRt.offsetMax = new Vector2(0f, contentRt.offsetMax.y);

            var scroll = viewport.AddComponent<ScrollRect>();
            scroll.content = contentRt;
            scroll.viewport = vpRt;
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 24f;

            return contentRt;
        }

        // ---- dynamic content (rebuilt on every change) --------------------------

        private void Rebuild()
        {
            if (_tierContent == null || _questContent == null) return;
            RebuildTiers();
            RebuildQuests();
        }

        private void RebuildTiers()
        {
            ClearChildren(_tierContent);

            // XP / tier summary header row.
            int tierNum = SeasonPass.CurrentTier + 1;
            var summary = RuntimeUI.Label(_tierContent, $"TIER {tierNum} / {SeasonPass.TierCount}   -   {SeasonPass.XpIntoTier}/{SeasonPass.XpPerTier} XP", 26,
                new Vector2(0.5f, 1f), new Vector2(0f, -10f), new Vector2(460f, 36f));
            summary.color = UITheme.Gold;
            BuildBar(_tierContent, new Vector2(0f, -52f), 460f, SeasonPass.TierProgress01, UITheme.Accent);

            if (!SeasonPass.OwnsPremium)
            {
                var locked = RuntimeUI.Label(_tierContent, "PREMIUM track locked (coming soon)", 22,
                    new Vector2(0.5f, 1f), new Vector2(0f, -78f), new Vector2(460f, 30f));
                locked.color = UITheme.OnSurfaceMuted;
            }

            float y = -120f;
            // Show every tier; reached tiers are interactive, future tiers are previews.
            for (int t = 0; t < SeasonPass.TierCount; t++)
            {
                BuildTierRow(t, y);
                y -= TierRowPitch;
            }
            SetContentHeight(_tierContent, 120f + SeasonPass.TierCount * TierRowPitch + 20f);
        }

        private void BuildTierRow(int tier, float y)
        {
            bool reached = tier <= SeasonPass.CurrentTier;

            var row = RuntimeUI.Panel(_tierContent, "Tier" + tier,
                reached ? UITheme.Surface : new Color(0.10f, 0.12f, 0.18f, 0.7f),
                new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                new Vector2(-230f, y - TierRowH), new Vector2(230f, y));
            row.sprite = UITheme.RoundedSprite();
            row.type = Image.Type.Sliced;

            RuntimeUI.Label(row.transform, "T" + (tier + 1), 26,
                new Vector2(0f, 0.5f), new Vector2(16f, 0f), new Vector2(70f, TierRowH),
                TextAlignmentOptions.Left).fontStyle = FontStyles.Bold;

            BuildTrackButton(row.transform, tier, premium: false, new Vector2(0.5f, 0.5f), new Vector2(-30f, 0f));
            BuildTrackButton(row.transform, tier, premium: true, new Vector2(0.5f, 0.5f), new Vector2(135f, 0f));
        }

        // A compact claim button for one tier/track. Label shows the reward; colour + interactable
        // reflect claimable / claimed / locked / not-yet-reached.
        private void BuildTrackButton(Transform parent, int tier, bool premium, Vector2 anchor, Vector2 pos)
        {
            int tokens = premium ? SeasonPass.PremiumTokenReward(tier) : SeasonPass.FreeTokenReward(tier);
            string unlock = premium ? SeasonPass.PremiumUnlockReward(tier) : SeasonPass.FreeUnlockReward(tier);
            string reward = !string.IsNullOrEmpty(unlock) ? PrettyUnlock(unlock)
                          : tokens > 0 ? (tokens + "T")
                          : "-";

            bool claimed = SeasonPass.IsClaimed(tier, premium);
            bool canClaim = SeasonPass.CanClaim(tier, premium);

            string label = claimed ? "OK" : reward;
            Color color = claimed ? UITheme.Success
                        : canClaim ? (premium ? UITheme.Accent : UITheme.Primary)
                        : UITheme.Neutral;

            var btn = RuntimeUI.Button(parent, label, color, anchor, pos, new Vector2(150f, 60f), null);
            var lbl = btn.GetComponentInChildren<TMP_Text>();
            if (lbl != null) lbl.fontSize = 22f;
            btn.interactable = canClaim;
            if (canClaim)
            {
                int capturedTier = tier; bool capturedPremium = premium;
                btn.onClick.AddListener(() => SeasonPass.Claim(capturedTier, capturedPremium));
            }
        }

        private void RebuildQuests()
        {
            ClearChildren(_questContent);

            float y = -8f;
            y = BuildQuestGroup("DAILY", QuestSystem.Daily, y);
            y = BuildQuestGroup("WEEKLY", QuestSystem.Weekly, y);

            SetContentHeight(_questContent, -y + 20f);
        }

        private float BuildQuestGroup(string header, IReadOnlyList<Quest> quests, float y)
        {
            var hdr = RuntimeUI.Label(_questContent, header, 26,
                new Vector2(0.5f, 1f), new Vector2(0f, y - 8f), new Vector2(460f, 32f));
            hdr.fontStyle = FontStyles.Bold;
            hdr.color = UITheme.Gold;
            y -= 44f;

            foreach (var q in quests)
            {
                BuildQuestRow(q, y);
                y -= QuestRowPitch;
            }
            return y - 12f;
        }

        private void BuildQuestRow(Quest q, float y)
        {
            var row = RuntimeUI.Panel(_questContent, "Quest_" + q.Id, UITheme.Surface,
                new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                new Vector2(-230f, y - QuestRowH), new Vector2(230f, y));
            row.sprite = UITheme.RoundedSprite();
            row.type = Image.Type.Sliced;

            RuntimeUI.Label(row.transform, q.Description, 22,
                new Vector2(0f, 1f), new Vector2(16f, -10f), new Vector2(300f, 30f),
                TextAlignmentOptions.Left).fontStyle = FontStyles.Bold;

            // Progress bar + "x/target" readout.
            BuildBar(row.transform, new Vector2(-72f, -50f), 296f, q.Progress01, UITheme.Success);
            RuntimeUI.Label(row.transform, $"{q.Progress}/{q.Target}", 20,
                new Vector2(0f, 1f), new Vector2(16f, -64f), new Vector2(160f, 26f),
                TextAlignmentOptions.Left).color = UITheme.OnSurfaceMuted;

            // Reward summary on the row.
            RuntimeUI.Label(row.transform, $"+{q.XpReward}XP  +{q.TokenReward}T", 18,
                new Vector2(1f, 1f), new Vector2(-160f, -64f), new Vector2(200f, 26f),
                TextAlignmentOptions.Right).color = UITheme.Accent;

            // Claim button: active only when complete + unclaimed.
            bool canClaim = q.Complete && !q.Claimed;
            string label = q.Claimed ? "CLAIMED" : (q.Complete ? "CLAIM" : "...");
            Color color = q.Claimed ? UITheme.Success : (canClaim ? UITheme.Primary : UITheme.Neutral);
            var btn = RuntimeUI.Button(row.transform, label, color,
                new Vector2(1f, 0.5f), new Vector2(-12f, 0f), new Vector2(130f, 56f), null);
            var lbl = btn.GetComponentInChildren<TMP_Text>();
            if (lbl != null) lbl.fontSize = 22f;
            btn.interactable = canClaim;
            if (canClaim)
            {
                string id = q.Id;
                btn.onClick.AddListener(() => QuestSystem.Claim(id));
            }
        }

        // ---- small builders ------------------------------------------------------

        // A two-layer progress bar (track + fill) anchored top-centre of its parent.
        private void BuildBar(Transform parent, Vector2 pos, float width, float fill01, Color fillColor)
        {
            var track = RuntimeUI.Panel(parent, "BarTrack", new Color(0f, 0f, 0f, 0.45f),
                new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                new Vector2(pos.x - width * 0.5f, pos.y - 16f), new Vector2(pos.x + width * 0.5f, pos.y));
            track.sprite = UITheme.RoundedSprite();
            track.type = Image.Type.Sliced;

            float w = Mathf.Max(0f, width * Mathf.Clamp01(fill01));
            var fill = RuntimeUI.Panel(track.transform, "BarFill", fillColor,
                new Vector2(0f, 0f), new Vector2(0f, 1f), Vector2.zero, new Vector2(w, 0f));
            fill.sprite = UITheme.RoundedSprite();
            fill.type = Image.Type.Sliced;
        }

        private static void SetContentHeight(RectTransform content, float height)
        {
            content.sizeDelta = new Vector2(content.sizeDelta.x, Mathf.Max(0f, height));
        }

        private static void ClearChildren(RectTransform t)
        {
            for (int i = t.childCount - 1; i >= 0; i--)
                Destroy(t.GetChild(i).gameObject);
        }

        // "skin.Cowboy_Male" -> "Cowboy", "emote.wave" -> "WAVE", for compact reward labels.
        private static string PrettyUnlock(string id)
        {
            if (string.IsNullOrEmpty(id)) return "-";
            int dot = id.IndexOf('.');
            string rest = dot >= 0 ? id.Substring(dot + 1) : id;
            if (id.StartsWith("skin."))
            {
                int idx = SkinCatalog.IndexOf(rest);
                if (idx >= 0 && rest == SkinCatalog.Ids[idx]) return SkinCatalog.DisplayFor(rest);
                return rest; // ids not in the catalog (e.g. premium_gold) shown raw
            }
            return rest.ToUpper(); // emotes
        }
    }
}
