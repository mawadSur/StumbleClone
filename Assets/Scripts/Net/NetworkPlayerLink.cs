using StumbleClone.Player;
using Unity.Netcode;
using UnityEngine;

namespace StumbleClone.Net
{
    /// Phase-1 wiring component that sits on the networked Player object alongside PlayerController and
    /// NetworkInputProvider. On spawn it decides where each instance's PlayerController gets its input:
    ///
    ///  • OWNER      — leave the controller on its LOCAL PlayerInputHandler (the Awake default). The
    ///    owning client drives its own character directly for responsiveness; NetworkInputProvider just
    ///    streams that same input up so the server and other clients can see it.
    ///  • NON-OWNER  — swap the controller's input source to the replicated NetworkInputProvider via
    ///    PlayerController.SetInputSource(...). From then on this instance's movement is driven entirely
    ///    by the input the owning client sent over the wire.
    ///
    /// Every lookup is null-guarded: this component is harmless if the prefab is only partially wired, and
    /// it is DORMANT in single-player because nothing spawns a NetworkManager yet (OnNetworkSpawn never
    /// fires). It won't do anything live until the editor wiring in MULTIPLAYER_PHASE1.md exists.
    ///
    /// Compiles against NGO 2.x (Unity.Netcode 2.2.0): NetworkBehaviour, IsOwner, OnNetworkSpawn.
    [RequireComponent(typeof(NetworkInputProvider))]
    public sealed class NetworkPlayerLink : NetworkBehaviour
    {
        [Tooltip("Optional explicit references. Both are auto-resolved on this object in OnNetworkSpawn " +
                 "if left null.")]
        [SerializeField] private PlayerController playerController;
        [SerializeField] private NetworkInputProvider networkInput;

        public override void OnNetworkSpawn()
        {
            if (playerController == null) playerController = GetComponent<PlayerController>();
            if (networkInput == null) networkInput = GetComponent<NetworkInputProvider>();

            // Owner keeps its local input: PlayerController's Awake already resolved the local
            // PlayerInputHandler, and the co-located NetworkInputProvider (which streams that same input up
            // to the server) runs itself off IsOwner — so the owner needs no SetInputSource call. Bail early.
            // NetworkGame handles server-side spawn placement; this component only routes input.
            if (IsOwner) return;

            // Remote instance: drive it from replicated input. Guard both refs — a partially-wired
            // prefab must not throw and abort the spawn.
            if (playerController != null && networkInput != null)
            {
                playerController.SetInputSource(networkInput);
            }
            else
            {
                Debug.LogWarning(
                    "[NetworkPlayerLink] Non-owner spawned without a PlayerController and/or " +
                    "NetworkInputProvider on this object — remote input not wired. Check Player.prefab " +
                    "(see Assets/Scripts/Net/MULTIPLAYER_PHASE1.md).",
                    this);
            }
        }
    }
}
