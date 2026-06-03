using System;
using System.Text;
using StumbleClone.Core;
using StumbleClone.Game;
using StumbleClone.Net;
using TMPro;
using Unity.Services.Multiplayer; // ISession + IReadOnlyPlayer.GetPlayerName() extension
using UnityEngine;
using UnityEngine.UI;
// Disambiguate: both StumbleClone.Net and Unity.Services.Multiplayer expose a "MultiplayerService".
// This file talks to OUR facade — alias the simple name to it (SDK types used via their namespace).
using MultiplayerService = StumbleClone.Net.MultiplayerService;

namespace StumbleClone.UI
{
    /// Self-contained multiplayer lobby modal, built entirely in code with RuntimeUI + UITheme so it
    /// matches the rest of the game and needs no scene wiring. Opened from the title screen's
    /// MULTIPLAYER button via <see cref="Open"/>.
    ///
    /// It is a thin VIEW over the networking wrapper <see cref="MultiplayerService"/> (owned elsewhere):
    ///   • HostAsync(name, max)  — create a session + auto-start NGO host over Relay; the host's code
    ///                             is then surfaced from ActiveSession.Code.
    ///   • JoinByCodeAsync(code, name) — join an existing session by its share code.
    ///   • LeaveAsync()          — leave/teardown the current session.
    ///   • OnSessionChanged      — fired whenever the roster/state changes (join, leave, host start).
    ///   • ActiveSession         — the live Unity ISession (Code / Players / PlayerCount / IsHost), or null.
    ///
    /// All service calls are async Tasks driven from `async void` UI handlers wrapped in try/catch, so a
    /// failure (e.g. the cloud project isn't linked) surfaces as a status line instead of an unhandled
    /// exception. OnSessionChanged can arrive off the main thread, so it only flips a dirty flag — the
    /// actual UI rebuild happens in Update on the main thread.
    public sealed class MultiplayerUI : MonoBehaviour
    {
        public static MultiplayerUI Instance { get; private set; }

        // The level every client loads when the host hits START. LastStanding (Knockout) is the
        // always-unlocked free mode, so it's the safe shared default for a multiplayer round.
        private const LevelMode MultiplayerLevel = LevelMode.LastStanding;
        private const int MaxPlayers = 8;

        private GameObject _modal;      // the whole overlay (destroy to close)
        private Transform _card;        // content parent inside the dimmed card
        private GameObject _menuRoot;    // HOST / JOIN entry view
        private GameObject _lobbyRoot;   // in-session roster view
        private TMP_Text _statusLabel;   // async-state line (shared by both views)
        private TMP_Text _codeValue;     // big join code in the lobby view (selectable)
        private TMP_Text _playerList;    // roster text in the lobby view
        private TMP_Text _lobbyHint;     // "waiting for host" / "you are the host" line
        private TMP_InputField _codeInput; // join-code entry in the menu view
        private Button _startButton;     // host-only START
        private Button _hostButton;
        private Button _joinButton;

        private bool _busy;             // an async op is in flight — guard against double taps
        private bool _sessionDirty;     // OnSessionChanged fired; rebuild roster on the next Update

        // Bootstrap a hidden instance into the menu scene so TitleScreen can call Open() without wiring.
        public static void EnsureInstance()
        {
            if (Instance != null) return;
            var go = new GameObject("MultiplayerUI");
            go.AddComponent<MultiplayerUI>();
        }

        /// Open (or re-open) the multiplayer panel. Lazily creates the singleton if needed.
        public static void Open()
        {
            EnsureInstance();
            Instance.OpenInternal();
        }

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
        }

        private void OnEnable()
        {
            MultiplayerService.OnSessionChanged += HandleSessionChanged;
        }

