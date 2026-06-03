using UnityEngine;

namespace StumbleClone.Player
{
    /// Input abstraction the PlayerController reads from. Today the only implementation is
    /// PlayerInputHandler (local device input). This seam is the first concrete step toward
    /// multiplayer (Phase 0 of the feasibility plan): a future NetworkInputProvider implements
    /// this same interface to drive a remote player from replicated input, with NO change to the
    /// movement code — the controller neither knows nor cares whether input is local or networked.
    public interface IPlayerInput
    {
        Vector2 Move { get; }
        Vector2 Look { get; }
        bool LookFromGamepad { get; }
        bool JumpPressed { get; }
        bool PushPressed { get; }
        bool PausePressed { get; }

        /// Masked while the controller is in a knockback/stun window.
        bool InputLocked { get; set; }
        Vector2 MoveMasked { get; }
        bool JumpPressedMasked { get; }
        bool PushPressedMasked { get; }
    }
}
