using Unity.Netcode;
using UI.EmoteWheel;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Avatars
{
    /// <summary>
    /// Local, non-networked driver of the embodied emote wheel. While the emote key is held in the embodied
    /// first-person vote (<see cref="CameraModeChannel.Current"/> == <c>Embodied</c>) it opens the
    /// <see cref="EmoteWheelController"/>, feeds it the pointed-at emote each frame (via
    /// <see cref="EmoteWheelSelection"/>), and on release plays the highlighted emote on the LOCAL owned avatar
    /// (<see cref="PlayerAvatar.RequestEmote"/> → server → NetworkAnimator, so everyone sees it).
    ///
    /// POINTING (per Poyo): while the OS cursor is LOCKED (the normal first-person state) the wheel is driven by
    /// an accumulated MOUSE-DELTA virtual stick — flick a direction to point at an emote. If the cursor is free
    /// for any reason (tablet open, board overview), it falls back to a plain HOVER of the pointer over the
    /// wheel centre — same selection math, no special-casing.
    ///
    /// Input-System-direct (Keyboard/Mouse.current), the local low-level path already used by the embodied
    /// camera; the emote key is a local HUD toggle, not a networked/bot-routed command, so it needs no
    /// InputActionAsset wiring (a future migration to a named "EmoteWheel" action is trivial — swap the reads).
    /// </summary>
    public class EmoteWheelInput : MonoBehaviour
    {
        [Tooltip("The wheel UI this drives. Wire the EmoteWheelController in the GameScene.")]
        [SerializeField] private EmoteWheelController wheel;

        [Tooltip("Camera-mode broadcast (arbiter-written). The wheel is only reachable in Embodied. Wire the " +
                 "shared CameraModeChannel asset.")]
        [SerializeField] private CameraModeChannel cameraModeChannel;

        [Header("Feel — placeholder defaults, Poyo-tuned")]
        [Tooltip("Key held to open the wheel (release to play the highlighted emote).")]
        [SerializeField] private Key openKey = Key.T;

        [Tooltip("Mouse-delta -> virtual-stick gain while the cursor is locked.")]
        [SerializeField] private float stickSensitivity = 1f;

        [Tooltip("Max virtual-stick reach (px). Keep near the wheel ring radius so a full flick reaches a slot.")]
        [SerializeField] private float stickMaxRadius = 190f;

        [Tooltip("Minimum pointing distance (px) before an emote is selected; inside it = no selection.")]
        [SerializeField] private float deadzone = 40f;

        private bool _open;
        // Accumulated virtual-stick offset from centre while the cursor is locked (reset each open).
        private Vector2 _stick;
        private PlayerAvatar _localAvatar;

        private void Update()
        {
            Keyboard _keyboard = Keyboard.current;
            if (_keyboard == null) return;

            bool _held = _keyboard[openKey].isPressed;
            bool _pressed = _keyboard[openKey].wasPressedThisFrame;
            bool _released = _keyboard[openKey].wasReleasedThisFrame;

            // Leaving the embodied view mid-hold: close WITHOUT playing (the wheel is embodied-only).
            if (_open && !IsEmbodied())
            {
                CloseWheel(playSelection: false);
                return;
            }

            if (_pressed && !_open && IsEmbodied())
            {
                OpenWheel();
            }

            if (_open && _held)
            {
                UpdateSelection();
            }

            if (_open && _released)
            {
                CloseWheel(playSelection: true);
            }
        }

        private bool IsEmbodied() =>
            cameraModeChannel != null && cameraModeChannel.Current == CameraMode.Embodied;

        private void OpenWheel()
        {
            _open = true;
            _stick = Vector2.zero;
            wheel?.Open();
        }

        // Compute the pointed-at emote and highlight it. Locked cursor = accumulated mouse-delta stick;
        // free cursor = pointer offset from screen centre (hover). Both feed the same selection math.
        private void UpdateSelection()
        {
            if (wheel == null) return;

            Vector2 _direction;
            if (Cursor.lockState == CursorLockMode.Locked)
            {
                Vector2 _delta = Mouse.current != null ? Mouse.current.delta.ReadValue() : Vector2.zero;
                _stick += _delta * stickSensitivity;
                _stick = Vector2.ClampMagnitude(_stick, stickMaxRadius);
                _direction = _stick;
            }
            else
            {
                Vector2 _pointer = Mouse.current != null ? Mouse.current.position.ReadValue() : Vector2.zero;
                // Screen space is +Y up (matches the selection math's math space) — offset from centre.
                _direction = _pointer - new Vector2(Screen.width, Screen.height) * 0.5f;
            }

            int _index = EmoteWheelSelection.SelectionIndex(_direction, wheel.Count, deadzone);
            wheel.SetSelection(_index);
        }

        private void CloseWheel(bool playSelection)
        {
            _open = false;
            if (wheel == null) return;

            if (playSelection)
            {
                int _index = EmoteWheelSelection.SelectionIndex(
                    Cursor.lockState == CursorLockMode.Locked ? _stick : PointerDirection(),
                    wheel.Count, deadzone);
                EmoteDefinition _emote = wheel.GetEmote(_index);
                if (_emote != null)
                {
                    PlayerAvatar _avatar = ResolveLocalAvatar();
                    _avatar?.RequestEmote(_emote.animatorEmoteId);
                }
            }

            wheel.Close();
        }

        private Vector2 PointerDirection()
        {
            Vector2 _pointer = Mouse.current != null ? Mouse.current.position.ReadValue() : Vector2.zero;
            return _pointer - new Vector2(Screen.width, Screen.height) * 0.5f;
        }

        // Lazily resolve (and cache) the local owned avatar — same pattern as AvatarCameraArbiter /
        // AvatarEmbodiedCamera (AvatarManager.For(NetworkManager.Singleton) + IsOwner). Re-resolves if the
        // cached one was torn down.
        private PlayerAvatar ResolveLocalAvatar()
        {
            if (_localAvatar != null) return _localAvatar;

            AvatarManager _manager = AvatarManager.For(NetworkManager.Singleton);
            if (_manager == null) return null;

            foreach (PlayerAvatar _avatar in _manager.GetAvatars())
            {
                if (_avatar != null && _avatar.IsOwner)
                {
                    _localAvatar = _avatar;
                    break;
                }
            }
            return _localAvatar;
        }
    }
}
