using Unity.Properties;
using UnityEngine;
using UnityEngine.UIElements;

namespace ModelTest.B.T2
{
    /// <summary>
    /// Displays a PlayerStats asset in a UIDocument using runtime data binding.
    ///
    /// The container gets the asset as its dataSource; every child inherits it and
    /// declares a DataBinding (ToTarget) on the relevant property. No Update(): the
    /// binding system refreshes the UI when the asset reports a change.
    ///
    /// Expected UXML: PlayerStatsScreen.uxml (element names below).
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public class PlayerStatsScreen : MonoBehaviour
    {
        const string ContainerName = "player-stats";
        const string NameLabelName = "player-name";
        const string LevelLabelName = "player-level";
        const string HealthLabelName = "player-health-label";
        const string HealthBarName = "player-health-bar";
        const string DamageButtonName = "damage-button";
        const string HealButtonName = "heal-button";
        const string LevelUpButtonName = "level-up-button";

        // BindingIds of the target (UI) properties.
        static readonly BindingId TextProperty = nameof(Label.text);
        static readonly BindingId ValueProperty = nameof(ProgressBar.value);

        [SerializeField] UIDocument m_Document;

        [Tooltip("Asset displayed. Bound directly (not copied): editing it in the Inspector updates the UI.")]
        [SerializeField] PlayerStats m_Stats;

        [Header("Demo buttons (optional)")]
        [SerializeField, Min(0)] int m_DamageAmount = 10;
        [SerializeField, Min(0)] int m_HealAmount = 10;

        VisualElement m_Container;
        Button m_DamageButton;
        Button m_HealButton;
        Button m_LevelUpButton;

        /// <summary>Change the displayed asset at runtime. Bindings follow automatically.</summary>
        public PlayerStats stats
        {
            get => m_Stats;
            set
            {
                m_Stats = value;
                if (m_Container != null)
                    m_Container.dataSource = m_Stats;
            }
        }

        void Reset()
        {
            m_Document = GetComponent<UIDocument>();
        }

        void OnEnable()
        {
            if (m_Document == null)
                m_Document = GetComponent<UIDocument>();

            VisualElement root = m_Document != null ? m_Document.rootVisualElement : null;
            if (root == null)
            {
                Debug.LogError($"[{nameof(PlayerStatsScreen)}] No UIDocument / root visual element.", this);
                return;
            }

            m_Container = root.Q<VisualElement>(ContainerName);
            if (m_Container == null)
            {
                Debug.LogError($"[{nameof(PlayerStatsScreen)}] Element '{ContainerName}' not found. " +
                               "Assign PlayerStatsScreen.uxml to the UIDocument.", this);
                return;
            }

            // Data source set once on the container; children inherit it.
            m_Container.dataSource = m_Stats;

            Bind(m_Container.Q<Label>(NameLabelName), TextProperty, PlayerStats.PlayerNamePath);
            Bind(m_Container.Q<Label>(LevelLabelName), TextProperty, PlayerStats.LevelLabelPath);
            Bind(m_Container.Q<Label>(HealthLabelName), TextProperty, PlayerStats.HealthLabelPath);

            ProgressBar healthBar = m_Container.Q<ProgressBar>(HealthBarName);
            if (healthBar != null)
            {
                healthBar.lowValue = 0f;
                healthBar.highValue = 1f;
                Bind(healthBar, ValueProperty, PlayerStats.HealthNormalizedPath);
            }

            m_DamageButton = m_Container.Q<Button>(DamageButtonName);
            m_HealButton = m_Container.Q<Button>(HealButtonName);
            m_LevelUpButton = m_Container.Q<Button>(LevelUpButtonName);
            if (m_DamageButton != null) m_DamageButton.clicked += OnDamageClicked;
            if (m_HealButton != null) m_HealButton.clicked += OnHealClicked;
            if (m_LevelUpButton != null) m_LevelUpButton.clicked += OnLevelUpClicked;
        }

        void OnDisable()
        {
            if (m_DamageButton != null) m_DamageButton.clicked -= OnDamageClicked;
            if (m_HealButton != null) m_HealButton.clicked -= OnHealClicked;
            if (m_LevelUpButton != null) m_LevelUpButton.clicked -= OnLevelUpClicked;
            m_DamageButton = m_HealButton = m_LevelUpButton = null;

            if (m_Container != null)
            {
                // Release the asset reference; bindings on children resolve to nothing.
                m_Container.dataSource = null;
                m_Container = null;
            }
        }

        void Bind(VisualElement target, in BindingId targetProperty, in PropertyPath sourcePath)
        {
            if (target == null)
            {
                Debug.LogWarning($"[{nameof(PlayerStatsScreen)}] Element for '{sourcePath}' not found; binding skipped.", this);
                return;
            }

            // The data source is inherited from m_Container; the path is relative to it.
            // updateTrigger is left at its default (OnSourceChanged).
            target.SetBinding(targetProperty, new DataBinding
            {
                dataSourcePath = sourcePath,
                bindingMode = BindingMode.ToTarget,
            });
        }

        // The buttons only modify the asset; the UI refresh comes from the bindings.
        void OnDamageClicked()
        {
            if (m_Stats != null) m_Stats.TakeDamage(m_DamageAmount);
        }

        void OnHealClicked()
        {
            if (m_Stats != null) m_Stats.Heal(m_HealAmount);
        }

        void OnLevelUpClicked()
        {
            if (m_Stats != null) m_Stats.LevelUp();
        }
    }
}
