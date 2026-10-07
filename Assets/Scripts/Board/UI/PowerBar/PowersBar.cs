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

        // Story 7.4 lane A: scene-wired CharacterManager, replacing the GameManager hub-hop.
        [SerializeField] private CharacterManager characterManager;

        // Story 12.2 lane A: scene-wired GameManager, clearing the last GameManager hub read.
        [SerializeField] private GameManager gameManager;
        
        public List<PowersBarObject> powersBarObjects = new();
        
        public event Action<Power> onPowerClicked;

        private Character _currentSubscribedCharacter;

        private void Start()
        {
            Assert.IsNotNull(characterManager, "PowersBar.characterManager is not wired — wire it in GameScene (the composition root).");
            Assert.IsNotNull(gameManager, "PowersBar.gameManager is not wired — wire it in GameScene (the composition root).");
            gameManager.onGameStarted += SubscribeToLocalCharacter;
            characterManager.onLocalIdentityChanged += SubscribeToLocalCharacter;
        }

        // The scene's characters outlive this bar while GameScene unloads (end of game, return to menu): their powers'
        // despawn rebuilds their lists and raised onPowersUpdated on this destroyed bar (NRE in CreatePowerBar).
        private void OnDestroy()
        {
            if (gameManager)
            {
                gameManager.onGameStarted -= SubscribeToLocalCharacter;
            }
            if (characterManager)
            {
                characterManager.onLocalIdentityChanged -= SubscribeToLocalCharacter;
            }
            if (_currentSubscribedCharacter)
            {
                _currentSubscribedCharacter.onPowersUpdated -= OnPowersUpdated;
                _currentSubscribedCharacter.onRoleUpdated -= OnPowersUpdated;
            }
            _currentSubscribedCharacter = null;
        }

        private void SubscribeToLocalCharacter()
        {
            if (_currentSubscribedCharacter)
            {
                _currentSubscribedCharacter.onPowersUpdated -= OnPowersUpdated;
                _currentSubscribedCharacter.onRoleUpdated -= OnPowersUpdated;
            }

            _currentSubscribedCharacter = characterManager.GetLocalCharacter(false);
            
            if (_currentSubscribedCharacter)
            {
                _currentSubscribedCharacter.onPowersUpdated += OnPowersUpdated;
                _currentSubscribedCharacter.onRoleUpdated += OnPowersUpdated;
                RefreshCharacterPowerBar(_currentSubscribedCharacter.ownerClientId.Value);
            }
        }

        private void OnPowersUpdated()
        {
            if (_currentSubscribedCharacter)
            {
                RefreshCharacterPowerBar(_currentSubscribedCharacter.ownerClientId.Value);
            }
        }

        private void Update()
        {
            // Session stopped (host left / ended the game: NGO shut down and the session registries were reset, the
            // scene unloads a frame later): nothing to show, and target checks would read the cleared registries.
            var _network = Unity.Netcode.NetworkManager.Singleton;
            if (!characterManager || !characterManager.IsSpawned || _network == null || _network.ShutdownInProgress || !_network.IsListening)
            {
                return;
            }

            var _rolePowers = characterManager.GetLocalCharacter(false)?.role?.powers;
            
            if (_rolePowers == null)
            {
                return;
            }
            
            if (_rolePowers.Count == 0)
            {
                return;
            }

            // A one-shot stolen copy leaves the bar with a shrink-to-zero animation. Detach it from the tracked
            // list first so the rebuild logic below (and ArePowerSetsEqual) ignore it — the tween runs on the
            // detached UI object and self-destroys. Two entry points: (a) caught while still alive at
            // powerUseLeft==0; (b) its Power already despawned (server despawn beat the poll) → `power` is
            // Unity-null but `wasStolenCopy` still tells us it was one. Any other Unity-null power (e.g. reparented
            // away) is dropped instantly — leaving it would make the foreach below deref a destroyed Power.
            for (int _i = powersBarObjects.Count - 1; _i >= 0; _i--)
            {
                PowersBarObject _pbo = powersBarObjects[_i];
                if (!_pbo)
                {
                    powersBarObjects.RemoveAt(_i);
                    continue;
                }
                if (IsSpentStolenCopy(_pbo.power) || (!_pbo.power && _pbo.wasStolenCopy))
                {
                    powersBarObjects.RemoveAt(_i);
                    _pbo.AnimateOutThenDestroy();
                    continue;
                }
                if (!_pbo.power)
                {
                    powersBarObjects.RemoveAt(_i);
                    Destroy(_pbo.gameObject);
                }
            }

            foreach (var _currentPowerBarObject in powersBarObjects)
            {
                if (!_currentPowerBarObject || !_currentPowerBarObject.power)
                {
                    continue;
                }
                var _playerPower = _rolePowers.FirstOrDefault(_p => _p.IsTheSamePower(_currentPowerBarObject.power));
                if (_playerPower == null)
                {
                    CreatePowerBar(_rolePowers, characterManager.GetLocalCharacter(false));
                    return;
                }
                _currentPowerBarObject.SetInteractable(_playerPower.CanUse(true));
            }
        }

        // A one-shot stolen copy (Ugues / Luma) whose last use is spent. Excluded from the displayed set so it is
        // never (re)built into the bar; its removal is handled by the scale-out animation + server despawn.
        private static bool IsSpentStolenCopy(Power _power)
        {
            return _power && _power.isStolenCopy.Value && _power.powerUseLeft.Value <= 0;
        }

        public void RefreshCharacterPowerBar(ulong _characterID)
        {
            Character _character = characterManager.GetCharacter(_characterID, false);
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
                if (_power.IsPassive)
                {
                    continue;
                }
                
                var _powerBarObject = powersBarObjects.FirstOrDefault(_obj => _obj.power == _power);
                if (_powerBarObject && _powerBarObject.fromCharacter != _character)
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
                if (_currentPower.IsPassive)
                {
                    continue;
                }

                // Belt-and-suspenders: a spent one-shot stolen copy is animated out + despawned elsewhere; never
                // rebuild a bar object for one (guards the frame between powerUseLeft==0 and the deferred despawn).
                if (IsSpentStolenCopy(_currentPower))
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
            var _leftIds = _powers?.Where(_p => _p && !IsSpentStolenCopy(_p)).Select(_p => _p.NetworkObjectId).ToList() ??
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
