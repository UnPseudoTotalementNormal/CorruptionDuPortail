using System;
using System.Collections.Generic;
using System.Linq;
using Characters;
using Characters.Powers;
using Characters.Powers.Interfaces;
using GameLogic.GameStates;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.Assertions;

namespace GameLogic
{
    public class PowerManager : MonoBehaviour
    {
        public static PowerManager instance;
        
        private void Awake()
        {
            if (instance != null && instance != this)
            {
                Destroy(gameObject);
                return;
            }
            instance = this;
        }

        private void Start()
        {
            Power.onPowerSpawned += OnPowerSpawned;
            
            if (!NetworkManager.Singleton.IsServer)
            {
                return;
            }
            //server only
            
            GameManager.instance.onGameStarted += OnGameStarted;
        }

        private void OnPowerSpawned(Power _newPower)
        {
            if (!NetworkManager.Singleton.IsServer)
            {
                return;
            }

            if (GameManager.instance.hasGameStarted)
            {
                _newPower.OnGameStartedServer();
            }

            _newPower.onPowerUsed += () => { OnPowerUsedServer(_newPower); };
        }

        private void OnPowerUsedServer(Power _newPower)
        {
            if (!NetworkManager.Singleton.IsServer)
            {
                return;
            }

            var _gameManager = GameManager.instance;
            if (_gameManager.GetGameState(_gameManager.currentGameStateIndex.Value) is AwakeningState _awakeningState)
            {
                _awakeningState.OnPowerUsedServer(_newPower);
            }
        }

        private void OnGameStarted()
        {
            Assert.IsTrue(NetworkManager.Singleton.IsServer, "OnGameStarted should only be called on the server");
            
            foreach (var _rolePower in GameManager.instance.characterManager.GetCharacters().SelectMany(_character => _character.role.powers))
            {
                if (GameManager.instance.characterManager.GetCharacter(_rolePower.ownerClientId.Value, false).isFake)
                {
                    continue;
                }
                _rolePower.OnGameStartedServer();
            }

            foreach (var _character in GameManager.instance.characterManager.GetCharacters())
            {
                _character.onCharacterAwakened += () => OnCharacterAwakenedServer(_character);
                foreach (Power _characterPower in _character.role.powers)
                {
                    
                }
            }
        }

        private void OnCharacterAwakenedServer(Character _character)
        {
            
        }
        
        public void ReparentPowerToCharacterServer(Power _power, Character _newOwner)
        {
            if (!NetworkManager.Singleton.IsServer)
            {
                Debug.LogError("ReparentPowerToCharacterServer should only be called on the server");
                return;
            }
            
            if (_power.ownerCharacter == _newOwner)
            {
                Debug.LogWarning($"Power {_power.powerName} is already owned by character {_newOwner.ownerClientId.Value}");
                return;
            }

            if (_power.ownerCharacter != null)
            {
                RemovePowerFromCharacterPowerListRpc(_power.ownerClientId.Value, new(_power));
            }

            _power.GetComponent<NetworkObject>().TrySetParent(_newOwner.GetComponent<NetworkObject>());
            
            _power.ownerClientId.Value = _newOwner.ownerClientId.Value;
            OnPowerReparentedServer(_power);
        }

        public void OnPowerReparentedServer(Power _power)
        {
            Assert.IsTrue(NetworkManager.Singleton.IsServer, "OnPowerReparented should only be called on the server");

            _power.OnReparentedServer();
        }
        
        [Rpc(SendTo.Everyone)]
        public void RemovePowerFromCharacterPowerListRpc(ulong _characterId, NetworkBehaviourReference _powerNetworkRef)
        {
            Character _character = CharacterManager.instance.GetCharacter(_characterId);
            Assert.IsNotNull(_character, $"Character with id {_characterId} not found when trying to remove power");
            _powerNetworkRef.TryGet(out Power _power);
            Assert.IsNotNull(_power, $"Power with id {_powerNetworkRef} not found on character {_characterId}");
            
            if (_character.role.powers.Contains(_power))
            {
                _character.role.powers.Remove(_power);
            }
        }

        private void OnDestroy()
        {
            Power.onPowerSpawned -= OnPowerSpawned;

            if (instance == this)
            {
                instance = null;
            }
        }
    }
}