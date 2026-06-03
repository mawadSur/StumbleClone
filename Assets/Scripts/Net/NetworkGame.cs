using System.Collections.Generic;
using StumbleClone.Core;
using Unity.Netcode;
using Unity.Netcode.Components;
using UnityEngine;

namespace StumbleClone.Net
{
    /// Runtime glue between Netcode for GameObjects and the StumbleClone gameplay objects.
    ///
    /// RESPONSIBILITIES (Phase 1):
    ///  • <see cref="EnsureManager"/> — instantiate the one NetworkManager (with UnityTransport +
    ///    NetworkConfig.PlayerPrefab) from the Resources prefab that <c>MultiplayerSetup</c> created, so a
    ///    session can be created/joined in any scene and in a headless build, with zero per-scene wiring.
    ///  • Server-side spawn positioning — NGO + NetworkConfig.PlayerPrefab auto-spawns one owned Player per
    ///    connected client. We don't spawn anything ourselves; we just place each freshly-connected client's
    ///    player at one of the scene's "Respawn" spawn points (server authority over the NetworkTransform).
    ///  • Single-player guard — everything no-ops unless a NetworkManager is actually listening, so the
    ///    offline game is completely unaffected.
    ///
    /// Per-player INPUT routing (owner = local input, remote = replicated input) lives in
    /// <see cref="NetworkPlayerLink"/>; this class only handles the manager lifecycle + placement.
    ///
    /// Compiles against NGO 2.x (Unity.Netcode 2.2.0): NetworkManager.Singleton, OnServerStarted,
    /// OnClientConnectedCallback, NetworkSpawnManager, NetworkTransform.Teleport.
    public static class NetworkGame
    {
        private static bool s_callbacksHooked;

        /// True once a host/server/client session is live (mirrors NetworkBootstrap.IsOnline; kept here so
        /// callers that only use NetworkGame don't need both types).
        public static bool IsOnline =>
            NetworkManager.Singleton != null &&
            (NetworkManager.Singleton.IsServer || NetworkManager.Singleton.IsClient);

        /// Ensures a single <see cref="NetworkManager"/> exists before a session is created/joined.
        ///
        /// The Multiplayer Services SDK's NGO handler drives <c>NetworkManager.Singleton</c> directly, so it
        /// must exist (with a UnityTransport and a PlayerPrefab) first. This loads the prefab that
        /// <c>StumbleClone/Multiplayer/Setup</c> produced under Resources and instantiates it as a persistent
        /// (DontDestroyOnLoad) singleton. Idempotent: returns the existing singleton if one is already present.
        ///
        /// Returns the NetworkManager, or null with an error if the Resources prefab is missing (i.e. the
        /// editor/headless Setup step has not been run). Call this once before
        /// <c>MultiplayerService.Instance.CreateSessionAsync</c> / <c>JoinSessionByCodeAsync</c>.
        public static NetworkManager EnsureManager()
        {
            if (NetworkManager.Singleton != null)
            {
                HookCallbacks();
                return NetworkManager.Singleton;
            }

            var prefab = Resources.Load<GameObject>(EditorSetupResourcePath);
            if (prefab == null)
            {
                Debug.LogError("[NetworkGame] Could not load the NetworkManager prefab from " +
                    $"Resources/{EditorSetupResourcePath}. Run StumbleClone/Multiplayer/Setup once " +
                    "(or ensure it runs in the build) before starting a session. " +
                    "See Assets/Scripts/Net/MULTIPLAYER_PHASE1.md.");
                return null;
            }

            var go = Object.Instantiate(prefab);
            go.name = "NetworkManager"; // strip the "(Clone)" suffix
            Object.DontDestroyOnLoad(go);

            HookCallbacks();
            return NetworkManager.Singleton;
        }

        // Kept in sync with MultiplayerSetup.NetworkManagerResourcePath. Duplicated as a literal (rather than
        // referencing the editor-only type) so this runtime file has no editor dependency and compiles in builds.
        private const string EditorSetupResourcePath = "Net/NetworkManager";

