using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Characters.Powers;
using GameLogic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.Assertions;
using Random = UnityEngine.Random;

namespace Characters
{
    public class CharacterManager : NetworkBehaviour
    {
        public static CharacterManager instance;
        
        [SerializeField] private Transform _charactersParent;
        [SerializeField] private NetworkObject _characterPrefab;

        private List<Character> _characters => new List<Character>(FindObjectsOfType<Character>()); //TODO: BIG TEMPORARY
        
        public event Action<List<Character>> onCharactersListUpdated;
        
        private void Awake()
        {
            if (instance != null && instance != this)
            {
                Destroy(this.gameObject);
            }
            else
            {
                instance = this;
            }
        }
        
        public Character GetLocalCharacter(bool _triggerUpdate = true)
        {
            if (_triggerUpdate)
            {
                StartCoroutine(TriggerOnCharactersListUpdatedAtEndOfFrame());
            }
            return _characters.FirstOrDefault(_character => _character.ownerClientId.Value == NetworkManager.LocalClientId);
        }

        public Character GetCharacter(ulong _characterId, bool _triggerUpdate = true)
        {
            return GetCharacters(_triggerUpdate).FirstOrDefault(_c => _c.ownerClientId.Value == _characterId);
        }

        public List<Character> GetCharacters(bool _triggerUpdate = true)
        {
            if (_triggerUpdate)
            {
                StartCoroutine(TriggerOnCharactersListUpdatedAtEndOfFrame());
            }
            return _characters;
        }
        
        [Rpc(SendTo.Everyone, RequireOwnership = true)]
        public void GiveRoleToCharacterRpc(ulong _characterId, Role _role)
        {
            StartCoroutine(GiveRoleToCharacterCoroutine(_characterId, _role));
        }

        private IEnumerator GiveRoleToCharacterCoroutine(ulong _characterId, Role _role)
        {
            const int _maxTries = 20;
            int _tries = 0;
            Character _character = null;
            while (_tries < _maxTries)
            {
                _character = GetCharacter(_characterId, false);
                if (_character)
                {
                    break;
                }
                _tries++;
                yield return null;
            }
            if (!_character)
            {
                yield break;
            }

            _character.role = _role;
            _character.UpdateRoleRpc(_role);
            _character.CheckForPowersRpc();
            _character.role.ownerClientId = _characterId;
        }
        
        #region Characters Updates

        [Rpc(SendTo.Server)]
        public void AskForUpdateAllCharactersRpc()
        {
            if (!IsServer)
            {
                return;
            }
     
            foreach (var _character in _characters)
            {
                _character.AskForRoleUpdateRpc();
            }
            
            UpdateAllCharactersRpc();
        }
    
        [Rpc(SendTo.NotServer)]
        private void UpdateAllCharactersRpc()
        {
            onCharactersListUpdated?.Invoke(this._characters);
        }
        
        public IEnumerator TriggerOnCharactersListUpdatedAtEndOfFrame()
        {
            yield return new WaitForEndOfFrame();
            onCharactersListUpdated?.Invoke(_characters);
        }

        #endregion
        
        public Character CreateNewFakeCharacter()
        {
            ulong _newFakeClientId = GameValues.FAKE_CLIENT_ID - (ulong)instance.GetCharacters().Count(_c => _c.isFake);
            return AddNewCharacter(_newFakeClientId);
        }

        public Character AddNewCharacter(ulong _clientId) //todo: create all characters on start, and simply change ownerID when starting the game, to not have spawn issues
        {
            if (_characters.Any(_c => _c.ownerClientId.Value == _clientId))
            {
                return null;
            }
            
            NetworkObject _newCharacterObject = NetworkManager.SpawnManager.InstantiateAndSpawn(_characterPrefab, destroyWithScene: true);
            
            if (!_charactersParent.GetComponent<NetworkObject>().IsSpawned)
            {
                StartCoroutine(WaitForParentToSpawnAndSet(_newCharacterObject, _charactersParent.GetComponent<NetworkObject>()));
            }
            else
            {
                _newCharacterObject.TrySetParent(_charactersParent, false);
            }
            
            Character _newCharacter = _newCharacterObject.GetComponent<Character>();
            _newCharacter.ownerClientId.Value = _clientId;
            _characters.Add(_newCharacter);
            onCharactersListUpdated?.Invoke(_characters);
            return _newCharacter;
        }

        public void RemoveCharacter(ulong _clientId)
        {
            Character _characterToRemove = GetCharacters(false).FirstOrDefault(_c => _c.ownerClientId.Value == _clientId);
            if (_characterToRemove != null)
            {
                var _networkObject = _characterToRemove.GetComponent<NetworkObject>();
                if (_networkObject != null && _networkObject.IsSpawned)
                {
                    _networkObject.Despawn();
                }
                _characters.Remove(_characterToRemove);
            }
            onCharactersListUpdated?.Invoke(_characters);
        }
        
        public void GivePowerToCharacter(ulong _characterId, Power _power)
        {
            Assert.IsTrue(NetworkManager.Singleton.IsServer, "GivePowerToCharacter should only be called on the server");
            Character _character = GetCharacter(_characterId);
            Assert.IsNotNull(_character, $"Character with id {_characterId} not found when trying to give power {_power.powerName}");
            
            Power _newPower = Instantiate(_power, null);
            NetworkObject _powerNetworkObject = _newPower.GetComponent<NetworkObject>();
            _powerNetworkObject.GetComponent<Power>().idHolderServer = _characterId;
            _powerNetworkObject.Spawn(true);
            StartCoroutine(
                WaitForParentToSpawnAndSet(_powerNetworkObject, _character.GetComponent<NetworkObject>(), 
                    (_result) => { OnPowerReparentComplete(_newPower, _result); })
                );
        }
        
        public void RemovePowerFromCharacter(ulong _characterId, Power _power)
        {
            Assert.IsTrue(NetworkManager.Singleton.IsServer, "RemovePowerFromCharacter should only be called on the server");
            Character _character = GetCharacter(_characterId);
            Assert.IsNotNull(_character, $"Character with id {_characterId} not found when trying to remove power {_power.powerName}");
            
            if (_power.ownerCharacter != _character)
            {
                Debug.LogError($"Power {_power.powerName} does not belong to character {_characterId}");
                return;
            }
            
            PowerManager.instance.RemovePowerFromCharacterPowerListRpc(_characterId, new(_power));
            NetworkObject _powerNetworkObject = _power.GetComponent<NetworkObject>();
            if (_powerNetworkObject != null)
            {
                _powerNetworkObject.Despawn();
            }
        }
        
        

        private void OnPowerReparentComplete(Power _power, bool _result)
        {
            if (!_result)
            {
                Debug.LogError("Failed to reparent power " + _power.powerName + " to character " + _power.ownerClientId);
                return;
            }

            PowerManager.instance.OnPowerReparentedServer(_power);
        }
        
        private IEnumerator WaitForParentToSpawnAndSet(NetworkObject _child, NetworkObject _parent, Action<bool> _callback = null)
        {
            if (!_child || !_parent)
            {
                Debug.LogError("Child or parent is null in WaitForParentToSpawnAndSet");
                yield break;
            }
            
            while (!_parent.IsSpawned || !_child.IsSpawned)
            {
                yield return null;
            }

            if (!_child.TrySetParent(_parent.transform, false))
            {
                Debug.LogError("Failed to set parent in WaitForParentToSpawnAndSet");
                _callback?.Invoke(false);
                yield break;
            }

            _callback?.Invoke(true);
        }
    }
}