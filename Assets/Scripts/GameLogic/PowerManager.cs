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

        public void OnPowerReparentedServer(Power _power)
        {
            Assert.IsTrue(NetworkManager.Singleton.IsServer, "OnPowerReparented should only be called on the server");

            _power.OnReparentedServer();
        }
    }
}