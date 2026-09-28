using System;
using System.Globalization;
using Unity.Properties;
using UnityEngine;
using UnityEngine.UIElements;

namespace ModelTest.B.T1
{
    /// <summary>
    /// Reusable stat bar: label + progress track + numeric value.
    /// Usable from UXML thanks to [UxmlElement] / [UxmlAttribute] (Unity 6+).
    ///
    /// UXML attributes (names are derived from the C# member names, kebab-case):
    ///   label        : text shown on the left.
    ///   max-value    : maximum value (bar is full when value >= max-value).
    ///   value        : current value (stored raw, clamped only for display).
    ///   bar-color    : fill color. Fully transparent (alpha = 0, default) means
    ///                  "let the USS decide" (see .B_stat-bar__fill).
    ///   value-format : composite format string, {0} = value, {1} = max.
    /// </summary>
    [UxmlElement]
    public partial class StatBar : VisualElement, INotifyValueChanged<float>
    {
        // ---- USS class names (prefixed with the output folder name) ----
        public static readonly string ussClassName = "B_stat-bar";
        public static readonly string labelUssClassName = ussClassName + "__label";
        public static readonly string trackUssClassName = ussClassName + "__track";
        public static readonly string fillUssClassName = ussClassName + "__fill";
        public static readonly string valueLabelUssClassName = ussClassName + "__value";
        public static readonly string emptyModifierUssClassName = ussClassName + "--empty";
        public static readonly string fullModifierUssClassName = ussClassName + "--full";

        // ---- Binding ids (runtime data binding support) ----
        public static readonly BindingId labelProperty = nameof(label);
        public static readonly BindingId maxValueProperty = nameof(maxValue);
        public static readonly BindingId valueProperty = nameof(value);
        public static readonly BindingId barColorProperty = nameof(barColor);

        const string k_DefaultFormat = "{0:0}/{1:0}";

        readonly Label m_Label;
        readonly VisualElement m_Track;
        readonly VisualElement m_Fill;
        readonly Label m_ValueLabel;

        string m_LabelText = "Stat";
        float m_MaxValue = 100f;
        float m_Value = 100f;
        Color m_BarColor = Color.clear;
        string m_ValueFormat = k_DefaultFormat;

        public StatBar()
        {
            AddToClassList(ussClassName);

            m_Label = new Label { name = "stat-label" };
            m_Label.AddToClassList(labelUssClassName);

            m_Track = new VisualElement { name = "stat-track" };
            m_Track.AddToClassList(trackUssClassName);

            m_Fill = new VisualElement { name = "stat-fill" };
            m_Fill.AddToClassList(fillUssClassName);
            m_Fill.pickingMode = PickingMode.Ignore;
            m_Track.Add(m_Fill);

            m_ValueLabel = new Label { name = "stat-value" };
            m_ValueLabel.AddToClassList(valueLabelUssClassName);

            Add(m_Label);
            Add(m_Track);
            Add(m_ValueLabel);

            RefreshLabel();
            RefreshColor();
            RefreshBar();
        }

        /// <summary>Text displayed on the left of the bar.</summary>
        [UxmlAttribute, CreateProperty]
        public string label
        {
            get => m_LabelText;
            set
            {
                if (string.Equals(m_LabelText, value, StringComparison.Ordinal))
                    return;
                m_LabelText = value;
                RefreshLabel();
                NotifyPropertyChanged(labelProperty);
            }
        }

        /// <summary>Maximum value. Values &lt;= 0 produce an empty bar.</summary>
        [UxmlAttribute, CreateProperty]
        public float maxValue
        {
            get => m_MaxValue;
            set
            {
                if (Mathf.Approximately(m_MaxValue, value))
                    return;
                m_MaxValue = value;
                RefreshBar();
                NotifyPropertyChanged(maxValueProperty);
            }
        }

        /// <summary>
        /// Current value. Stored as-is (not clamped) so that UXML attribute order
        /// (value before max-value) does not matter; clamped to [0, maxValue] for display.
        /// Setting it sends a ChangeEvent&lt;float&gt;.
        /// </summary>
        [UxmlAttribute, CreateProperty]
        public float value
        {
            get => m_Value;
            set
            {
                if (Mathf.Approximately(m_Value, value))
                    return;

                float previous = m_Value;
                SetValueWithoutNotify(value);

                if (panel != null)
                {
                    using (ChangeEvent<float> evt = ChangeEvent<float>.GetPooled(previous, m_Value))
                    {
                        evt.target = this;
                        SendEvent(evt);
                    }
                }

                NotifyPropertyChanged(valueProperty);
            }
        }

        /// <summary>
        /// Fill color. Alpha = 0 (default) clears the inline style so the USS color applies.
        /// </summary>
        [UxmlAttribute, CreateProperty]
        public Color barColor
        {
            get => m_BarColor;
            set
            {
                if (m_BarColor == value)
                    return;
                m_BarColor = value;
                RefreshColor();
                NotifyPropertyChanged(barColorProperty);
            }
        }

        /// <summary>Composite format for the numeric text. {0} = value, {1} = max.</summary>
        [UxmlAttribute]
        public string valueFormat
        {
            get => m_ValueFormat;
            set
            {
                m_ValueFormat = string.IsNullOrEmpty(value) ? k_DefaultFormat : value;
                RefreshBar();
            }
        }

        /// <summary>Normalized fill ratio in [0, 1].</summary>
        public float normalizedValue => m_MaxValue > 0f ? Mathf.Clamp01(m_Value / m_MaxValue) : 0f;

        public void SetValueWithoutNotify(float newValue)
        {
            m_Value = newValue;
            RefreshBar();
        }

        // ---- Internal refresh ----

        void RefreshLabel()
        {
            m_Label.text = m_LabelText;
            m_Label.style.display = string.IsNullOrEmpty(m_LabelText) ? DisplayStyle.None : DisplayStyle.Flex;
        }

        void RefreshColor()
        {
            m_Fill.style.backgroundColor = m_BarColor.a > 0f
                ? new StyleColor(m_BarColor)
                : new StyleColor(StyleKeyword.Null);
        }

        void RefreshBar()
        {
            float ratio = normalizedValue;
            m_Fill.style.width = Length.Percent(ratio * 100f);

            EnableInClassList(emptyModifierUssClassName, ratio <= 0f);
            EnableInClassList(fullModifierUssClassName, ratio >= 1f);

            float shown = m_MaxValue > 0f ? Mathf.Clamp(m_Value, 0f, m_MaxValue) : 0f;
            m_ValueLabel.text = FormatValue(shown, m_MaxValue);
        }

        string FormatValue(float shown, float max)
        {
            try
            {
                return string.Format(CultureInfo.InvariantCulture, m_ValueFormat, shown, max);
            }
            catch (FormatException)
            {
                return string.Format(CultureInfo.InvariantCulture, k_DefaultFormat, shown, max);
            }
        }
    }
}
