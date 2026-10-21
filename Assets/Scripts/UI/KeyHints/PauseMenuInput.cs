using Avatars;
using TMPro;
using UnityEngine;
using UnityEngine.Assertions;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

namespace UI.KeyHints
{
    /// <summary>
    /// Escape opens and closes the pause menu (until now only its top-left button did, unreachable with the cursor locked
    /// in first person). While the menu is open the camera arbiter frees the cursor and freezes the look, like the tablet.
    /// The key hints show Échap (mockups touches v2 K5). Escape is left alone while another overlay is up (the playtest bug
    /// report window closes on it) and while the player types in the chat.
    /// </summary>
    public class PauseMenuInput : MonoBehaviour
    {
        // Lane A: wired in GameScene.
        [SerializeField] private PanelComponent _pausePanel;
        [SerializeField] private AvatarCameraArbiter _arbiter;

        private bool _otherOverlayLastFrame;

        public Key MenuKey => Key.Escape;

        public bool IsMenuOpen => _pausePanel.isPanelOpen;

        private void Awake()
        {
            Assert.IsNotNull(_pausePanel, "PauseMenuInput._pausePanel is not wired — wire the PausePanel in GameScene.");
            Assert.IsNotNull(_arbiter, "PauseMenuInput._arbiter is not wired — wire the AvatarCameraArbiter in GameScene.");
        }

        private void Update()
        {
            Keyboard _keyboard = Keyboard.current;
            if (_keyboard == null || !_keyboard[MenuKey].wasPressedThisFrame || _otherOverlayLastFrame || IsTyping())
            {
                return;
            }
            _pausePanel.SwitchPanelOpen();
        }

        // Polled, not evented: the menu's own button opens and closes it too.
        private void LateUpdate()
        {
            _arbiter.SetOverlayOpen(this, _pausePanel.isPanelOpen);
            // Kept for the next frame's Escape: an overlay that closes on that same Escape, in an Update that may run
            // before this one, was still up here.
            _otherOverlayLastFrame = _arbiter.IsOverlayOpenBesides(this);
        }

        private void OnDisable()
        {
            if (_arbiter != null) _arbiter.SetOverlayOpen(this, false);
        }

        private static bool IsTyping()
        {
            GameObject _selected = EventSystem.current != null ? EventSystem.current.currentSelectedGameObject : null;
            return _selected != null && _selected.TryGetComponent(out TMP_InputField _field) && _field.isFocused;
        }
    }
}
