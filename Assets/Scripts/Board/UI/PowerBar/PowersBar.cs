#region

using System;
using System.Collections.Generic;
using System.Linq;
using Characters;
using Characters.Powers;
using GameLogic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.Assertions;

#endregion

namespace Board.UI.PowerBar
{
    public class PowersBar : NetworkBehaviour
    {
        public Transform powersBarParent;
        
        [SerializeField] private GameObject powerBarObjectPrefab;
        
        public List<PowersBarObject> powersBarObjects = new();
        
        public event Action<Power> onPowerClicked;

        private void Start()
        {
            GameManager.instance.onGameStarted += () =>
            {
                var _localCharacter = GameManager.instance.characterManager.GetLocalCharacter(false);
                if (_localCharacter)
                {
                    _localCharacter.onRoleUpdated += () => { RefreshCharacterPowerBar(_localCharacter.ownerClientId.Value); };
                    RefreshCharacterPowerBar(_localCharacter.ownerClientId.Value);
                }
            };
        }

        private void Update()
        {
            var _rolePowers = GameManager.instance.characterManager.GetLocalCharacter(false)?.role?.powers;
            
            if (_rolePowers == null)
            {
                return;
            }
            
            if (_rolePowers.Count == 0)
            {
                return;
            }
            
            foreach (var _currentPowerBarObject in powersBarObjects)
            {
                var _playerPower = _rolePowers.FirstOrDefault(_p => _p.IsTheSamePower(_currentPowerBarObject.power));
                Assert.IsNotNull(_playerPower, "Player Power should not be null");
                _currentPowerBarObject.customButton.enabled = _playerPower.CanUse(true);
            }
        }

        public void RefreshCharacterPowerBar(ulong _characterID)
        {
            Character _character = GameManager.instance.characterManager.GetCharacter(_characterID, false);
            if (_character == null || _character.role == null)
            {
                return;
            }
            
            List<Power> _powers = _character.role.powers;

            if (powersBarObjects.Count == 0)
            {
                CreatePowerBar(_powers, _character);
                return;
            }
            
            foreach (var _power in _powers)
            {
                if (_power.isPassive)
                {
                    continue;
                }
                
                var _powerBarObject = powersBarObjects.FirstOrDefault(_obj => _obj.power == _power);
                if (_powerBarObject)
                {
                    _powerBarObject.SetPower(_power, _character);
                }
            }
        }

        public void CreatePowerBar(List<Power> _powers, Character _fromCharacter)
        {
            foreach (var _currentPower in _powers)
            {
                if (_currentPower.isPassive)
                {
                    continue;
                }
                
                GameObject _powerBarGameObject = Instantiate(powerBarObjectPrefab, powersBarParent);
                var _powersBarObject = _powerBarGameObject.GetComponent<PowersBarObject>();
                _powersBarObject.SetPower(_currentPower, _fromCharacter);
                _powersBarObject.onPowerBarObjectClicked += OnPowerClicked;
                
                powersBarObjects.Add(_powersBarObject);
            }
        }

        private void OnPowerClicked(Power _power)
        {
            onPowerClicked?.Invoke(_power);
        }
        
        public PowersBarObject GetPowerBarObject(Power _power)
        {
            foreach (Transform _child in powersBarParent)
            {
                var _powerBarObject = _child.GetComponent<PowersBarObject>();
                if (_powerBarObject != null && _powerBarObject.power == _power)
                {
                    return _powerBarObject;
                }
            }
            return null;
        }
    }
}