        /// Subscribe to NGO lifecycle callbacks exactly once. Safe to call repeatedly.
        private static void HookCallbacks()
        {
            if (s_callbacksHooked) return;
            var nm = NetworkManager.Singleton;
            if (nm == null) return;

            nm.OnClientConnectedCallback += OnClientConnected;
            nm.OnServerStarted += OnServerStarted;
            s_callbacksHooked = true;
        }

        private static void OnServerStarted()
        {
            // Host/server: the local (host) client connects in the same flow; OnClientConnected places it.
            // Nothing extra to do here for Phase 1 — kept as a clear extension point (replicated registry /
            // win logic init belongs here in Phase 2).
        }

        /// Fires on the SERVER for every client that connects (including the host's own client). NGO has
        /// already auto-spawned that client's owned Player by now. We position it at a spawn point so players
        /// don't stack on the prefab origin. Position is set on the server and replicated via NetworkTransform.
        private static void OnClientConnected(ulong clientId)
        {
            var nm = NetworkManager.Singleton;
            if (nm == null || !nm.IsServer) return; // placement is a server-authority concern only

            var playerObject = nm.SpawnManager != null
                ? nm.SpawnManager.GetPlayerNetworkObject(clientId)
                : null;
            if (playerObject == null)
            {
                // AutoSpawnPlayerPrefab may not have run yet, or PlayerPrefab is unset. Either way there is
                // nothing to place; NetworkPlayerLink still wires input on the object once it spawns.
                Debug.LogWarning($"[NetworkGame] No player object for client {clientId} on connect — " +
                    "skipping spawn placement (is NetworkConfig.PlayerPrefab set? run StumbleClone/Multiplayer/Setup).");
                return;
            }

            PlaceAtSpawnPoint(playerObject, clientId);
        }

        /// Move the given player object to a spawn point. Uses the scene's "Respawn"-tagged transforms
        /// (GameConstants.TagRespawnPoint), distributing players across them by client order; falls back to
        /// the object's current position if none exist. Server-authoritative teleport so the move replicates
        /// cleanly without NetworkTransform interpolating across the map.
        private static void PlaceAtSpawnPoint(NetworkObject playerObject, ulong clientId)
        {
            var spawnPoints = GetSpawnPoints();
            if (spawnPoints.Count == 0) return; // leave at prefab/scene position

            // Spread players over the available points deterministically by client id.
            int index = (int)(clientId % (ulong)spawnPoints.Count);
            Transform point = spawnPoints[index];

            var netTransform = playerObject.GetComponent<NetworkTransform>();
            if (netTransform != null)
            {
                // Teleport bypasses interpolation — the player appears at the spawn point, not slides to it.
                netTransform.Teleport(point.position, point.rotation, playerObject.transform.localScale);
            }
            else
            {
                playerObject.transform.SetPositionAndRotation(point.position, point.rotation);
            }
        }

        private static readonly List<Transform> s_spawnPointBuffer = new List<Transform>();

        /// Collects the scene's spawn-point transforms (objects tagged GameConstants.TagRespawnPoint).
        /// Reuses a static buffer to avoid per-connect allocations. The tag may be undefined in a scene that
        /// never set it up; GameObject.FindGameObjectsWithTag throws on an unknown tag, so we guard it.
        private static List<Transform> GetSpawnPoints()
        {
            s_spawnPointBuffer.Clear();
            GameObject[] tagged;
            try
            {
                tagged = GameObject.FindGameObjectsWithTag(GameConstants.TagRespawnPoint);
            }
            catch (UnityException)
            {
                // Tag not defined in this project/scene — no spawn points to use.
                return s_spawnPointBuffer;
            }

            foreach (var go in tagged)
            {
                if (go != null) s_spawnPointBuffer.Add(go.transform);
            }
            return s_spawnPointBuffer;
        }
    }
}
