using System;
using System.Collections.Generic;
using Characters;
using Characters.Powers;
using Extensions;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;

namespace Board.UI.PowerBar
{
    public class PowersBar : NetworkBehaviour
    {
        public Transform powersBarParent;
        
        [SerializeField] private GameObject powerBarObjectPrefab;
        
        public event Action<Power> onPowerClicked;
        
        public void RefreshCharacterPowerBar(Character _character)
        {
            foreach (Transform child in powersBarParent)
            {
                Destroy(child.gameObject);
            }
            
            if (_character == null || _character.role == null)
            {
                return;
            }
            List<Power> _powers = _character.role.powers;
            CreatePowerBar(_powers, _character);
        }

        public void CreatePowerBar(List<Power> _powers, Character _fromCharacter)
        {
            foreach (var _currentPower in _powers)
            {
                GameObject _powerBarGameObject = Instantiate(powerBarObjectPrefab, powersBarParent);
                var _powersBarObject = _powerBarGameObject.GetComponent<PowersBarObject>();
                _powersBarObject.SetPower(_currentPower, _fromCharacter);
                _powersBarObject.onPowerBarObjectClicked += OnPowerClicked;
            }
        }

        private void OnPowerClicked(Power _power)
        {
            onPowerClicked?.Invoke(_power);
        }
    }
}
