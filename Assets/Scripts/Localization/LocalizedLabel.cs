using CorruptionDuPortail.Domain.Localization;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Localization
{
    /// <summary>
    /// Shows a text written in a prefab or a scene in the player's language (T36). Put it next to the text component
    /// (TextMeshPro or legacy <see cref="Text"/>) of a label whose French text is authored in the asset: that text is
    /// the source, nothing else to fill in. It follows a language change while it is on screen.
    ///
    /// <para>A label whose text is written by code does NOT take this component: that code calls <c>Loc.Tr</c>. If code
    /// does write over a localized label, the label steps aside (it only ever replaces a text it wrote itself).</para>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class LocalizedLabel : MonoBehaviour
    {
        private TMP_Text _tmp;
        private Text _legacy;
        private string _source;
        private string _applied;
        private bool _captured;

        /// <summary>The French text authored in the asset (null before the first activation).</summary>
        public string Source => _source;

        private string Current
        {
            get => _tmp != null ? _tmp.text : _legacy != null ? _legacy.text : null;
            set
            {
                if (_tmp != null)
                {
                    _tmp.text = value;
                }
                else if (_legacy != null)
                {
                    _legacy.text = value;
                }
            }
        }

        private void Awake() => Capture();

        private void OnEnable()
        {
            Capture();
            Loc.LanguageChanged += Apply;
            Apply();
        }

        private void OnDisable() => Loc.LanguageChanged -= Apply;

        private void Capture()
        {
            if (_captured)
            {
                return;
            }
            _captured = true;
            _tmp = GetComponent<TMP_Text>();
            _legacy = _tmp == null ? GetComponent<Text>() : null;
            if (_tmp == null && _legacy == null)
            {
                Debug.LogError($"[LOC] LocalizedLabel on '{name}' has no text component.", this);
                return;
            }
            _source = Current;
        }

        private void Apply()
        {
            if (_source == null)
            {
                return;
            }
            // Some code wrote over the label since our last pass: the text is that code's business now.
            if (_applied != null && Current != _applied)
            {
                return;
            }
            // Quiet: before its first activation a label may have been filled by code with a text that is no game
            // text (a pseudo). The texts authored in assets are checked by LocalizationCoverageTests instead.
            _applied = Loc.TrQuiet(_source);
            Current = _applied;
        }
    }
}
