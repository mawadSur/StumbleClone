using System;
using System.Threading.Tasks;
using Unity.Netcode;
using Unity.Services.Authentication;
using Unity.Services.Core;
using Unity.Services.Multiplayer;
using UnityEngine;
using UgsMultiplayerService = Unity.Services.Multiplayer.MultiplayerService;

namespace StumbleClone.Net
{
    /// <summary>
    /// Static facade for online "lobby" sessions, built on the Unity Multiplayer Services
    /// Sessions SDK (com.unity.services.multiplayer) with the default NGO + Relay network
    /// handler. The UI talks ONLY to this class — it never touches the UGS SDK directly.
    ///
    /// Creating a session via <see cref="HostAsync"/> auto-starts a Netcode-for-GameObjects
    /// host over Relay; joining via <see cref="JoinByCodeAsync"/> auto-starts an NGO client
    /// over Relay. This works because the SDK's NetworkProvider detects the installed NGO 2.x
    /// package and, when <c>WithRelayNetwork()</c> is set with no custom <c>INetworkHandler</c>,
    /// drives <c>NetworkManager.Singleton</c> (Relay transport + StartHost/StartClient) itself.
    ///
    /// No cloud call ever throws to the caller: the UGS project may not be linked yet, so every
    /// path is wrapped in try/catch and degrades to a logged warning + a null/false return.
    /// </summary>
    public static class MultiplayerService
    {
        // ---- State -----------------------------------------------------------------------

        /// <summary>The active session (host or client), or null when not in a session.</summary>
        public static ISession ActiveSession { get; private set; }

        /// <summary>The join code other players type to join (null when not hosting/joined).</summary>
        public static string JoinCode => ActiveSession?.Code;

        /// <summary>True when the local player is the host of <see cref="ActiveSession"/>.</summary>
        public static bool IsHost => ActiveSession?.IsHost ?? false;

        /// <summary>True while a session is active.</summary>
        public static bool InSession => ActiveSession != null;

        /// <summary>
        /// Fired whenever the session changes in a way the UI cares about: joined, left,
        /// deleted/kicked, or the player list / properties changed. Always raised on the main
        /// thread (the SDK invokes its session callbacks there).
        /// </summary>
        public static event Action OnSessionChanged;

        // ---- Internals -------------------------------------------------------------------

        // Guards against re-entrant UnityServices.InitializeAsync() (which throws if called
        // while already initializing). We do our own gate on top of UnityServices.State.
        private static bool s_initializing;

        private const string PlayerNamePropertyKey = "_player_name"; // matches PlayerNameModule.PropertyKey

        private const string LinkHint =
            "Is the Unity Cloud project linked (Edit > Project Settings > Services) and are " +
            "Relay + Lobby enabled in the Unity Cloud dashboard?";

        // ---- Auth ------------------------------------------------------------------------

        /// <summary>
        /// Initializes Unity Services (once) and signs in anonymously if not already signed in.
        /// Safe to call repeatedly. Never throws — returns false on any failure.
        /// </summary>
        public static async Task<bool> EnsureSignedInAsync()
        {
            try
            {
                if (UnityServices.State != ServicesInitializationState.Initialized)
                {
                    // Avoid a double InitializeAsync if a concurrent call is in flight.
                    while (s_initializing)
                        await Task.Yield();

                    if (UnityServices.State == ServicesInitializationState.Uninitialized)
                    {
                        s_initializing = true;
                        try
                        {
                            await UnityServices.InitializeAsync();
                        }
                        finally
                        {
                            s_initializing = false;
                        }
                    }
                    else
                    {
                        // Another caller is finishing initialization; wait it out.
                        while (UnityServices.State == ServicesInitializationState.Initializing)
                            await Task.Yield();
                    }
                }

                if (!AuthenticationService.Instance.IsSignedIn)
                    await AuthenticationService.Instance.SignInAnonymouslyAsync();

                return AuthenticationService.Instance.IsSignedIn;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[MultiplayerService] Sign-in/init failed: {e.Message}\n{LinkHint}");
                return false;
            }
        }

        // ---- Host ------------------------------------------------------------------------

        /// <summary>
        /// Creates a session (lobby) over Relay and auto-starts the NGO host. Returns the join
        /// code players use to join, or null on failure.
        /// </summary>
        /// <param name="playerName">Display name shown to other players in the lobby.</param>
        /// <param name="maxPlayers">Max players including the host. Defaults to 8.</param>
        public static async Task<string> HostAsync(string playerName, int maxPlayers = 8)
        {
            if (!await EnsureSignedInAsync())
                return null;

            await TrySetAuthPlayerNameAsync(playerName);

            try
            {
                var options = new SessionOptions
                {
                    MaxPlayers = Mathf.Max(1, maxPlayers),
                }
                .WithRelayNetwork()   // default NGO+Relay handler: auto-starts host over Relay
                .WithPlayerName();    // syncs the Authentication player name into the session

                // Also stamp the name directly as a player property so the UI can read it even
                // if the Authentication name fetch is unavailable (key matches the SDK module).
                if (!string.IsNullOrWhiteSpace(playerName))
                    options.PlayerProperties[PlayerNamePropertyKey] =
                        new PlayerProperty(playerName, VisibilityPropertyOptions.Member);

                IHostSession session = await UgsMultiplayerService.Instance.CreateSessionAsync(options);

                SetActiveSession(session);
                return session?.Code;
            }
            catch (SessionException e)
            {
                Debug.LogWarning($"[MultiplayerService] CreateSession failed: {e.Message}\n{LinkHint}");
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[MultiplayerService] CreateSession failed: {e.Message}\n{LinkHint}");
            }

            return null;
        }

