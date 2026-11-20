using Diagnostics.Logs;
using UnityEngine;

namespace UI.Settings
{
    /// <summary>
    /// « Options » in the pause menu (T9 maquettes menus v1 P3): the words on the left, the options on the right. The
    /// options are open at first, the word lit; a click hides or shows them.
    /// </summary>
    [RequireComponent(typeof(CustomButton))]
    public class PauseOptionsEntry : MonoBehaviour
    {
        [Tooltip("What « Options » shows and hides: the audio panel and the trial panel.")]
        [SerializeField] private GameObject[] _pages;
        [SerializeField] private Color _openColor = new(0.941f, 0.722f, 0.353f); // #F0B85A
        [SerializeField] private Color _closedColor = new(0.945f, 0.886f, 0.769f); // #F1E2C4

        private CustomButton _button;
        private bool _open = true;

        private void Awake()
        {
            _button = GetComponent<CustomButton>();
            _button.onButtonClicked += Toggle;
        }

        // Open at first; then the player's choice stays while the game lasts (the menu fades, it is never disabled).
        private void Start() => Apply(true);

        private void Toggle()
        {
            Apply(!_open);
            GameLog.Info("ui", "ui.pause.options", "open", _open);
        }

        private void Apply(bool _value)
        {
            _open = _value;
            if (_pages != null)
            {
                foreach (GameObject _page in _pages)
                {
                    if (_page != null) _page.SetActive(_open);
                }
            }
            if (_button != null) _button.SetRestColor(_open ? _openColor : _closedColor);
        }
    }
}
