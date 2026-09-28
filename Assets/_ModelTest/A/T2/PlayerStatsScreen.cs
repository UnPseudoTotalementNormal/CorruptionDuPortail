using Unity.Properties;
using UnityEngine;
using UnityEngine.UIElements;

namespace ModelTest.A.T2
{
    /// <summary>
    /// Wires a <see cref="PlayerStats"/> ScriptableObject to a UI Toolkit screen using the
    /// Unity 6 <b>runtime data binding</b> system.
    ///
    /// The data source is assigned once on the root; each element resolves its
    /// <c>dataSourcePath</c> against that inherited source. All updates are pushed by the
    /// data source itself (it implements INotifyBindablePropertyChanged) — there is no
    /// per-frame polling and no manual refresh in Update().
    ///
    /// Requires a <see cref="UIDocument"/> whose Source Asset is
    /// <c>PlayerStatsScreen.uxml</c>.
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public class PlayerStatsScreen : MonoBehaviour
    {
        [Tooltip("The stats asset displayed by this screen. Editing it (code or Inspector) updates the UI live.")]
        [SerializeField] private PlayerStats _stats;

        private UIDocument _document;

        // Element names expected in PlayerStatsScreen.uxml.
        private const string NameLabelName = "player-name";
        private const string LevelLabelName = "player-level";
        private const string HealthTextName = "hp-text";
        private const string HealthFillName = "hp-fill";

        // Setup runs in Start(): by then every UIDocument.OnEnable has already built its
        // visual tree for objects present at scene load, so rootVisualElement is available.
        private void Start()
        {
            _document = GetComponent<UIDocument>();
            Bind();
        }

        /// <summary>
        /// (Re)assigns the data source and installs the bindings. Public so it can be called
        /// again if the visual tree is rebuilt (e.g. after disabling/enabling the UIDocument).
        /// </summary>
        public void Bind()
        {
            if (_document == null)
                _document = GetComponent<UIDocument>();

            VisualElement root = _document != null ? _document.rootVisualElement : null;
            if (root == null)
            {
                Debug.LogWarning($"{nameof(PlayerStatsScreen)}: rootVisualElement is not ready; is a Source Asset assigned on the UIDocument?", this);
                return;
            }

            if (_stats == null)
            {
                Debug.LogWarning($"{nameof(PlayerStatsScreen)}: no PlayerStats assigned.", this);
                return;
            }

            // A single data source at the root is inherited by all descendants.
            root.dataSource = _stats;

            BindText(root, NameLabelName, nameof(PlayerStats.PlayerName));
            BindText(root, LevelLabelName, nameof(PlayerStats.LevelText));
            BindText(root, HealthTextName, nameof(PlayerStats.HealthText));

            BindHealthBarWidth(root, HealthFillName, nameof(PlayerStats.HealthRatio));
        }

        /// <summary>Binds a Label's <c>text</c> to a string property on the data source (ToTarget).</summary>
        private static void BindText(VisualElement root, string elementName, string propertyName)
        {
            Label label = root.Q<Label>(elementName);
            if (label == null)
            {
                Debug.LogWarning($"{nameof(PlayerStatsScreen)}: no Label named '{elementName}' found.");
                return;
            }

            label.SetBinding("text", new DataBinding
            {
                dataSourcePath = new PropertyPath(propertyName),
                bindingMode = BindingMode.ToTarget,
            });
        }

        /// <summary>
        /// Binds an element's <c>style.width</c> to a 0..1 float ratio, converting it to a
        /// percentage Length via a local source-to-UI converter.
        /// </summary>
        private static void BindHealthBarWidth(VisualElement root, string elementName, string propertyName)
        {
            VisualElement fill = root.Q<VisualElement>(elementName);
            if (fill == null)
            {
                Debug.LogWarning($"{nameof(PlayerStatsScreen)}: no element named '{elementName}' found.");
                return;
            }

            var widthBinding = new DataBinding
            {
                dataSourcePath = new PropertyPath(propertyName),
                bindingMode = BindingMode.ToTarget,
            };

            // float ratio (0..1) -> StyleLength percentage.
            widthBinding.sourceToUiConverters.AddConverter(
                (ref float ratio) => new StyleLength(Length.Percent(Mathf.Clamp01(ratio) * 100f)));

            fill.SetBinding("style.width", widthBinding);
        }
    }
}
