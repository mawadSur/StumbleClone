using System.Collections;
using System.Collections.Generic;
using StumbleClone.Audio;
using StumbleClone.Game;
using StumbleClone.Visuals;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace StumbleClone.UI
{
    /// The cosmetics LOCKER — a modal for previewing + equipping procedural EMOTES and VICTORY
    /// POSES (no new art; see <see cref="EmoteSystem"/>). The lead wires a LOCKER title button to
    /// <see cref="Open"/>. Skins keep their own inline shop on the title screen — this locker is
    /// strictly emotes + victory poses.
    ///
    /// Layout mirrors the existing modal/shop conventions (RuntimeUI + UITheme, the same
    /// dimmed-card + tap-to-equip + buy-then-equip flow the skin/perk shops use):
    ///   • A LIVE 3D preview of the equipped skin playing the highlighted cosmetic, rendered to a
    ///     RenderTexture via a private preview camera (so it can't disturb the menu scene).
    ///   • EMOTES / VICTORY tabs; each lists its catalog. Tapping a row previews + equips it when
    ///     owned, or buys-then-equips it (TokenWallet.TrySpend via EmoteSystem) when locked.
    ///
    /// Self-instantiated on demand: callers just invoke LockerUI.Open(). One instance at a time.
    public sealed class LockerUI : MonoBehaviour
    {
        private static LockerUI _instance;

        /// Open (or focus) the locker modal.
        public static void Open()
        {
            if (_instance != null) { _instance.transform.SetAsLastSibling(); return; }
            var go = new GameObject("LockerUI");
            _instance = go.AddComponent<LockerUI>();
        }

        private enum Tab { Emotes, Victory }

        // ---- preview rig --------------------------------------------------------
        private const int PreviewLayer = 31;               // a high, almost-certainly-unused layer
        private const int PreviewTex = 512;
        private static readonly Vector3 PreviewPivot = new Vector3(2000f, 2000f, 2000f); // far from gameplay

        private GameObject _overlay;
        private GameObject _previewRoot;     // instantiated skin model
        private Camera _previewCam;
        private Light _previewLight;
        private RenderTexture _previewRt;
        private Transform _previewVisual;    // the "Character" transform the EmotePlayer animates

        private Tab _tab = Tab.Emotes;
        private string _highlight;           // the id currently shown in the preview (may be unowned)
        private EmotePlayer _activePlayer;
        private float _emoteReplayTimer;     // re-fires one-shot emotes so the preview keeps moving

        // UI refs rebuilt per tab switch.
        private Transform _card;
        private Transform _listRoot;
        private TMP_Text _tokenLabel;
        private TMP_Text _previewName;
        private readonly List<RowRef> _rows = new List<RowRef>();

        private struct RowRef
        {
            public string Id;
            public TMP_Text Label;
            public Image Image;
        }

        private void Awake()
        {
            if (_instance != null && _instance != this) { Destroy(gameObject); return; }
            _instance = this;
        }

        private void Start()
        {
            BuildPreviewRig();
            BuildModal();
            // Start by highlighting whatever is equipped on the current tab.
            _highlight = EmoteSystem.SelectedEmote;
            RebuildList();
            PlayHighlight();
            OverlayIntro.Play(_overlay);
        }

        private void OnDestroy()
        {
            if (_instance == this) _instance = null;
            TeardownPreviewRig();
        }

        // ---- modal shell --------------------------------------------------------

        private void BuildModal()
        {
            _overlay = RuntimeUI.Overlay("LockerOverlay", 125); // above title(100)/modal(120)
            RuntimeUI.Panel(_overlay.transform, "Dim", new Color(0f, 0f, 0f, 0.74f),
                Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);

            // A wide card: preview on the left, scrollable list on the right.
            var cardImg = RuntimeUI.Panel(_overlay.transform, "Card", UITheme.SurfaceDeep,
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(-520f, -440f), new Vector2(520f, 440f));
            _card = cardImg.transform;

            var title = RuntimeUI.Label(_card, "LOCKER", 56,
                new Vector2(0.5f, 1f), new Vector2(0f, -28f), new Vector2(700f, 80f));
            title.fontStyle = FontStyles.Bold;
            title.color = UITheme.Gold;

            _tokenLabel = RuntimeUI.Label(_card, "", 30,
                new Vector2(1f, 1f), new Vector2(-36f, -34f), new Vector2(360f, 44f), TextAlignmentOptions.Right);
            _tokenLabel.fontStyle = FontStyles.Bold;
            _tokenLabel.color = UITheme.Gold;
            RefreshTokens();

            BuildPreviewPanel();
            BuildTabs();
            BuildListContainer();

            RuntimeUI.Button(_card, "DONE", UITheme.Neutral,
                new Vector2(0.5f, 0f), new Vector2(0f, 34f), new Vector2(300f, 60f), Close);
        }

        // Left half: the RenderTexture image + the highlighted cosmetic's name.
        private void BuildPreviewPanel()
        {
            var frame = RuntimeUI.Panel(_card, "PreviewFrame", UITheme.Surface,
                new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
                new Vector2(36f, -250f), new Vector2(456f, 260f));
            frame.GetComponent<Image>().sprite = UITheme.RoundedSprite();
            frame.GetComponent<Image>().type = Image.Type.Sliced;

            var raw = new GameObject("PreviewImage", typeof(RectTransform)).AddComponent<RawImage>();
            var rt = (RectTransform)raw.transform;
            rt.SetParent(frame.transform, false);
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
            rt.offsetMin = new Vector2(12f, 12f); rt.offsetMax = new Vector2(-12f, -12f);
            raw.texture = _previewRt;

            _previewName = RuntimeUI.Label(frame.transform, "", 34,
                new Vector2(0.5f, 0f), new Vector2(0f, 30f), new Vector2(400f, 48f));
            _previewName.fontStyle = FontStyles.Bold;
        }

        private void BuildTabs()
        {
            // Two tabs sit on the right column header, switching which catalog the list shows.
            var emotesBtn = RuntimeUI.Button(_card, "EMOTES",
                _tab == Tab.Emotes ? UITheme.Primary : UITheme.Neutral,
                new Vector2(1f, 1f), new Vector2(-300f, -120f), new Vector2(220f, 60f), () => SwitchTab(Tab.Emotes));
            var victoryBtn = RuntimeUI.Button(_card, "VICTORY",
                _tab == Tab.Victory ? UITheme.Primary : UITheme.Neutral,
                new Vector2(1f, 1f), new Vector2(-70f, -120f), new Vector2(220f, 60f), () => SwitchTab(Tab.Victory));
            // Tag them so SwitchTab can recolor without a full rebuild.
            emotesBtn.gameObject.name = "Tab_Emotes";
            victoryBtn.gameObject.name = "Tab_Victory";
        }

        private void BuildListContainer()
        {
            // A simple top-anchored column on the right half. The catalogs are short (<=6 rows), so
            // a fixed column is enough — no ScrollRect needed.
            var holder = new GameObject("ListRoot", typeof(RectTransform));
            var rt = (RectTransform)holder.transform;
            rt.SetParent(_card, false);
            rt.anchorMin = new Vector2(1f, 1f);
            rt.anchorMax = new Vector2(1f, 1f);
            rt.pivot = new Vector2(1f, 1f);
            rt.anchoredPosition = new Vector2(-36f, -200f);
            rt.sizeDelta = new Vector2(460f, 560f);
            _listRoot = rt;
        }

        private void SwitchTab(Tab tab)
        {
            if (_tab == tab) return;
            _tab = tab;
            AudioManager.Play(Sfx.UiClick);
            RecolorTabs();
            // Highlight the equipped item of the newly-selected tab.
            _highlight = tab == Tab.Emotes ? EmoteSystem.SelectedEmote : EmoteSystem.SelectedVictory;
            RebuildList();
            PlayHighlight();
        }

        private void RecolorTabs()
        {
            var e = _card.Find("Tab_Emotes")?.GetComponent<Image>();
            var v = _card.Find("Tab_Victory")?.GetComponent<Image>();
            if (e != null) e.color = _tab == Tab.Emotes ? UITheme.Primary : UITheme.Neutral;
            if (v != null) v.color = _tab == Tab.Victory ? UITheme.Primary : UITheme.Neutral;
        }

        // ---- list ---------------------------------------------------------------

        private EmoteSystem.Entry[] Catalog
            => _tab == Tab.Emotes ? EmoteSystem.Emotes : EmoteSystem.VictoryPoses;

        private void RebuildList()
        {
            _rows.Clear();
            if (_listRoot != null)
                for (int i = _listRoot.childCount - 1; i >= 0; i--)
                    Destroy(_listRoot.GetChild(i).gameObject);

            const float rowPitch = 84f;
            const float rowH = 76f;
            const float rowW = 460f;
            float y = 0f;

            var catalog = Catalog;
            for (int i = 0; i < catalog.Length; i++)
            {
                string id = catalog[i].Id; // capture per row
                var btn = RuntimeUI.Button(_listRoot, "", UITheme.Secondary,
                    new Vector2(0.5f, 1f), new Vector2(0f, y), new Vector2(rowW, rowH), null);
                var lbl = btn.GetComponentInChildren<TMP_Text>();
                var img = btn.GetComponent<Image>();
                lbl.fontSize = 26f;
                var lblCapture = lbl;
                btn.onClick.AddListener(() => OnRowClicked(id, lblCapture));
                _rows.Add(new RowRef { Id = id, Label = lbl, Image = img });
                y -= rowPitch;
            }

            RefreshRows();
        }

        private void RefreshRows()
        {
            string selected = _tab == Tab.Emotes ? EmoteSystem.SelectedEmote : EmoteSystem.SelectedVictory;
            foreach (var r in _rows)
            {
                bool owned = EmoteSystem.IsOwned(r.Id);
                bool equipped = owned && r.Id == selected;
                bool highlighted = r.Id == _highlight;
                int price = EmoteSystem.PriceOf(r.Id);

                string state = !owned ? ("BUY " + price) : (equipped ? "EQUIPPED" : "EQUIP");
                if (r.Label != null)
                    r.Label.text = EmoteSystem.NameOf(r.Id) + "   [" + state + "]";

                if (r.Image != null)
                {
                    // Equipped = pink CTA; highlighted-but-not-equipped = gold accent so the preview
                    // selection reads; unaffordable-locked = muted neutral; otherwise purple.
                    if (equipped) r.Image.color = UITheme.Primary;
                    else if (highlighted) r.Image.color = UITheme.Accent;
                    else if (!owned && !TokenWallet.CanAfford(price)) r.Image.color = UITheme.Neutral;
                    else r.Image.color = UITheme.Secondary;
                }
            }

            if (_previewName != null)
            {
                _previewName.text = EmoteSystem.NameOf(_highlight);
                _previewName.color = EmoteSystem.IsOwned(_highlight) ? UITheme.OnSurface : UITheme.OnSurfaceMuted;
            }
        }

        // Tap a row: always preview it. If owned -> equip. If locked -> buy-then-equip (or deny).
        private void OnRowClicked(string id, TMP_Text label)
        {
            _highlight = id;
            PlayHighlight();

            if (EmoteSystem.IsOwned(id))
            {
                Equip(id);
                AudioManager.Play(Sfx.UiClick);
                RefreshRows();
                return;
            }

            int price = EmoteSystem.PriceOf(id);
            if (TokenWallet.TrySpend(price))   // charge, then grant + equip (mirrors the skin shop)
            {
                EmoteSystem.GrantOwnership(id);
                Equip(id);
                RefreshTokens();
                CelebratePurchase(EmoteSystem.NameOf(id).ToUpper() + " UNLOCKED  -" + price);
            }
            else
            {
                FlashDenied(label);            // unaffordable — show the failure
            }
            RefreshRows();
        }

        private void Equip(string id)
        {
            if (_tab == Tab.Emotes) EmoteSystem.SelectedEmote = id;
            else EmoteSystem.SelectedVictory = id;
        }

        // ---- live preview -------------------------------------------------------

        private void BuildPreviewRig()
        {
            _previewRt = new RenderTexture(PreviewTex, PreviewTex, 16, RenderTextureFormat.ARGB32)
            {
                name = "LockerPreviewRT",
                antiAliasing = 2,
            };
            _previewRt.Create();

            // A camera looking at a fixed pivot far from gameplay, rendering only the preview layer
            // onto the RenderTexture so nothing in the live menu/scene is disturbed.
            var camGo = new GameObject("LockerPreviewCamera");
            camGo.transform.position = PreviewPivot + new Vector3(0f, 1.1f, 3.2f);
            camGo.transform.LookAt(PreviewPivot + new Vector3(0f, 1.0f, 0f));
            _previewCam = camGo.AddComponent<Camera>();
            _previewCam.targetTexture = _previewRt;
            _previewCam.clearFlags = CameraClearFlags.SolidColor;
            _previewCam.backgroundColor = new Color(0.10f, 0.12f, 0.20f, 0f); // transparent — frame shows through
            _previewCam.cullingMask = 1 << PreviewLayer;
            _previewCam.fieldOfView = 38f;
            _previewCam.nearClipPlane = 0.05f;
            _previewCam.farClipPlane = 50f;

            var lightGo = new GameObject("LockerPreviewLight");
            lightGo.transform.position = PreviewPivot + new Vector3(2f, 3f, 2f);
            lightGo.transform.LookAt(PreviewPivot + Vector3.up);
            _previewLight = lightGo.AddComponent<Light>();
            _previewLight.type = LightType.Directional;
            _previewLight.color = new Color(1f, 0.97f, 0.9f);
            _previewLight.intensity = 1.1f;
            _previewLight.cullingMask = 1 << PreviewLayer;

            BuildPreviewModel();
        }

        // Instantiate the player's equipped skin (or the default) at the preview pivot.
        private void BuildPreviewModel()
        {
            string skinId = SkinStore.Current;
            GameObject prefab = Resources.Load<GameObject>("Skins/" + skinId);
            if (prefab == null) prefab = Resources.Load<GameObject>("Skins/" + SkinCatalog.Default);

            _previewRoot = new GameObject("LockerPreviewModel");
            _previewRoot.transform.position = PreviewPivot;
            _previewRoot.transform.rotation = Quaternion.Euler(0f, 18f, 0f); // slight 3/4 view

            if (prefab != null)
            {
                var model = Instantiate(prefab, _previewRoot.transform);
                model.name = "Character";
                model.transform.localPosition = Vector3.zero;
                model.transform.localRotation = Quaternion.identity;
                _previewVisual = model.transform;
            }
            else
            {
                // No skin resource available (e.g. SkinSetup not run) — fall back to a capsule so the
                // preview still animates and the locker stays usable.
                var capsule = GameObject.CreatePrimitive(PrimitiveType.Capsule);
                capsule.name = "Character";
                capsule.transform.SetParent(_previewRoot.transform, false);
                var col = capsule.GetComponent<Collider>();
                if (col != null) Destroy(col);
                _previewVisual = capsule.transform;
            }

            SetLayerRecursive(_previewRoot, PreviewLayer);
        }

        // Re-fire the highlighted cosmetic on the preview model.
        private void PlayHighlight()
        {
            if (_previewRoot == null || string.IsNullOrEmpty(_highlight)) return;
            if (_activePlayer != null) { _activePlayer.Stop(); _activePlayer = null; }
            _activePlayer = EmoteSystem.Play(_highlight, _previewRoot.transform);
            _emoteReplayTimer = 0f;
        }

        private void Update()
        {
            // Keep one-shot emote previews lively by replaying them on a loop (victory poses already
            // loop, so the player persists and this just idles).
            if (_previewRoot == null) return;
            _previewRoot.transform.Rotate(0f, 12f * Time.unscaledDeltaTime, 0f); // slow turntable

            if (_tab == Tab.Emotes)
            {
                _emoteReplayTimer += Time.unscaledDeltaTime;
                if (_activePlayer == null || !_activePlayer)
                {
                    if (_emoteReplayTimer > 0.5f) PlayHighlight();
                }
            }
        }

        private void TeardownPreviewRig()
        {
            if (_activePlayer != null) { _activePlayer.Stop(); _activePlayer = null; }
            if (_previewRoot != null) Destroy(_previewRoot);
            if (_previewCam != null) Destroy(_previewCam.gameObject);
            if (_previewLight != null) Destroy(_previewLight.gameObject);
            if (_previewRt != null)
            {
                _previewRt.Release();
                Destroy(_previewRt);
            }
        }

        private static void SetLayerRecursive(GameObject go, int layer)
        {
            go.layer = layer;
            foreach (Transform child in go.transform) SetLayerRecursive(child.gameObject, layer);
        }

        // ---- feedback helpers (mirrors TitleScreen's purchase celebration) ------

        private void RefreshTokens()
        {
            if (_tokenLabel != null) _tokenLabel.text = "TOKENS: " + TokenWallet.Balance;
        }

        private void CelebratePurchase(string message)
        {
            AudioManager.Play(Sfx.Pickup);
            var toastOverlay = RuntimeUI.Overlay("LockerToastOverlay", 135);
            var toast = RuntimeUI.Label(toastOverlay.transform, message, 30,
                new Vector2(0.5f, 1f), new Vector2(0f, -120f), new Vector2(720f, 48f));
            toast.color = UITheme.Gold;
            toast.fontStyle = FontStyles.Bold;
            toast.raycastTarget = false;
            StartCoroutine(FadeAndDestroy(toast, toastOverlay, 1.4f));
        }

        private void FlashDenied(TMP_Text label)
        {
            if (label != null) StartCoroutine(FlashRed(label));
        }

        private static IEnumerator FlashRed(TMP_Text label)
        {
            Color original = label.color;
            label.color = UITheme.Danger;
            float t = 0f;
            while (t < 0.35f) { t += Time.unscaledDeltaTime; yield return null; }
            if (label != null) label.color = original;
        }

        private IEnumerator FadeAndDestroy(TMP_Text toast, GameObject overlay, float hold)
        {
            if (toast == null) { if (overlay != null) Destroy(overlay); yield break; }
            float t = 0f;
            while (t < hold && toast != null) { t += Time.unscaledDeltaTime; yield return null; }
            float f = 0f;
            Color start = toast != null ? toast.color : Color.clear;
            while (f < 0.4f && toast != null)
            {
                f += Time.unscaledDeltaTime;
                Color c = start; c.a = Mathf.Lerp(start.a, 0f, f / 0.4f);
                toast.color = c;
                yield return null;
            }
            if (overlay != null) Destroy(overlay);
        }

        private void Close()
        {
            AudioManager.Play(Sfx.UiClick);
            if (_overlay != null) Destroy(_overlay);
            Destroy(gameObject);
        }
    }
}
