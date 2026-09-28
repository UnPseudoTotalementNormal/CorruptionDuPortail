using System;
using Unity.Properties;
using UnityEngine;
using UnityEngine.UIElements;

namespace ModelTest.B.T2
{
    /// <summary>
    /// Player data, usable directly as a UI Toolkit runtime data source.
    ///
    /// - [CreateProperty] exposes the properties to the binding system (Unity.Properties).
    /// - INotifyBindablePropertyChanged: the binding system only refreshes the bindings
    ///   whose path matches the property that changed.
    /// - IDataSourceViewHashProvider: the binding system compares a cheap version number
    ///   instead of re-reading every property to detect a change.
    ///
    /// Changes made through the C# setters notify immediately. Changes made in the
    /// Inspector bypass the setters: OnValidate() handles that case.
    /// </summary>
    [CreateAssetMenu(fileName = "B_PlayerStats", menuName = "B_ModelTest/T2/Player Stats")]
    public class PlayerStats : ScriptableObject, INotifyBindablePropertyChanged, IDataSourceViewHashProvider
    {
        // ---- Property paths, shared with the UI side to avoid magic strings ----
        public static readonly PropertyPath PlayerNamePath = new PropertyPath(nameof(playerName));
        public static readonly PropertyPath CurrentHealthPath = new PropertyPath(nameof(currentHealth));
        public static readonly PropertyPath MaxHealthPath = new PropertyPath(nameof(maxHealth));
        public static readonly PropertyPath LevelPath = new PropertyPath(nameof(level));
        public static readonly PropertyPath HealthNormalizedPath = new PropertyPath(nameof(healthNormalized));
        public static readonly PropertyPath HealthLabelPath = new PropertyPath(nameof(healthLabel));
        public static readonly PropertyPath LevelLabelPath = new PropertyPath(nameof(levelLabel));

        const int MinMaxHealth = 1;
        const int MinLevel = 1;

        // ---- Serialized data (hidden from the property bag: exposed through the properties below) ----
        [SerializeField, DontCreateProperty] string m_PlayerName = "Player";
        [SerializeField, DontCreateProperty, Min(0)] int m_CurrentHealth = 100;
        [SerializeField, DontCreateProperty, Min(MinMaxHealth)] int m_MaxHealth = 100;
        [SerializeField, DontCreateProperty, Min(MinLevel)] int m_Level = 1;

        // Incremented on every change; read by the binding system through GetViewHashCode().
        [NonSerialized] long m_Version;

        public event EventHandler<BindablePropertyChangedEventArgs> propertyChanged;

        // ---- Bindable properties ----

        [CreateProperty]
        public string playerName
        {
            get => m_PlayerName;
            set
            {
                string newValue = value ?? string.Empty;
                if (m_PlayerName == newValue)
                    return;
                m_PlayerName = newValue;
                MarkChanged();
                Notify(nameof(playerName));
            }
        }

        [CreateProperty]
        public int currentHealth
        {
            get => m_CurrentHealth;
            set
            {
                int newValue = Mathf.Clamp(value, 0, m_MaxHealth);
                if (m_CurrentHealth == newValue)
                    return;
                m_CurrentHealth = newValue;
                MarkChanged();
                NotifyHealth();
            }
        }

        [CreateProperty]
        public int maxHealth
        {
            get => m_MaxHealth;
            set
            {
                int newValue = Mathf.Max(MinMaxHealth, value);
                if (m_MaxHealth == newValue)
                    return;
                m_MaxHealth = newValue;
                // Keep the invariant currentHealth <= maxHealth.
                m_CurrentHealth = Mathf.Min(m_CurrentHealth, m_MaxHealth);
                MarkChanged();
                Notify(nameof(maxHealth));
                NotifyHealth();
            }
        }

        [CreateProperty]
        public int level
        {
            get => m_Level;
            set
            {
                int newValue = Mathf.Max(MinLevel, value);
                if (m_Level == newValue)
                    return;
                m_Level = newValue;
                MarkChanged();
                Notify(nameof(level));
                Notify(nameof(levelLabel));
            }
        }

        // ---- Derived, read-only properties (display-ready, so no converter is needed) ----

        /// <summary>Health in [0, 1], meant for a ProgressBar with low-value=0 / high-value=1.</summary>
        [CreateProperty]
        public float healthNormalized => m_MaxHealth > 0 ? (float)m_CurrentHealth / m_MaxHealth : 0f;

        [CreateProperty]
        public string healthLabel => $"{m_CurrentHealth} / {m_MaxHealth} PV";

        [CreateProperty]
        public string levelLabel => $"Niveau {m_Level}";

        // ---- Gameplay helpers (go through the setters, so they notify) ----

        public void TakeDamage(int amount) => currentHealth -= Mathf.Max(0, amount);

        public void Heal(int amount) => currentHealth += Mathf.Max(0, amount);

        public void LevelUp() => level += 1;

        // ---- IDataSourceViewHashProvider ----

        public long GetViewHashCode() => m_Version;

        // ---- Inspector edits ----

        // Called in the Editor when a value changes in the Inspector (and on load / undo).
        // The setters are bypassed in that case, so clamp and notify everything here.
        void OnValidate()
        {
            if (m_PlayerName == null)
                m_PlayerName = string.Empty;
            m_MaxHealth = Mathf.Max(MinMaxHealth, m_MaxHealth);
            m_CurrentHealth = Mathf.Clamp(m_CurrentHealth, 0, m_MaxHealth);
            m_Level = Mathf.Max(MinLevel, m_Level);

            MarkChanged();
            Notify(nameof(playerName));
            Notify(nameof(maxHealth));
            Notify(nameof(level));
            Notify(nameof(levelLabel));
            NotifyHealth();
        }

        // ---- Internals ----

        void MarkChanged() => m_Version++;

        void NotifyHealth()
        {
            Notify(nameof(currentHealth));
            Notify(nameof(healthNormalized));
            Notify(nameof(healthLabel));
        }

        void Notify(string property)
        {
            propertyChanged?.Invoke(this, new BindablePropertyChangedEventArgs(property));
        }
    }
}
