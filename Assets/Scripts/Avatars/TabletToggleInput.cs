using Smartphone;
using UnityEngine;
using UnityEngine.Assertions;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;

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

        // T23: by day, T opens the tablet straight on the players' table ("Tableur : T", the day's keys are 1, 2, T, E).
        [Tooltip("The tablet's table of players and roles, opened by the spreadsheet key by day.")]
        [SerializeField] private SmartphoneApp _spreadsheetApp;
        [Tooltip("Gates the spreadsheet key to the day (the seated Embodied mode).")]
        [SerializeField] private CameraModeChannel _cameraMode;
        [SerializeField] private Key _spreadsheetKey = Key.T;

        /// <summary>The spreadsheet key (T), shown by the key hints.</summary>
        public Key SpreadsheetKey => _spreadsheetKey;

        /// <summary>Whether the spreadsheet key works right now (by day, the table app wired).</summary>
        public bool SpreadsheetReachable =>
            _spreadsheetApp != null && _cameraMode != null && _cameraMode.Current == CameraMode.Embodied;

        private void Update()
        {
            Keyboard _keyboard = Keyboard.current;
            if (_keyboard == null || !SpreadsheetReachable || !_keyboard[_spreadsheetKey].wasPressedThisFrame
                || Presentation.DayScreenRows.IsTyping())
            {
                return;
            }
            if (!_smartphone.IsOpen)
            {
                _smartphone.TryOpenPanel();
                _smartphone.GoToApp(_spreadsheetApp);
            }
            else if (_smartphone.currentApp != _spreadsheetApp)
            {
                _smartphone.GoToApp(_spreadsheetApp);
            }
            else
            {
                _smartphone.TryClosePanel();
            }
        }

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

        /// <summary>The key bound to ToggleTablet (Tab), shown by the key hints.</summary>
        public Key ToggleKey =>
            _toggleAction != null && _toggleAction.controls.Count > 0 && _toggleAction.controls[0] is KeyControl _key
                ? _key.keyCode
                : Key.Tab;

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
