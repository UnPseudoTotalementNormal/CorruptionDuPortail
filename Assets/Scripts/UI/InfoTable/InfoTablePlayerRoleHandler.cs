using System;
using System.Collections.Generic;
using System.Linq;
using Characters;
using UnityEngine;
using UnityEngine.UI;

namespace UI.InfoTable
{
    public class InfoTablePlayerRoleHandler : MonoBehaviour
    {
        [SerializeField] private Image background;

        private Color baseColor;
        [SerializeField] private Color conflictColor;
        [SerializeField] private Color lockedColor = new Color(0.3f, 0.8f, 0.3f, 1f); // Vert par défaut

        private Character character;
        private bool isLocked;
        private List<InfoRoleChecker> roleCheckers = new();
        private ConflictType currentConflict = ConflictType.None;

        public event Action onConflictChanged;

        private void Awake()
        {
            baseColor = background.color;
        }

        public void AddRoleChecker(InfoRoleChecker _infoRoleChecker)
        {
            roleCheckers.Add(_infoRoleChecker);
            _infoRoleChecker.onValueChanged += OnRoleCheckerValueChanged;
        }

        private void OnRoleCheckerValueChanged(CheckerType _checkerType, bool _value)
        {
            CheckLocalConflicts();
        }

        public void CheckLocalConflicts()
        {
            // Ignorer les vérifications de conflit si le handler est verrouillé
            if (isLocked)
            {
                return;
            }
            
            // Count how many roles are marked as "Sure" for this player
            int _sureCount = roleCheckers.Count(_rc => _rc.GetCurrentCheckerType() == CheckerType.Sure);

            ConflictType _newConflict = _sureCount > 1 ? ConflictType.PlayerMultipleRoles : ConflictType.None;

            if (_newConflict != currentConflict)
            {
                currentConflict = _newConflict;
                onConflictChanged?.Invoke();
            }
            
            // Toujours mettre à jour le background, même si le type de conflit n'a pas changé
            // car le nombre de checkers concernés peut avoir changé
            UpdateBackground();
        }

        public void SetConflict(ConflictType _conflictType)
        {
            if (currentConflict != _conflictType)
            {
                currentConflict = _conflictType;
                UpdateBackground();
            }
        }

        private void UpdateBackground()
        {
            if (isLocked)
            {
                background.color = lockedColor;
            }
            else
            {
                background.color = currentConflict != ConflictType.None ? conflictColor : baseColor;
            }
            UpdateCheckerConflicts();
        }

        private void UpdateCheckerConflicts()
        {
            foreach (InfoRoleChecker _checker in roleCheckers)
            {
                bool _isInConflict = false;
                
                if (_checker.GetCurrentCheckerType() == CheckerType.Sure && currentConflict != ConflictType.None)
                {
                    _isInConflict = true;
                }
                
                _checker.SetConflict(_isInConflict);
            }
        }

        public ConflictType GetCurrentConflict()
        {
            return currentConflict;
        }

        public List<InfoRoleChecker> GetRoleCheckers()
        {
            return roleCheckers;
        }

        /// <summary>
        /// Définit le personnage associé à ce handler
        /// </summary>
        public void SetCharacter(Character _character)
        {
            character = _character;
        }

        /// <summary>
        /// Retourne le personnage associé à ce handler
        /// </summary>
        public Character GetCharacter()
        {
            return character;
        }

        /// <summary>
        /// Verrouille le handler et configure tous les checkers avec le rôle révélé
        /// </summary>
        public void LockWithRevealedRole()
        {
            if (character == null)
            {
                Debug.LogWarning("Cannot lock handler without a character assigned!");
                return;
            }

            isLocked = true;

            // Configurer tous les checkers : le rôle correct en Sure, les autres en SurelyNot
            foreach (InfoRoleChecker _checker in roleCheckers)
            {
                if (_checker.role.IsTheSameRole(character.role))
                {
                    _checker.ForceSetCheckerType(CheckerType.Sure);
                }
                else
                {
                    _checker.ForceSetCheckerType(CheckerType.SurelyNot);
                }
                
                // Verrouiller le checker
                _checker.SetLocked(true);
            }

            // Réinitialiser les conflits et mettre à jour le background
            currentConflict = ConflictType.None;
            UpdateBackground();
        }

        /// <summary>
        /// Retourne si le handler est verrouillé (rôle révélé)
        /// </summary>
        public bool IsLocked()
        {
            return isLocked;
        }

        private void OnDestroy()
        {
            foreach (InfoRoleChecker _roleChecker in roleCheckers)
            {
                if (_roleChecker != null)
                {
                    _roleChecker.onValueChanged -= OnRoleCheckerValueChanged;
                }
            }
        }
    }
}
