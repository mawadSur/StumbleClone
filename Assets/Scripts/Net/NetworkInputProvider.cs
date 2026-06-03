using StumbleClone.Player;
using Unity.Netcode;
using UnityEngine;

namespace StumbleClone.Net
{
    /// Phase-1 networked input source. This NetworkBehaviour implements the SAME
    /// <see cref="StumbleClone.Player.IPlayerInput"/> surface the local PlayerInputHandler does, so the
    /// movement code in PlayerController is identical whether it reads local device input or replicated
    /// remote input — that was the whole point of the input seam (see IPlayerInput / SetInputSource).
    ///
    /// FLOW:
    ///  • OWNER  — each tick, read the local PlayerInputHandler on this object and push its state up to
    ///    the server (an analog Move/Look pair as NetworkVariables we write directly because we own the
    ///    object, plus edge-triggered Jump/Push/Pause sent as ServerRpcs so a single press isn't lost or
    ///    doubled by replication timing). The owner's own PlayerController keeps reading the LOCAL handler
    ///    (NetworkPlayerLink only swaps the input source on non-owners), so this provider exists mainly to
    ///    feed the server/other clients.
    ///  • NON-OWNER (server + remote clients) — the replicated NetworkVariables ARE the input. The
    ///    IPlayerInput getters below read them, so a remote PlayerController is driven entirely by what
    ///    the owning client sent.
    ///
    /// EDGE HANDLING (documented approximation): "pressed this frame" is inherently a one-frame edge that
    /// does NOT survive replication cleanly (a NetworkVariable that flips true→false can be missed, and an
    /// RPC arrives on an arbitrary later tick). We model each press as a monotonically increasing counter
    /// (NetworkVariable<int>) that the owner bumps on the press edge. A non-owner reports "pressed" for
    /// exactly ONE consume by comparing the replicated counter against a locally-remembered last-consumed
    /// value. This can coalesce two presses landing in the same replication window into one, and adds up to
    /// ~1 tick of latency — acceptable for Phase-1 groundwork; Phase-2 prediction tightens it.
    ///
    /// Compiles against NGO 2.x (Unity.Netcode 2.2.0): NetworkBehaviour, NetworkVariable, ServerRpc,
    /// IsOwner, OnNetworkSpawn. DORMANT — nothing spawns a NetworkManager yet, so this never runs in
    /// single-player.
    public sealed class NetworkInputProvider : NetworkBehaviour, IPlayerInput
    {
        [Tooltip("Optional explicit local input handler to read on the owner. Auto-resolved on this " +
                 "object (then scene-wide) in OnNetworkSpawn if left null.")]
        [SerializeField] private PlayerInputHandler localInput;

        // ---- Replicated analog state. Owner-writable so the owning client streams its own input;
        // everyone reads. ----
        private readonly NetworkVariable<Vector2> _netMove = new NetworkVariable<Vector2>(
            Vector2.zero, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);

        private readonly NetworkVariable<Vector2> _netLook = new NetworkVariable<Vector2>(
            Vector2.zero, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);

        private readonly NetworkVariable<bool> _netLookFromGamepad = new NetworkVariable<bool>(
            false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);

        // ---- Edge counters. Bumped by the owner on each press edge; consumed once by non-owners.
        // Written via ServerRpc (not owner-write) so the press can't be dropped by a coalesced
        // NetworkVariable delta when it toggles within a single replication window. ----
        private readonly NetworkVariable<int> _jumpCount = new NetworkVariable<int>(
            0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        private readonly NetworkVariable<int> _pushCount = new NetworkVariable<int>(
            0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        private readonly NetworkVariable<int> _pauseCount = new NetworkVariable<int>(
            0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        // Last-consumed edge counters on the reading (non-owner) side. A getter returns true once per
        // increment, then records the new value so it won't fire again until the next press.
        private int _jumpConsumed;
        private int _pushConsumed;
        private int _pauseConsumed;

        public override void OnNetworkSpawn()
        {
            // Only the owner needs a local handler to read from. Non-owners are pure consumers of the
            // replicated values, so a missing handler there is expected and harmless.
            if (IsOwner && localInput == null)
            {
                localInput = GetComponent<PlayerInputHandler>();
                if (localInput == null) localInput = FindAnyObjectByType<PlayerInputHandler>();
            }

            // Seed the consume cursors to the current replicated values so a late-joining reader doesn't
            // fire a spurious "pressed" for presses that happened before it spawned.
            _jumpConsumed = _jumpCount.Value;
            _pushConsumed = _pushCount.Value;
            _pauseConsumed = _pauseCount.Value;
        }

        private void Update()
        {
            if (!IsOwner || localInput == null) return;

            // Stream analog state. These are owner-writable NetworkVariables; NGO only sends a delta when
            // the value actually changes, so a still player costs nothing.
            _netMove.Value = localInput.Move;
            _netLook.Value = localInput.Look;
            _netLookFromGamepad.Value = localInput.LookFromGamepad;

            // Forward press edges to the server, which owns the authoritative counters. Reading the
            // raw (unmasked) press here keeps masking a server/remote concern — InputLocked is applied
            // through the masked getters below, exactly like the local handler.
            if (localInput.JumpPressed) RaiseJumpServerRpc();
            if (localInput.PushPressed) RaisePushServerRpc();
            if (localInput.PausePressed) RaisePauseServerRpc();
        }

        [ServerRpc]
        private void RaiseJumpServerRpc() => _jumpCount.Value++;

        [ServerRpc]
        private void RaisePushServerRpc() => _pushCount.Value++;

        [ServerRpc]
        private void RaisePauseServerRpc() => _pauseCount.Value++;

        // ---- IPlayerInput: analog ----

        public Vector2 Move => _netMove.Value;
        public Vector2 Look => _netLook.Value;
        public bool LookFromGamepad => _netLookFromGamepad.Value;

        // ---- IPlayerInput: edge-triggered. Each getter returns true exactly once per replicated
        // increment, then advances its local cursor. Note these are NOT idempotent — reading consumes
        // the edge, matching WasPressedThisFrame() semantics where the controller reads each once a
        // frame. ----

        public bool JumpPressed => ConsumeEdge(_jumpCount.Value, ref _jumpConsumed);
        public bool PushPressed => ConsumeEdge(_pushCount.Value, ref _pushConsumed);
        public bool PausePressed => ConsumeEdge(_pauseCount.Value, ref _pauseConsumed);

        private static bool ConsumeEdge(int current, ref int consumed)
        {
            if (current != consumed)
            {
                // Jump straight to current (don't single-step) so a burst of presses that arrived in one
                // replication window resolves to a single edge rather than queuing phantom presses.
                consumed = current;
                return true;
            }
            return false;
        }

        // ---- IPlayerInput: masking. Mirrors PlayerInputHandler so the PlayerController's knockback/stun
        // window behaves identically for a remote player. InputLocked is set by the controller each tick
        // (UpdateInputLock) and is a purely local concern on the reading side. ----

        public bool InputLocked { get; set; }

        public Vector2 MoveMasked => InputLocked ? Vector2.zero : Move;
        public bool JumpPressedMasked => !InputLocked && JumpPressed;
        public bool PushPressedMasked => !InputLocked && PushPressed;
    }
}
