using System;
using System.Runtime.CompilerServices;
using Unity.Properties;
using UnityEngine;

namespace ModelTest.A.T2
{
    /// <summary>
    /// ScriptableObject holding a player's core stats (name, level, health, max health).
    ///
    /// It is designed to be used as a <b>runtime binding data source</b> (Unity 6):
    /// - Properties exposed to the binding system are tagged with [CreateProperty]
    ///   (Unity.Properties).
    /// - It implements <see cref="INotifyBindablePropertyChanged"/> so that any change
    ///   (from code OR from the Inspector via OnValidate) pushes an update to bound UI
    ///   elements, without any manual polling in Update().
    ///
    /// Derived read-only properties (HealthRatio / HealthText / LevelText) are also
    /// exposed so the UI can bind directly to display-ready values.
    /// </summary>
    [CreateAssetMenu(fileName = "PlayerStats", menuName = "A_ModelTest/T2/Player Stats")]
    public class PlayerStats : ScriptableObject, INotifyBindablePropertyChanged
    {
        [SerializeField] private string _playerName = "Unknown";
        [SerializeField, Min(1)] private int _level = 1;
        [SerializeField, Min(0)] private int _health = 100;
        [SerializeField, Min(1)] private int _maxHealth = 100;

        /// <summary>Raised whenever a bindable property changes. Consumed by the UITK binding system.</summary>
        public event EventHandler<BindablePropertyChangedEventArgs> propertyChanged;

        // ---- Directly editable, bindable state -----------------------------------

        [CreateProperty]
        public string PlayerName
        {
            get => _playerName;
            set
            {
                if (_playerName == value)
                    return;
                _playerName = value;
                Notify();
            }
        }

        [CreateProperty]
        public int Level
        {
            get => _level;
            set
            {
                int v = Mathf.Max(1, value);
                if (_level == v)
                    return;
                _level = v;
                Notify();
                Notify(nameof(LevelText));
            }
        }

        [CreateProperty]
        public int Health
        {
            get => _health;
            set
            {
                int v = Mathf.Clamp(value, 0, _maxHealth);
                if (_health == v)
                    return;
                _health = v;
                Notify();
                Notify(nameof(HealthRatio));
                Notify(nameof(HealthText));
            }
        }

        [CreateProperty]
        public int MaxHealth
        {
            get => _maxHealth;
            set
            {
                int v = Mathf.Max(1, value);
                if (_maxHealth == v)
                    return;
                _maxHealth = v;
                // Keep current health within the new bound.
                _health = Mathf.Clamp(_health, 0, _maxHealth);
                Notify();
                Notify(nameof(Health));
                Notify(nameof(HealthRatio));
                Notify(nameof(HealthText));
            }
        }

        // ---- Derived, display-ready, bindable values -----------------------------

        /// <summary>Health as a 0..1 ratio, convenient for driving a bar width.</summary>
        [CreateProperty]
        public float HealthRatio => Mathf.Clamp01((float)_health / Mathf.Max(1, _maxHealth));

        /// <summary>Formatted "current / max" health readout.</summary>
        [CreateProperty]
        public string HealthText => $"{_health} / {_maxHealth}";

        /// <summary>Formatted level readout.</summary>
        [CreateProperty]
        public string LevelText => $"Level {_level}";

        // ---- Change notification -------------------------------------------------

        private void Notify([CallerMemberName] string propertyName = "")
        {
            propertyChanged?.Invoke(this, new BindablePropertyChangedEventArgs(propertyName));
        }

        /// <summary>
        /// Push a notification for every bindable property. Used after an Inspector edit
        /// (which mutates the serialized fields directly, bypassing the C# setters).
        /// </summary>
        private void NotifyAll()
        {
            Notify(nameof(PlayerName));
            Notify(nameof(Level));
            Notify(nameof(Health));
            Notify(nameof(MaxHealth));
            Notify(nameof(HealthRatio));
            Notify(nameof(HealthText));
            Notify(nameof(LevelText));
        }

        private void OnValidate()
        {
            // Keep the serialized data coherent after an Inspector edit...
            _maxHealth = Mathf.Max(1, _maxHealth);
            _level = Mathf.Max(1, _level);
            _health = Mathf.Clamp(_health, 0, _maxHealth);
            // ...then let the binding system refresh the UI.
            NotifyAll();
        }

        // ---- Convenience test hooks (Inspector gear menu, no input dependency) ----

        [ContextMenu("Test/Take 10 damage")]
        private void TakeDamage() => Health -= 10;

        [ContextMenu("Test/Heal 10")]
        private void Heal() => Health += 10;

        [ContextMenu("Test/Level up")]
        private void LevelUp() => Level += 1;
    }
}
