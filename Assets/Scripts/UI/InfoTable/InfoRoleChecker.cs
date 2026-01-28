using System;
using System.Collections.Generic;
using AYellowpaper.SerializedCollections;
using UnityEngine;
using UnityEngine.UI;

namespace UI.InfoTable
{
    public class InfoRoleChecker : MonoBehaviour
    {
        [SerializeField] private Image backgroundImage;
        
        private Color currentColor;
        private Color originalColor;
        [SerializeField] private Color conflictColor;

        [SerializeField] private SerializedDictionary<CheckerType, Color> checkBackgroundColors = new()
        {
            { CheckerType.Sure, Color.green },
            { CheckerType.Maybe, Color.yellow },
            { CheckerType.SurelyNot, Color.grey }
        };
        
        public Role role { get; private set; }
        [field: SerializeField] public SerializedDictionary<CheckerType, Toggle> toggles { get; private set; } = new();
        
        private bool isLocked;
        
        public event Action<CheckerType, bool> onValueChanged;

        private void Awake()
        {
            if (backgroundImage != null)
            {
                currentColor = backgroundImage.color;
                originalColor = currentColor;
            }
            foreach (KeyValuePair<CheckerType, Toggle> _pair in toggles)
            {
                _pair.Value.onValueChanged.AddListener((_isOn) => OnToggleValueChanged(_pair.Key, _isOn));
            }
        }

        private void OnToggleValueChanged(CheckerType _checkerType, bool _value)
        {
            foreach (KeyValuePair<CheckerType, Toggle> _keyValuePair in toggles)
            {
                if (_value && _keyValuePair.Key != _checkerType)
                {
                    _keyValuePair.Value.isOn = false;
                }
            }
            
            // Mettre à jour la couleur du background selon le type de checker activé
            UpdateBackgroundColor();
            
            onValueChanged?.Invoke(_checkerType, _value);
        }

        public void Setup(Role _role)
        {
            role = _role;
        }

        public CheckerType? GetCurrentCheckerType()
        {
            foreach (KeyValuePair<CheckerType, Toggle> _pair in toggles)
            {
                if (_pair.Value.isOn)
                {
                    return _pair.Key;
                }
            }
            return null;
        }

        private void UpdateBackgroundColor()
        {
            CheckerType? _currentType = GetCurrentCheckerType();
            
            if (backgroundImage != null)
            {
                if (_currentType.HasValue && checkBackgroundColors.ContainsKey(_currentType.Value))
                {
                    // Un toggle est coché, utiliser sa couleur
                    currentColor = checkBackgroundColors[_currentType.Value];
                }
                else
                {
                    // Aucun toggle coché, revenir à la couleur originale
                    currentColor = originalColor;
                }
                
                backgroundImage.color = currentColor;
            }
        }

        public void SetConflict(bool _hasConflict)
        {
            if (backgroundImage != null)
            {
                backgroundImage.color = _hasConflict ? conflictColor : currentColor;
            }
        }

        /// <summary>
        /// Verrouille ou déverrouille tous les toggles du checker
        /// </summary>
        public void SetLocked(bool _locked)
        {
            isLocked = _locked;
            foreach (KeyValuePair<CheckerType, Toggle> _pair in toggles)
            {
                _pair.Value.interactable = !_locked;
            }
        }

        /// <summary>
        /// Force le checker à un type spécifique sans déclencher les events
        /// Utilisé pour auto-compléter les rôles révélés
        /// </summary>
        public void ForceSetCheckerType(CheckerType _checkerType, bool _triggerEvent = false)
        {
            // Désactiver temporairement les listeners si on ne veut pas trigger les events
            if (!_triggerEvent)
            {
                foreach (KeyValuePair<CheckerType, Toggle> _pair in toggles)
                {
                    _pair.Value.onValueChanged.RemoveListener((_isOn) => OnToggleValueChanged(_pair.Key, _isOn));
                }
            }

            // Mettre à jour les toggles
            foreach (KeyValuePair<CheckerType, Toggle> _pair in toggles)
            {
                _pair.Value.isOn = (_pair.Key == _checkerType);
            }

            // Réactiver les listeners si nécessaire
            if (!_triggerEvent)
            {
                foreach (KeyValuePair<CheckerType, Toggle> _pair in toggles)
                {
                    _pair.Value.onValueChanged.AddListener((_isOn) => OnToggleValueChanged(_pair.Key, _isOn));
                }
            }

            UpdateBackgroundColor();
        }

        public bool IsLocked()
        {
            return isLocked;
        }
    }

    public enum CheckerType
    {
        Sure,
        Maybe,
        SurelyNot
    }
}