        private void OnDisable()
        {
            MultiplayerService.OnSessionChanged -= HandleSessionChanged;
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        // OnSessionChanged may be raised from a background continuation; never touch UGUI here.
        private void HandleSessionChanged() => _sessionDirty = true;

        private void Update()
        {
            if (_sessionDirty)
            {
                _sessionDirty = false;
                if (_modal != null) RefreshView();
            }
        }

        // ---- View construction --------------------------------------------------

        private void OpenInternal()
        {
            if (_modal != null) { Destroy(_modal); _modal = null; }

            _busy = false;

            _modal = RuntimeUI.Overlay("MultiplayerOverlay", 125); // above title(100)/modal(120)
            RuntimeUI.Panel(_modal.transform, "Dim", new Color(0f, 0f, 0f, 0.72f),
                Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);

            var cardImg = RuntimeUI.Panel(_modal.transform, "Card", UITheme.SurfaceDeep,
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(-420f, -470f), new Vector2(420f, 470f));
            _card = cardImg.transform;

            var title = RuntimeUI.Label(_card, "MULTIPLAYER", 56,
                new Vector2(0.5f, 1f), new Vector2(0f, -30f), new Vector2(760f, 80f));
            title.fontStyle = FontStyles.Bold;
            title.color = UITheme.Gold;

            // Status line lives just under the title and is shared by both sub-views.
            _statusLabel = RuntimeUI.Label(_card, "", 26,
                new Vector2(0.5f, 1f), new Vector2(0f, -108f), new Vector2(780f, 40f));
            _statusLabel.color = UITheme.OnSurfaceMuted;

            BuildMenuView();
            BuildLobbyView();

            // Shared BACK/close — also leaves the session if we're in one.
            RuntimeUI.Button(_card, "BACK", UITheme.Neutral,
                new Vector2(0.5f, 0f), new Vector2(0f, 40f), new Vector2(300f, 64f), OnBack);

            RefreshView();
            OverlayIntro.Play(_modal);
        }

        // HOST + JOIN entry view.
        private void BuildMenuView()
        {
            _menuRoot = new GameObject("MenuView", typeof(RectTransform));
            var rt = (RectTransform)_menuRoot.transform;
            rt.SetParent(_card, false);
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
            var t = _menuRoot.transform;

            // HOST — big primary CTA up top.
            _hostButton = RuntimeUI.Button(t, "HOST GAME", UITheme.Primary,
                new Vector2(0.5f, 1f), new Vector2(0f, -190f), new Vector2(700f, 90f), OnHost);

            RuntimeUI.Label(t, "Create a lobby and share the join code with friends.", 24,
                new Vector2(0.5f, 1f), new Vector2(0f, -258f), new Vector2(740f, 36f))
                .color = UITheme.OnSurfaceMuted;

            // Divider label.
            RuntimeUI.Label(t, "— OR JOIN BY CODE —", 26,
                new Vector2(0.5f, 1f), new Vector2(0f, -322f), new Vector2(740f, 40f))
                .color = UITheme.OnSurfaceMuted;

            // Code entry. Uppercased + trimmed when read; characterLimit is generous for relay codes.
            _codeInput = RuntimeUI.InputField(t, "ENTER CODE", "",
                new Vector2(0.5f, 1f), new Vector2(0f, -390f), new Vector2(440f, 70f));
            _codeInput.characterLimit = 12;
            _codeInput.contentType = TMP_InputField.ContentType.Alphanumeric;
            // Force uppercase as the player types so the display matches the host's shown code.
            _codeInput.onValueChanged.AddListener(v =>
            {
                string up = v.ToUpperInvariant();
                if (up != v) _codeInput.SetTextWithoutNotify(up);
            });

            _joinButton = RuntimeUI.Button(t, "JOIN", UITheme.Secondary,
                new Vector2(0.5f, 1f), new Vector2(0f, -470f), new Vector2(440f, 76f), OnJoin);
        }

        // In-session roster view (host or guest).
        private void BuildLobbyView()
        {
            _lobbyRoot = new GameObject("LobbyView", typeof(RectTransform));
            var rt = (RectTransform)_lobbyRoot.transform;
            rt.SetParent(_card, false);
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
            var t = _lobbyRoot.transform;

            RuntimeUI.Label(t, "JOIN CODE", 26,
                new Vector2(0.5f, 1f), new Vector2(0f, -160f), new Vector2(700f, 34f))
                .color = UITheme.OnSurfaceMuted;

            // Big selectable/copyable code. A read-only TMP_InputField lets the player drag-select +
            // copy it on desktop; on mobile it's still legible and tappable. Centred and bold.
            var codeField = RuntimeUI.InputField(t, "", "",
                new Vector2(0.5f, 1f), new Vector2(0f, -226f), new Vector2(420f, 78f));
            codeField.readOnly = true;
            codeField.pointSize = 52f;
            var codeText = codeField.textComponent;
            if (codeText != null)
            {
                codeText.alignment = TextAlignmentOptions.Center;
                codeText.fontStyle = FontStyles.Bold;
                codeText.color = UITheme.Gold;
            }
            _codeValue = codeText;

            // Roster header + body.
            RuntimeUI.Label(t, "PLAYERS", 26,
                new Vector2(0.5f, 1f), new Vector2(0f, -302f), new Vector2(700f, 34f))
                .color = UITheme.OnSurfaceMuted;

            _playerList = RuntimeUI.Label(t, "", 28,
                new Vector2(0.5f, 1f), new Vector2(0f, -430f), new Vector2(700f, 220f), TextAlignmentOptions.Top);

            _lobbyHint = RuntimeUI.Label(t, "", 24,
                new Vector2(0.5f, 0f), new Vector2(0f, 196f), new Vector2(740f, 36f));
            _lobbyHint.color = UITheme.OnSurfaceMuted;

            // START — host only; loads the gameplay level for everyone (NGO scene sync handles guests).
            _startButton = RuntimeUI.Button(t, "START", UITheme.Primary,
                new Vector2(0.5f, 0f), new Vector2(0f, 120f), new Vector2(440f, 76f), OnStart);
        }

        // ---- View state ---------------------------------------------------------

        private bool InSession => SafeActiveSession() != null;

        private void RefreshView()
        {
            bool inSession = InSession;
            if (_menuRoot != null) _menuRoot.SetActive(!inSession);
            if (_lobbyRoot != null) _lobbyRoot.SetActive(inSession);

            // Entry buttons are disabled while an async op is running.
            if (_hostButton != null) _hostButton.interactable = !_busy;
            if (_joinButton != null) _joinButton.interactable = !_busy;

            if (inSession) RefreshLobby();
        }

        private void RefreshLobby()
        {
            var session = SafeActiveSession();
            if (session == null) return;

            bool isHost = SafeIsHost(session);

            if (_codeValue != null) _codeValue.text = SafeCode(session);

            if (_playerList != null) _playerList.text = BuildRosterText(session);

            if (_lobbyHint != null)
                _lobbyHint.text = isHost
                    ? "You are the host — start when everyone's in."
                    : "Waiting for the host to start the game…";

            // START is host-only and needs at least the host present.
            if (_startButton != null)
            {
                _startButton.gameObject.SetActive(isHost);
                _startButton.interactable = isHost && !_busy;
            }
        }

        // "1. Alice (you)\n2. Bob\n…  (3 / 8)"
        private string BuildRosterText(ISession session)
        {
            var sb = new StringBuilder();
            int count = 0;
            int max = MaxPlayers;
            try { max = session.MaxPlayers; } catch { /* fall back to MaxPlayers const */ }

            string selfId = SafeCurrentPlayerId(session);

            try
            {
                var players = session.Players;
                if (players != null)
                {
                    for (int i = 0; i < players.Count; i++)
                    {
                        var p = players[i];
                        string name = SafePlayerName(p, i);
                        bool isSelf = selfId != null && p != null && p.Id == selfId;
                        sb.Append(i + 1).Append(". ").Append(name);
                        if (isSelf) sb.Append("  (you)");
                        sb.Append('\n');
                        count++;
                    }
                }
            }
            catch { /* roster transiently unavailable — show whatever we have */ }

            if (count == 0) sb.Append("Connecting…\n");
            sb.Append("\n(").Append(count).Append(" / ").Append(max).Append(')');
            return sb.ToString();
        }

        private void SetStatus(string message, bool error = false)
        {
            if (_statusLabel == null) return;
            _statusLabel.text = message ?? "";
            _statusLabel.color = error ? UITheme.Danger : UITheme.OnSurfaceMuted;
        }

        // ---- Async action handlers ----------------------------------------------

        private async void OnHost()
        {
            if (_busy) return;
            _busy = true;
            RefreshView();
            SetStatus("Signing in…");

            try
            {
                SetStatus("Creating lobby…");
                await MultiplayerService.HostAsync(PlayerName(), MaxPlayers);
                SetStatus("Lobby ready — share your code!");
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[MultiplayerUI] Host failed: {e}");
                SetStatus("Couldn't create the lobby — is the cloud project linked?", true);
            }
            finally
            {
                _busy = false;
                RefreshView();
            }
        }

        private async void OnJoin()
        {
            if (_busy) return;

            string code = _codeInput != null ? _codeInput.text.Trim().ToUpperInvariant() : "";
            if (string.IsNullOrEmpty(code))
            {
                SetStatus("Enter a join code first.", true);
                return;
            }

            _busy = true;
            RefreshView();
            SetStatus("Signing in…");

            try
            {
                SetStatus("Joining lobby…");
                await MultiplayerService.JoinByCodeAsync(code, PlayerName());
                SetStatus("Joined! Waiting for the host…");
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[MultiplayerUI] Join failed: {e}");
                SetStatus("Couldn't join — check the code, or the cloud project link.", true);
            }
            finally
            {
                _busy = false;
                RefreshView();
            }
        }

        // Host-only. Loads the shared gameplay level. With the SDK's NGO+Relay network handler the host
        // is already the NGO server, so a networked scene load propagates to every connected client.
        private void OnStart()
        {
            var session = SafeActiveSession();
            if (session == null || !SafeIsHost(session)) return;

            SetStatus("Starting…");
            ClosePanel(); // leave the modal up to here, then drop into gameplay
            if (GameManager.Instance != null) GameManager.Instance.LoadLevel(MultiplayerLevel);
            else Debug.LogWarning("[MultiplayerUI] No GameManager — cannot load the multiplayer level.");
        }

        private async void OnBack()
        {
            if (InSession)
            {
                _busy = true;
                SetStatus("Leaving lobby…");
                try { await MultiplayerService.LeaveAsync(); }
                catch (Exception e) { Debug.LogWarning($"[MultiplayerUI] Leave failed: {e}"); }
                finally { _busy = false; }
            }
            ClosePanel();
        }

        private void ClosePanel()
        {
            if (_modal != null) { Destroy(_modal); _modal = null; }
        }

        private static string PlayerName() => LeaderboardStore.GetPlayerName();

        // ---- Defensive accessors over the service / SDK session ------------------
        // The wrapper drives an async lifecycle; any member can be transiently null mid-transition.

        private static ISession SafeActiveSession()
        {
            try { return MultiplayerService.ActiveSession; }
            catch { return null; }
        }

        private static bool SafeIsHost(ISession s)
        {
            try { return s != null && (s.IsHost || s.IsServer); }
            catch { return false; }
        }

        private static string SafeCode(ISession s)
        {
            try { return string.IsNullOrEmpty(s?.Code) ? "…" : s.Code; }
            catch { return "…"; }
        }

        private static string SafeCurrentPlayerId(ISession s)
        {
            try { return s?.CurrentPlayer?.Id; }
            catch { return null; }
        }

        // Player display name comes from the SDK PlayerName module via the GetPlayerName() extension.
        // A just-joined player may not have synced a name yet, so fall back to a short id / index.
        private static string SafePlayerName(IReadOnlyPlayer p, int index)
        {
            if (p == null) return "Player " + (index + 1);
            try
            {
                string n = p.GetPlayerName();
                if (!string.IsNullOrWhiteSpace(n)) return n;
            }
            catch { /* name not synced yet */ }

            try
            {
                if (!string.IsNullOrEmpty(p.Id))
                    return "Player " + p.Id.Substring(0, Mathf.Min(4, p.Id.Length));
            }
            catch { /* ignore */ }
            return "Player " + (index + 1);
        }
    }
}
