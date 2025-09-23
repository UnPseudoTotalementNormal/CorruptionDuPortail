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
                Destroy(this);
            }
            else
            {
                instance = this;
            }
        }

        private void Start()
        {
            if (!NetworkManager.Singleton.IsServer)
            {
                return;
            }
            //server only
            
            GameManager.instance.onGameStarted += OnGameStarted;
        }

        private void OnGameStarted()
        {
            Assert.IsTrue(NetworkManager.Singleton.IsServer, "OnGameStarted should only be called on the server");
            
            foreach (var _rolePower in GameManager.instance.GetCharacters().SelectMany(_character => _character.role.powers))
            {
                if (GameManager.instance.GetCharacter(_rolePower.ownerClientId, false).isFake)
                {
                    continue;
                }
                _rolePower.OnGameStartedServer();
            }

            foreach (var _character in GameManager.instance.GetCharacters())
            {
                _character.onCharacterAwakened += () => OnCharacterAwakenedServer(_character);
                foreach (Power _characterPower in _character.role.powers)
                {
                    
                }
            }
        }

        private void OnCharacterAwakenedServer(Character _character)
        {
            List<Power> _characterPowers = _character.role.powers;
            foreach (Power _rolePower in _characterPowers)
            {
                if (_rolePower is ICorruptionChainPower _corruptionChainPower)
                {
                    HandleCorruptionChainPower(_character, _corruptionChainPower, _rolePower);
                }
            }
        }

        private void HandleCorruptionChainPower(Character _character, ICorruptionChainPower _corruptionChainPower, Power _rolePower)
        {
            _corruptionChainPower.currentCorruptionChain = 0; ;
            Action<Character> _corruptionChainSuccessHandler = (_corruptedCharacter) =>
            {
                _corruptionChainPower.currentCorruptionChain++;
                if (_corruptionChainPower.currentCorruptionChain >= _corruptionChainPower.maxCorruptionChain)
                {
                    _rolePower.powerUseLeft = 0;
                }
                GameManager.instance.AskForUpdateAllCharactersRpc();
            };
            _corruptionChainPower.onCharacterCorruptionSuccessful += _corruptionChainSuccessHandler;
            _character.onCharacterSleep += () =>
            {
                _corruptionChainPower.onCharacterCorruptionSuccessful -= _corruptionChainSuccessHandler;
            };
        }
    }
}