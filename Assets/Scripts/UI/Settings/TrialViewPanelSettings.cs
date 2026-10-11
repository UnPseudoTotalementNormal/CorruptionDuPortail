using Presentation;
using UnityEngine;
using UnityEngine.Assertions;
using UnityEngine.UI;

namespace UI.Settings
{
    /// <summary>
    /// Pause menu toggles for the seated view trial options (board task T16): standing cards in first person and the
    /// round table. Local to this player, saved in PlayerPrefs by <see cref="SeatedViewOptions"/>.
    /// </summary>
    public class TrialViewPanelSettings : MonoBehaviour
    {
        [SerializeField] private SeatedViewOptions _options;
        [SerializeField] private Toggle _cardsStandToggle;
        [SerializeField] private Toggle _roundTableToggle;

        private void Start()
        {
            Assert.IsNotNull(_options, "TrialViewPanelSettings._options not wired");
            Assert.IsNotNull(_cardsStandToggle, "TrialViewPanelSettings._cardsStandToggle not wired");
            Assert.IsNotNull(_roundTableToggle, "TrialViewPanelSettings._roundTableToggle not wired");

            _cardsStandToggle.SetIsOnWithoutNotify(_options.CardsStand);
            _roundTableToggle.SetIsOnWithoutNotify(_options.RoundTable);
            _cardsStandToggle.onValueChanged.AddListener(OnCardsStandChanged);
            _roundTableToggle.onValueChanged.AddListener(OnRoundTableChanged);
            _options.OnChanged += Refresh;
        }

        private void OnDestroy()
        {
            if (_cardsStandToggle != null)
            {
                _cardsStandToggle.onValueChanged.RemoveListener(OnCardsStandChanged);
            }
            if (_roundTableToggle != null)
            {
                _roundTableToggle.onValueChanged.RemoveListener(OnRoundTableChanged);
            }
            if (_options != null)
            {
                _options.OnChanged -= Refresh;
            }
        }

        private void OnCardsStandChanged(bool _value) => _options.SetCardsStand(_value);

        private void OnRoundTableChanged(bool _value) => _options.SetRoundTable(_value);

        // Another writer (autoplay) changed an option: keep the toggles in sync without re-saving.
        private void Refresh()
        {
            _cardsStandToggle.SetIsOnWithoutNotify(_options.CardsStand);
            _roundTableToggle.SetIsOnWithoutNotify(_options.RoundTable);
        }
    }
}
