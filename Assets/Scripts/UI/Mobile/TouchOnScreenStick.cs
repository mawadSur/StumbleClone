using UnityEngine.InputSystem.OnScreen;

namespace StumbleClone.UI.Mobile
{
    /// On-screen virtual joystick that exposes a code-settable control path.
    ///
    /// The base <see cref="OnScreenStick"/> only lets the bound control path be
    /// set in the inspector (the setter for <c>controlPathInternal</c> is
    /// protected). The mobile controls overlay is built entirely from code, so
    /// this subclass surfaces a public setter. Set the path BEFORE the owning
    /// GameObject is activated — the control registers in OnEnable.
    public sealed class TouchOnScreenStick : OnScreenStick
    {
        public void SetControlPath(string path) => controlPathInternal = path;

        /// Surface the stick behaviour (e.g. dynamic origin for a drag-anywhere look pad).
        public void SetBehaviour(Behaviour b) => behaviour = b;
    }
}
