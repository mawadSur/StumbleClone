#if UNITY_EDITOR
using System.IO;
using StumbleClone.Net;
using Unity.Netcode;
using Unity.Netcode.Components;
using Unity.Netcode.Transports.UTP;
using UnityEditor;
using UnityEngine;

namespace StumbleClone.EditorTools
{
    /// One-call, idempotent editor wiring for the Phase-1 multiplayer stack (Netcode for GameObjects +
    /// Unity Multiplayer Services SDK). Run it from the menu (<b>StumbleClone &gt; Multiplayer &gt; Setup</b>)
    /// or headlessly via <c>-executeMethod StumbleClone.EditorTools.MultiplayerSetup.Run</c>.
    ///
    /// What it does (all safe to repeat):
    ///   1. Turns <c>Assets/Prefabs/Player.prefab</c> into a spawnable NetworkObject by ensuring it has
    ///      <see cref="NetworkObject"/>, <see cref="NetworkTransform"/> (so position/rotation replicate),
    ///      <see cref="NetworkInputProvider"/> and <see cref="NetworkPlayerLink"/>.
    ///   2. Builds a <c>NetworkManager</c> prefab under <c>Assets/Resources/Net/NetworkManager.prefab</c>
    ///      carrying <see cref="NetworkManager"/> + <see cref="UnityTransport"/>, with
    ///      <c>NetworkConfig.PlayerPrefab</c> = Player.prefab (also registered in the NetworkPrefabs list).
    ///      It lives under a Resources folder so the runtime <see cref="NetworkGame"/> bootstrap can
    ///      instantiate it on demand in any scene and in a headless/CI build — no per-scene placement
    ///      and no manual inspector wiring required.
    ///
    /// DESIGN NOTE — why a Resources prefab rather than a scene object: the Multiplayer Services SDK's
    /// NGO network handler drives <c>NetworkManager.Singleton</c> directly (see the package's
    /// GameObjectsNetcodeNetworkHandler). It needs exactly one NetworkManager with a UnityTransport to
    /// exist before a session is created/joined. Spawning it from one Resources prefab via code keeps a
    /// single source of truth, works in every gameplay scene, and is fully reproducible headlessly — the
    /// brittle alternative would be hand-placing (and re-syncing) a NetworkManager in each scene.
    public static class MultiplayerSetup
    {
        private const string PlayerPrefabPath = "Assets/Prefabs/Player.prefab";
        private const string ResourcesDir = "Assets/Resources";
        private const string NetResourcesDir = "Assets/Resources/Net";

        /// Resources-relative path (no extension) used by NetworkGame.Load to fetch the manager prefab.
        public const string NetworkManagerResourcePath = "Net/NetworkManager";
        private const string NetworkManagerPrefabPath = NetResourcesDir + "/NetworkManager.prefab";

        private const string LogPrefix = "[MultiplayerSetup] ";

        [MenuItem("StumbleClone/Multiplayer/Setup")]
        public static void Run()
        {
            int changes = 0;
            changes += WirePlayerPrefab();
            changes += EnsureNetworkManagerPrefab();

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log(LogPrefix + (changes == 0
                ? "Already fully wired — nothing to change. NGO multiplayer is ready."
                : $"Done. Applied {changes} change(s). Player.prefab is a NetworkObject and "
                  + $"{NetworkManagerPrefabPath} is ready (NetworkGame loads it at runtime)."));
        }

        // ---- 1. Player.prefab -> NetworkObject --------------------------------------------------------

        /// Adds the networking components to Player.prefab if missing. Edits the prefab contents directly
        /// (PrefabUtility load/save) so the change persists on disk. Returns the number of components added.
        private static int WirePlayerPrefab()
        {
            if (!File.Exists(PlayerPrefabPath))
            {
                Debug.LogError(LogPrefix + $"Player prefab not found at {PlayerPrefabPath}. " +
                    "Run StumbleClone/Bootstrap MVP first.");
                return 0;
            }

            GameObject root = PrefabUtility.LoadPrefabContents(PlayerPrefabPath);
            int added = 0;
            try
            {
                // NetworkObject is mandatory on every spawnable networked object and must come first
                // (NetworkBehaviours require it on the same object).
                if (root.GetComponent<NetworkObject>() == null)
                {
                    root.AddComponent<NetworkObject>();
                    added++;
                    Debug.Log(LogPrefix + "Added NetworkObject to Player.prefab.");
                }

                // NetworkTransform replicates position/rotation so remote players are visible where they
                // actually are (Phase-1 input replication alone would let positions drift). Server-authoritative
                // by default in NGO 2.x, which matches the host-authoritative session model.
                if (root.GetComponent<NetworkTransform>() == null)
                {
                    root.AddComponent<NetworkTransform>();
                    added++;
                    Debug.Log(LogPrefix + "Added NetworkTransform to Player.prefab.");
                }

                if (root.GetComponent<NetworkInputProvider>() == null)
                {
                    root.AddComponent<NetworkInputProvider>();
                    added++;
                    Debug.Log(LogPrefix + "Added NetworkInputProvider to Player.prefab.");
                }

                if (root.GetComponent<NetworkPlayerLink>() == null)
                {
                    root.AddComponent<NetworkPlayerLink>();
                    added++;
                    Debug.Log(LogPrefix + "Added NetworkPlayerLink to Player.prefab.");
                }

                if (added > 0)
                {
                    PrefabUtility.SaveAsPrefabAsset(root, PlayerPrefabPath);
                }
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }

            return added;
        }