        // ---- Join ------------------------------------------------------------------------

        /// <summary>
        /// Joins a session by its join code and auto-starts the NGO client over Relay.
        /// Returns true on success.
        /// </summary>
        public static async Task<bool> JoinByCodeAsync(string code, string playerName)
        {
            if (string.IsNullOrWhiteSpace(code))
            {
                Debug.LogWarning("[MultiplayerService] JoinByCode called with an empty code.");
                return false;
            }

            if (!await EnsureSignedInAsync())
                return false;

            await TrySetAuthPlayerNameAsync(playerName);

            try
            {
                var options = new JoinSessionOptions().WithPlayerName();

                if (!string.IsNullOrWhiteSpace(playerName))
                    options.PlayerProperties[PlayerNamePropertyKey] =
                        new PlayerProperty(playerName, VisibilityPropertyOptions.Member);

                ISession session =
                    await UgsMultiplayerService.Instance.JoinSessionByCodeAsync(code.Trim(), options);

                SetActiveSession(session);
                return session != null;
            }
            catch (SessionException e)
            {
                Debug.LogWarning($"[MultiplayerService] JoinByCode failed: {e.Message}\n{LinkHint}");
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[MultiplayerService] JoinByCode failed: {e.Message}\n{LinkHint}");
            }

            return false;
        }

        // ---- Leave -----------------------------------------------------------------------

        /// <summary>
        /// Leaves (client) or deletes (host) the active session and shuts NGO down. Never throws.
        /// </summary>
        public static async Task LeaveAsync()
        {
            var session = ActiveSession;

            // Detach state + listeners first so the resulting Deleted/RemovedFromSession
            // callbacks don't re-enter and fire OnSessionChanged twice.
            ClearActiveSession();

            if (session != null)
            {
                try
                {
                    if (session.IsHost)
                        await session.AsHost().DeleteAsync();
                    else
                        await session.LeaveAsync();
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"[MultiplayerService] Leave/Delete failed: {e.Message}");
                }
            }

            // The Relay network handler normally shuts NGO down on leave; do it defensively
            // in case the session object was already gone.
            try
            {
                var nm = NetworkManager.Singleton;
                if (nm != null && (nm.IsServer || nm.IsClient))
                    nm.Shutdown();
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[MultiplayerService] NetworkManager shutdown failed: {e.Message}");
            }

            OnSessionChanged?.Invoke();
        }

        // ---- Helpers ---------------------------------------------------------------------

        /// <summary>Best-effort: push the chosen display name to the Authentication service.</summary>
        private static async Task TrySetAuthPlayerNameAsync(string playerName)
        {
            if (string.IsNullOrWhiteSpace(playerName))
                return;

            try
            {
                if (AuthenticationService.Instance.IsSignedIn &&
                    AuthenticationService.Instance.PlayerName != playerName)
                {
                    await AuthenticationService.Instance.UpdatePlayerNameAsync(playerName.Trim());
                }
            }
            catch (Exception e)
            {
                // Non-fatal: the direct player property still carries the name into the session.
                Debug.LogWarning($"[MultiplayerService] UpdatePlayerName failed (non-fatal): {e.Message}");
            }
        }

        private static void SetActiveSession(ISession session)
        {
            if (session == null)
                return;

            ClearActiveSession();
            ActiveSession = session;

            session.Changed += HandleSessionChanged;
            session.PlayerJoined += HandlePlayerListChanged;
            session.PlayerHasLeft += HandlePlayerListChanged;
            session.PlayerPropertiesChanged += HandleSessionChanged;
            session.Deleted += HandleSessionEnded;
            session.RemovedFromSession += HandleSessionEnded;

            OnSessionChanged?.Invoke();
        }

        private static void ClearActiveSession()
        {
            var session = ActiveSession;
            if (session != null)
            {
                session.Changed -= HandleSessionChanged;
                session.PlayerJoined -= HandlePlayerListChanged;
                session.PlayerHasLeft -= HandlePlayerListChanged;
                session.PlayerPropertiesChanged -= HandleSessionChanged;
                session.Deleted -= HandleSessionEnded;
                session.RemovedFromSession -= HandleSessionEnded;
            }
            ActiveSession = null;
        }

        private static void HandleSessionChanged() => OnSessionChanged?.Invoke();
        private static void HandlePlayerListChanged(string _) => OnSessionChanged?.Invoke();

        // Host deleted the session, or we were kicked/disconnected: drop our reference and
        // make sure NGO is torn down so the game returns to a clean offline state.
        private static void HandleSessionEnded()
        {
            ClearActiveSession();
            try
            {
                var nm = NetworkManager.Singleton;
                if (nm != null && (nm.IsServer || nm.IsClient))
                    nm.Shutdown();
            }
            catch { /* defensive only */ }

            OnSessionChanged?.Invoke();
        }
    }
}
