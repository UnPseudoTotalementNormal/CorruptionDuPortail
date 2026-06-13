using Smartphone;
using UnityEngine;
using UnityEngine.Assertions;
using UnityEngine.InputSystem;

namespace Avatars
{
    /// <summary>
    /// Story 13.2 (Epic 13 — Player Embodiment, DO1). Input-driven open/close of the smartphone/tablet so it
    /// stays reachable while free-roaming the Lobby — the board-camera <c>openOnCamera</c> trigger is
    /// unavailable when walking (no board camera is active). This is ADDITIVE and DECOUPLED: it calls the
    /// already-public <see cref="SmartphoneController.TryOpenPanel"/> / <see cref="SmartphoneController.TryClosePanel"/>,
    /// while the camera-coupled open path (SmartphoneController.cs:68-72) stays untouched for the other states.
    ///
    /// Reads a RUNTIME CLONE of the Input asset's ToggleTablet action (isolated from the scene PlayerInput),
    /// the same Input-System-direct approach as <see cref="AvatarMovementController"/>.
    /// </summary>
    public class TabletToggleInput : MonoBehaviour
    {
        [SerializeField] private SmartphoneController _smartphone;
        [SerializeField] private InputActionAsset _inputActions;

        private InputActionAsset _runtimeActions;
        private InputAction _toggleAction;

        private void Awake()
        {
            Assert.IsNotNull(_smartphone, "TabletToggleInput._smartphone is not wired — wire the SmartphoneController.");
            Assert.IsNotNull(_inputActions, "TabletToggleInput._inputActions is not wired — wire the project InputActionAsset.");

            _runtimeActions = Instantiate(_inputActions);
            InputActionMap _playerMap = _runtimeActions.FindActionMap("Player", throwIfNotFound: true);
            _toggleAction = _playerMap.FindAction("ToggleTablet", throwIfNotFound: true);
            _toggleAction.performed += OnTogglePressed;
            _playerMap.Enable();
        }

        private void OnDestroy()
        {
            if (_toggleAction != null)
            {
                _toggleAction.performed -= OnTogglePressed;
            }
            if (_runtimeActions != null)
            {
                _runtimeActions.Disable();
                Destroy(_runtimeActions);
                _runtimeActions = null;
            }
        }

        private void OnTogglePressed(InputAction.CallbackContext _context)
        {
            if (_smartphone.IsOpen)
            {
                _smartphone.TryClosePanel();
            }
            else
            {
                _smartphone.TryOpenPanel();
            }
        }
    }
}