        // ---- 2. NetworkManager prefab (Resources/Net) -------------------------------------------------

        /// Ensures Assets/Resources/Net/NetworkManager.prefab exists and is wired: NetworkManager +
        /// UnityTransport, NetworkConfig.PlayerPrefab = Player.prefab (which both auto-spawns the player and
        /// registers the prefab), and auto-connect/approval OFF (sessions are started by code only).
        /// Returns 1 if it created or re-wired the prefab, else 0.
        private static int EnsureNetworkManagerPrefab()
        {
            var playerPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPrefabPath);
            if (playerPrefab == null)
            {
                Debug.LogError(LogPrefix + $"Cannot wire NetworkManager — Player prefab missing at {PlayerPrefabPath}.");
                return 0;
            }

            EnsureFolder(ResourcesDir);
            EnsureFolder(NetResourcesDir);

            var existing = AssetDatabase.LoadAssetAtPath<GameObject>(NetworkManagerPrefabPath);

            // Build/refresh on a throwaway scene instance, then save. Doing it on a fresh GameObject (not
            // PrefabUtility.LoadPrefabContents) keeps component-add ordering simple and is idempotent —
            // we re-derive the full desired state every run and overwrite the asset.
            var temp = new GameObject("NetworkManager");
            bool changed = false;
            try
            {
                var nm = temp.GetComponent<NetworkManager>();
                if (nm == null) nm = temp.AddComponent<NetworkManager>();

                var transport = temp.GetComponent<UnityTransport>();
                if (transport == null) transport = temp.AddComponent<UnityTransport>();

                // NetworkConfig is non-null on a fresh NetworkManager. Wire transport + player prefab.
                if (nm.NetworkConfig == null) nm.NetworkConfig = new NetworkConfig();
                nm.NetworkConfig.NetworkTransport = transport;

                // PlayerPrefab is the serialized field NGO reads to auto-create + auto-spawn one owned
                // Player per connected client (and it implicitly registers the prefab). This is the
                // mechanism the whole Phase-1 flow keys off (IsOwner). NetworkConfig.Prefabs' runtime
                // Add() is NOT serialized into a saved prefab, so we deliberately rely on PlayerPrefab
                // only — adding to the list here would silently no-op on the saved asset and mislead.
                nm.NetworkConfig.PlayerPrefab = playerPrefab;

                // Dormant by default: never auto-start. Sessions are created/joined exclusively through the
                // Multiplayer Services SDK (NetworkGame), which calls StartHost/StartClient on the singleton.
                nm.NetworkConfig.ConnectionApproval = false;

                // Default UTP endpoint; the SDK's Relay handler overwrites this with allocation data when a
                // session starts, so the literal value here only matters for a direct/LAN fallback.
                transport.SetConnectionData("127.0.0.1", 7777);

                // Save (create or overwrite). SaveAsPrefabAsset is idempotent and returns the asset.
                PrefabUtility.SaveAsPrefabAsset(temp, NetworkManagerPrefabPath);
                changed = existing == null; // count "created" as a change; overwrite of identical content is silent
                Debug.Log(LogPrefix + (existing == null
                    ? $"Created {NetworkManagerPrefabPath} (NetworkManager + UnityTransport, PlayerPrefab set)."
                    : $"Refreshed {NetworkManagerPrefabPath} (NetworkManager + UnityTransport, PlayerPrefab set)."));
            }
            finally
            {
                Object.DestroyImmediate(temp);
            }

            return changed ? 1 : 0;
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = Path.GetDirectoryName(path)?.Replace('\\', '/');
            string leaf = Path.GetFileName(path);
            if (!string.IsNullOrEmpty(parent) && !AssetDatabase.IsValidFolder(parent))
                EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, leaf);
        }
    }
}
#endif
