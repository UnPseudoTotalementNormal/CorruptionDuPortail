using UnityEngine;
using UnityEngine.UIElements;

namespace ModelTest.A.T1
{
    /// <summary>
    /// Reusable UI Toolkit control: a labelled progress bar with a numeric readout.
    /// Layout: [ Label ][ ============ track/fill ============ ][ value / max ]
    ///
    /// Usable from UXML thanks to the [UxmlElement] source-generated factory
    /// (Unity 2023.2+ / Unity 6). Configurable attributes: label, max-value,
    /// value and fill-color.
    /// </summary>
    [UxmlElement]
    public partial class StatBar : VisualElement
    {
        // USS classes are prefixed with the output folder name ("A_") as required.
        public static readonly string ussClassName = "A_stat-bar";
        public static readonly string labelUssClassName = "A_stat-bar__label";
        public static readonly string trackUssClassName = "A_stat-bar__track";
        public static readonly string fillUssClassName = "A_stat-bar__fill";
        public static readonly string valueUssClassName = "A_stat-bar__value";

        private readonly Label _label;
        private readonly VisualElement _track;
        private readonly VisualElement _fill;
        private readonly Label _valueLabel;

        private string _labelText = "Stat";
        private float _maxValue = 100f;
        private float _value = 0f;
        private Color _fillColor = new Color(0.29f, 0.66f, 0.35f, 1f); // pleasant green

        /// <summary>Text shown on the left-hand side of the bar.</summary>
        [UxmlAttribute("label")]
        public string LabelText
        {
            get => _labelText;
            set
            {
                _labelText = value;
                if (_label != null)
                    _label.text = _labelText;
            }
        }

        /// <summary>Upper bound of the bar. Clamped to a tiny positive number to avoid divide-by-zero.</summary>
        [UxmlAttribute("max-value")]
        public float MaxValue
        {
            get => _maxValue;
            set
            {
                _maxValue = Mathf.Max(value, 0.0001f);
                RefreshFill();
            }
        }

        /// <summary>Current value. Clamped to [0, MaxValue].</summary>
        [UxmlAttribute("value")]
        public float Value
        {
            get => _value;
            set
            {
                _value = Mathf.Clamp(value, 0f, _maxValue);
                RefreshFill();
            }
        }

        /// <summary>Color of the filled portion of the bar.</summary>
        [UxmlAttribute("fill-color")]
        public Color FillColor
        {
            get => _fillColor;
            set
            {
                _fillColor = value;
                if (_fill != null)
                    _fill.style.backgroundColor = _fillColor;
            }
        }

        public StatBar()
        {
            AddToClassList(ussClassName);

            _label = new Label(_labelText);
            _label.AddToClassList(labelUssClassName);
            _label.pickingMode = PickingMode.Ignore;
            Add(_label);

            _track = new VisualElement();
            _track.AddToClassList(trackUssClassName);
            _track.pickingMode = PickingMode.Ignore;
            Add(_track);

            _fill = new VisualElement();
            _fill.AddToClassList(fillUssClassName);
            _fill.pickingMode = PickingMode.Ignore;
            _fill.style.backgroundColor = _fillColor;
            _track.Add(_fill);

            _valueLabel = new Label();
            _valueLabel.AddToClassList(valueUssClassName);
            _valueLabel.pickingMode = PickingMode.Ignore;
            Add(_valueLabel);

            RefreshFill();
        }

        private void RefreshFill()
        {
            float ratio = Mathf.Clamp01(_value / _maxValue);

            // Width driven as a percentage of the track.
            if (_fill != null)
                _fill.style.width = Length.Percent(ratio * 100f);

            if (_valueLabel != null)
                _valueLabel.text = $"{Mathf.RoundToInt(_value)} / {Mathf.RoundToInt(_maxValue)}";
        }
    }
}
