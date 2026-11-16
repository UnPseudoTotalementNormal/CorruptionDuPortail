using System;
using CorruptionDuPortail.Domain.Localization;
using Localization;
using TMPro;
using UnityEngine;
using UnityEngine.Assertions;
using UnityEngine.UI;

namespace UI.Settings
{
    /// <summary>
    /// The language line of the options panel (T36), in the main menu and the pause menu: a click goes to the next
    /// language, like the microphone mode line above it. The choice applies at once to everything on screen (every
    /// text follows <see cref="Loc.LanguageChanged"/>) and is kept for the next launches by
    /// <see cref="LocalizationRuntime"/>. Local to this player, never networked.
    /// </summary>
    public class LanguagePanelSettings : MonoBehaviour
    {
        [SerializeField] private Button _languageButton;
        [SerializeField] private TMP_Text _languageLabel;

        private void Start()
        {
            Assert.IsNotNull(_languageButton, "LanguagePanelSettings._languageButton not wired");
            Assert.IsNotNull(_languageLabel, "LanguagePanelSettings._languageLabel not wired");
            _languageButton.onClick.AddListener(NextLanguage);
            Loc.LanguageChanged += Refresh;
            Refresh();
        }

        private void OnDestroy()
        {
            if (_languageButton != null)
            {
                _languageButton.onClick.RemoveListener(NextLanguage);
            }
            Loc.LanguageChanged -= Refresh;
        }

        private void NextLanguage()
        {
            int _count = Enum.GetValues(typeof(GameLanguage)).Length;
            LocalizationRuntime.SetLanguage((GameLanguage)(((int)Loc.Language + 1) % _count));
        }

        // The other options panel (main menu / pause menu) or a command changed the language: keep this line in sync.
        // A language is named in its own words, whatever the current one is.
        private void Refresh()
        {
            _languageLabel.text = $"‹ {LocalizationRuntime.NativeName(Loc.Language)} ›";
        }
    }
}
