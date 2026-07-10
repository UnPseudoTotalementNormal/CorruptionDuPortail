using UI.EmoteWheel;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Avatars
{
    /// <summary>
    /// DEBUG SPIKE ONLY (EmoteWheelSpike.unity). Drives the <see cref="EmoteWheelController"/> in complete
    /// isolation — NO network, NO CameraModeChannel gate, NO avatar resolve. Hold the key to open the wheel,
    /// point with the mouse (hover from screen centre), release to "play" the highlighted emote (logged only).
    /// Purpose: prove the wheel UI + selection math + UITK render + Input reading work on their own, so a dead
    /// "T does nothing" in GameScene can be pinned to the gates (embodied/avatar) vs the wheel itself.
    /// Delete after diagnosis. Logs are tagged [EMOTESPIKE].
    /// </summary>
    public class EmoteWheelSpikeDriver : MonoBehaviour
    {
        [SerializeField] private EmoteWheelController wheel;
        [SerializeField] private Key openKey = Key.T;
        [SerializeField] private float deadzone = 40f;

        private bool _open;

        private void Start()
        {
            Debug.Log($"[EMOTESPIKE] driver up. wheel={(wheel != null ? "wired" : "NULL")} " +
                      $"count={(wheel != null ? wheel.Count : -1)} keyboard={(Keyboard.current != null)}");
        }

        private void Update()
        {
            Keyboard _kb = Keyboard.current;
            if (_kb == null)
            {
                return;
            }

            if (_kb[openKey].wasPressedThisFrame && !_open)
            {
                _open = true;
                Debug.Log("[EMOTESPIKE] open");
                wheel?.Open();
            }

            if (_open && _kb[openKey].isPressed)
            {
                UpdateSelection();
            }

            if (_open && _kb[openKey].wasReleasedThisFrame)
            {
                _open = false;
                int _index = Selected();
                EmoteDefinition _emote = wheel != null ? wheel.GetEmote(_index) : null;
                Debug.Log($"[EMOTESPIKE] release -> index={_index} emote={(_emote != null ? _emote.displayName : "none")}");
                if (_emote != null) wheel.Confirm(_index);
                wheel?.Close();
            }
        }

        private void UpdateSelection()
        {
            if (wheel == null)
            {
                return;
            }
            wheel.SetSelection(Selected());
        }

        private int Selected()
        {
            if (wheel == null)
            {
                return EmoteWheelSelection.None;
            }
            Vector2 _pointer = Mouse.current != null ? Mouse.current.position.ReadValue() : Vector2.zero;
            Vector2 _direction = _pointer - new Vector2(Screen.width, Screen.height) * 0.5f;
            return EmoteWheelSelection.SelectionIndex(_direction, wheel.Count, deadzone);
        }
    }
}
