using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;

namespace StumbleClone.Net
{
    /// Phase-0 multiplayer foundation (Netcode for GameObjects). Provides host/client entry points
    /// against NetworkManager.Singleton and is DORMANT by default — nothing starts a session, so the
    /// single-player game is completely unaffected (this just proves NGO integrates and gives the
    /// surface the rest of multiplayer will build on).
    ///
    /// PHASE ROADMAP (still ahead):
    ///  • Phase 1 — turn Player.prefab into a NetworkObject; add a NetworkInputProvider : IPlayerInput
    ///    (the remote-input seam PlayerController.SetInputSource already accepts); put a NetworkManager
    ///    + UnityTransport in the gameplay scenes (WebSocket mode for WebGL, UDP for iOS/Android);
    ///    networked spawn flow; 2-player relay/LAN connect test.
    ///  • Phase 2 — server-owned bots (NavMeshAgent has no net API → puppet via replicated transforms),
    ///    client-side prediction + reconciliation for the Rigidbody, replicated RacerRegistry / win
    ///    logic, lobby/matchmaking, 8-player.
    public static class NetworkBootstrap
    {
        /// True once a host or client session is live.
        public static bool IsOnline =>
            NetworkManager.Singleton != null &&
            (NetworkManager.Singleton.IsServer || NetworkManager.Singleton.IsClient);

        /// Default direct-connect port. Picked once here so host and client agree; matchmaking/Relay
        /// (Phase 2) will supply its own allocation data and bypass this.
        public const ushort DefaultPort = 7777;

        public static bool StartHost() => WithManager(nm => nm.StartHost());
        public static bool StartClient() => WithManager(nm => nm.StartClient());

        /// Configure the scene's UnityTransport before StartHost/StartClient. Sets the connection
        /// endpoint and the transport protocol. WebGL CANNOT open raw UDP sockets, so a WebGL build MUST
        /// use WebSockets (<paramref name="useWebSockets"/> = true); native iOS/Android/desktop builds
        /// use plain UDP (false) for lower latency. Host and client must agree on the protocol.
        ///
        /// Returns false (with a warning) if there's no NetworkManager or no UnityTransport yet — both
        /// are expected until the editor wiring in MULTIPLAYER_PHASE1.md exists, so this is safe to call
        /// from dormant code.
        /// <param name="address">Server IP/host to listen on (host) or connect to (client). "127.0.0.1"
        /// for a local 2-client test.</param>
        /// <param name="port">Server port. Defaults to <see cref="DefaultPort"/>.</param>
        /// <param name="useWebSockets">True for WebGL (WebSocket transport); false for native UDP.</param>
        public static bool ConfigureTransport(string address, ushort port = DefaultPort, bool useWebSockets = false)
        {
            var nm = NetworkManager.Singleton;
            if (nm == null)
            {
                Debug.LogWarning("[NetworkBootstrap] No NetworkManager — cannot configure transport (Phase 1 not wired).");
                return false;
            }

            var transport = nm.GetComponent<UnityTransport>();
            if (transport == null)
            {
                Debug.LogWarning("[NetworkBootstrap] NetworkManager has no UnityTransport component — add one (see MULTIPLAYER_PHASE1.md).");
                return false;
            }

            // UseWebSockets must be set before the connection is established; flipping it live has no
            // effect on an already-bound socket. NGO 2.x exposes it as a public property on UnityTransport.
            transport.UseWebSockets = useWebSockets;

            // listenAddress left null -> UnityTransport binds to the same address (fine for localhost and
            // a single-NIC server). A real server behind NAT sets a separate bind address; out of Phase-1
            // scope.
            transport.SetConnectionData(address, port);
            return true;
        }

        public static void Shutdown()
        {
            if (NetworkManager.Singleton != null) NetworkManager.Singleton.Shutdown();
        }

        private static bool WithManager(System.Func<NetworkManager, bool> action)
        {
            var nm = NetworkManager.Singleton;
            if (nm == null)
            {
                // Expected until Phase 1 places a NetworkManager + transport in the scene.
                Debug.LogWarning("[NetworkBootstrap] No NetworkManager in the scene yet — multiplayer Phase 1 not wired.");
                return false;
            }
            return action(nm);
        }
    }
}
