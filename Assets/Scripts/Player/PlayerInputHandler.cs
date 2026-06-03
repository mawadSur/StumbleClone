using UnityEngine;
using UnityEngine.InputSystem;

namespace StumbleClone.Player
{
    /// Thin wrapper around UnityEngine.InputSystem.PlayerInput so other scripts
    /// can query input without taking a dependency on the InputSystem types.
    [RequireComponent(typeof(PlayerInput))]
    public sealed class PlayerInputHandler : MonoBehaviour, IPlayerInput
    {
        private const string GameplayMap = "Gameplay";

        private PlayerInput _playerInput;
        private InputAction _moveAction;
        private InputAction _lookAction;
        private InputAction _jumpAction;
        private InputAction _pushAction;
        private InputAction _pauseAction;

        public Vector2 Move => _moveAction != null ? _moveAction.ReadValue<Vector2>() : Vector2.zero;
        public Vector2 Look => _lookAction != null ? _lookAction.ReadValue<Vector2>() : Vector2.zero;

        /// True when the Look action is currently driven by a gamepad stick (a per-frame
        /// position) rather than a mouse (a per-frame delta). The camera scales the two
        /// differently so stick look isn't frame-rate dependent. Null active control
        /// (idle) reports false, which is harmless since Look is zero then.
        public bool LookFromGamepad
        {
            get
            {
                if (_lookAction == null) return false;
                var control = _lookAction.activeControl;
                return control != null && control.device is Gamepad;
            }
        }
        public bool JumpPressed => _jumpAction != null && _jumpAction.WasPressedThisFrame();
        public bool PushPressed => _pushAction != null && _pushAction.WasPressedThisFrame();
        public bool PausePressed => _pauseAction != null && _pauseAction.WasPressedThisFrame();

        /// Input is masked while the controller is in a knockback/stun window.
        public bool InputLocked { get; set; }

        public Vector2 MoveMasked => InputLocked ? Vector2.zero : Move;
        public bool JumpPressedMasked => !InputLocked && JumpPressed;
        public bool PushPressedMasked => !InputLocked && PushPressed;

        private void Awake()
        {
            _playerInput = GetComponent<PlayerInput>();
            if (_playerInput.defaultActionMap != GameplayMap && _playerInput.actions != null)
            {
                // Force the gameplay map on awake so designers don't have to set it in the inspector.
                _playerInput.defaultActionMap = GameplayMap;
            }
            CacheActions();
        }

        private void OnEnable()
        {
            if (_playerInput != null && _playerInput.actions != null)
            {
                _playerInput.SwitchCurrentActionMap(GameplayMap);
            }
            CacheActions();
        }

        private void CacheActions()
        {
            if (_playerInput == null || _playerInput.actions == null) return;
            _moveAction = _playerInput.actions.FindAction("Move", false);
            _lookAction = _playerInput.actions.FindAction("Look", false);
            _jumpAction = _playerInput.actions.FindAction("Jump", false);
            _pushAction = _playerInput.actions.FindAction("Push", false);
            _pauseAction = _playerInput.actions.FindAction("Pause", false);
        }
    }
}
