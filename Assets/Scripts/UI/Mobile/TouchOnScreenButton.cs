using UnityEngine.InputSystem.OnScreen;

namespace StumbleClone.UI.Mobile
{
    /// On-screen button that exposes a code-settable control path.
    /// See <see cref="TouchOnScreenStick"/> for why the subclass exists.
    public sealed class TouchOnScreenButton : OnScreenButton
    {
        public void SetControlPath(string path) => controlPathInternal = path;
    }
}
