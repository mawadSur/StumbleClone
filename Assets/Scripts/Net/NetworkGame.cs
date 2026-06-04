using System.Collections.Generic;
using StumbleClone.Bots;
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

        /// Target number of racers in an online match (humans + backfill bots), i.e. the lobby's max
        /// player count. The server tops up with bots so a thin lobby still feels full. Kept here (not in
        /// GameConstants, which this file doesn't own) and mirrors MultiplayerService's default MaxPlayers=8.
        public const int NetworkedFieldSize = 8;

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
            nm.OnClientDisconnectCallback += OnClientDisconnected;
            nm.OnServerStarted += OnServerStarted;
            s_callbacksHooked = true;
        }

        private static void OnServerStarted()
        {
            // Host/server: the local (host) client connects in the same flow; OnClientConnected places it.
            // Switch the scene's BotSpawner into networked mode so it does NOT run its offline auto-fill —
            // this server now owns the bot count and backfills empty slots itself (BackfillBots below).
            // Then do an initial backfill in case the spawner already spawned/Started before we got here.
            var spawner = GetBotSpawner();
            if (spawner != null) spawner.SetNetworkedMode(true);
            BackfillBots();
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
            }
            else
            {
                PlaceAtSpawnPoint(playerObject, clientId);
            }

            // A human took a slot: shed a backfill bot to keep the field at NetworkedFieldSize.
            BackfillBots();
        }

        /// Fires on the SERVER when a client leaves. Their owned Player despawns automatically; we top the
        /// field back up with a bot so a lobby that empties out still feels full.
        private static void OnClientDisconnected(ulong clientId)
        {
            var nm = NetworkManager.Singleton;
            if (nm == null || !nm.IsServer) return; // backfill is a server-authority concern only
            BackfillBots();
        }

        /// Reconcile the bot count so humans + bots == <see cref="NetworkedFieldSize"/>. Server-side and
        /// live-session only; the offline single-player path never reaches here (no NetworkManager listening),
        /// so BotSpawner's own auto-fill of 7 is untouched. Adds bots when humans are scarce, removes them
        /// when humans join. Safe to call repeatedly (it only spawns/despawns the delta).
        private static void BackfillBots()
        {
            var nm = NetworkManager.Singleton;
            if (nm == null || !nm.IsServer || !nm.IsListening) return;

            var spawner = GetBotSpawner();
            if (spawner == null) return; // scene has no bots (e.g. a lobby-only scene) — nothing to backfill

            // Always re-assert networked mode here: if this spawner's Start() runs AFTER the session is
            // already live (e.g. a gameplay scene loaded post-host), this is the point at which we suppress
            // its offline auto-fill — and even if it already auto-filled 7, the reconcile below trims to target.
            spawner.SetNetworkedMode(true);

            // ConnectedClients exists only on the server and counts every connected human (incl. the host).
            int humans = nm.ConnectedClientsIds != null ? nm.ConnectedClientsIds.Count : 0;
            int desiredBots = Mathf.Max(0, NetworkedFieldSize - humans);
            int currentBots = spawner.SpawnedBotCount;

            if (currentBots < desiredBots)
                spawner.SpawnBots(desiredBots - currentBots);
            else if (currentBots > desiredBots)
                spawner.DespawnExtraBots(currentBots - desiredBots);
        }

        /// Find the active scene's BotSpawner (one per gameplay scene). Returns null in scenes without bots.
        private static BotSpawner GetBotSpawner() => Object.FindAnyObjectByType<BotSpawner>();

        // ---- Per-scene self-bootstrap -------------------------------------------------------------------
        // The session may already be live (host started in the menu/lobby) when a gameplay scene loads, so
        // the new scene's BotSpawner would otherwise run its offline auto-fill before any NGO callback fires.
        // We subscribe to SceneManager.sceneLoaded (the same always-on pattern SceneAtmosphere/EliminationFeed
        // use) and reconcile the bot count for the freshly loaded scene. No-ops entirely when not a live server,
        // so single-player is unaffected.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Bootstrap()
        {
            UnityEngine.SceneManagement.SceneManager.sceneLoaded -= OnSceneLoaded; // guard double-subscribe (domain reload off)
            UnityEngine.SceneManagement.SceneManager.sceneLoaded += OnSceneLoaded;
        }

        private static void OnSceneLoaded(
            UnityEngine.SceneManagement.Scene scene,
            UnityEngine.SceneManagement.LoadSceneMode loadMode)
        {
            // sceneLoaded fires before the scene objects' first Start(), so setting networked mode inside
            // BackfillBots here suppresses the BotSpawner's offline auto-fill in time. Only acts as a live
            // server — offline (NetworkManager not listening) this returns immediately.
            BackfillBots();
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
