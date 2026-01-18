#region

using System;
using System.Collections.Generic;
using System.Linq;
using Characters;
using Characters.Powers;
using Extensions;
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
                    _localCharacter.onPowersUpdated += () => { RefreshCharacterPowerBar(_localCharacter.ownerClientId.Value); };
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
                if (!_currentPowerBarObject)
                {
                    continue;
                }
                var _playerPower = _rolePowers.FirstOrDefault(_p => _p.IsTheSamePower(_currentPowerBarObject.power));
                if (_playerPower == null)
                {
                    CreatePowerBar(_rolePowers, GameManager.instance.characterManager.GetLocalCharacter(false));
                    return;
                }
                _currentPowerBarObject.SetInteractable(_playerPower.CanUse(true));
            }
        }

        public void RefreshCharacterPowerBar(ulong _characterID)
        {
            Character _character = GameManager.instance.characterManager.GetCharacter(_characterID, false);
            if (!_character || _character.role == null)
            {
                return;
            }
            
            List<Power> _powers = _character.role.powers;
            
            
            if (powersBarObjects.Count == 0)
            {
                CreatePowerBar(_powers, _character);
                return;
            }
            
            bool _hasSamePower = ArePowerSetsEqual(_powers, powersBarObjects);
            if (!_hasSamePower)
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
            foreach (var _powersBarObject in powersBarObjects)
            {
                Destroy(_powersBarObject.gameObject);
            }
            powersBarObjects.Clear();

            foreach (Power _currentPower in _powers)
            {
                if (_currentPower.isPassive)
                {
                    continue;
                }
                
                GameObject _powerBarGameObject = Instantiate(powerBarObjectPrefab, powersBarParent);
                PowersBarObject _powersBarObject = _powerBarGameObject.GetComponent<PowersBarObject>();
                _powersBarObject.SetPower(_currentPower, _fromCharacter);
                _powersBarObject.onPowerBarObjectClicked += OnPowerClicked;
                
                powersBarObjects.Add(_powersBarObject);
            }

            if (powerBarObjectPrefab.GetComponent<PowersBarObject>() is PowerBarObject3D)
            {
                for (int _i = 0; _i < powersBarObjects.Count; _i++)
                {
                    PowersBarObject _powerBarObject = powersBarObjects[_i];
                    Vector3 _parentScale = _powerBarObject.transform.parent.lossyScale;
                    float _parentScaleX = 1f / _parentScale.x;
                    float _parentScaleY = 1f / _parentScale.y;
                    float _parentScaleZ = 1f / _parentScale.z;
                    _powerBarObject.transform.localScale = new Vector3(_parentScaleX, _parentScaleY, _parentScaleZ);
                    Vector3 _offset = new Vector3((_i - ((powersBarObjects.Count - 1) / 2f)) * 3 * _parentScaleX, 0, 0);
                    _powerBarObject.transform.localPosition = _offset;
                }
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

        public bool ArePowerSetsEqual(List<Power> _powers, List<PowersBarObject> _powersBarObjects)
        {
            var _leftIds = _powers?.Where(_p => _p).Select(_p => _p.NetworkObjectId).ToList() ??
                           new List<ulong>();
            var _rightIds = _powersBarObjects?
                .Where(_pbo => _pbo.power)
                .Select(_pbo => _pbo.power.NetworkObjectId)
                .ToList() ?? new List<ulong>();

            if (_leftIds.Count != _rightIds.Count)
                return false;


            var _remaining = new List<ulong>(_rightIds);

            foreach (var id in _leftIds)
            {
                if (!_remaining.Remove(id))
                    return false;
            }

            return _remaining.Count == 0;
        }
    }
}
