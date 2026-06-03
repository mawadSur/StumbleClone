using Unity.Netcode;
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

        public static bool StartHost() => WithManager(nm => nm.StartHost());
        public static bool StartClient() => WithManager(nm => nm.StartClient());

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
