using System.Collections;
using Characters;
using Characters.Powers;
using GameLogic;
using AudioSystem;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Unity.Netcode;

namespace Tests.PlayMode
{
    public class PowerTests
    {
        private GameObject _networkManagerGo;
        private NetworkManager _networkManager;
        private GameObject _gameManagerGo;
        private GameManager _gameManager;
        private GameObject _characterManagerGo;
        private CharacterManager _characterManager;
        private GameObject _audioManagerGo;
        private GameAudioManager _audioManager;

        private GameObject _dummyCharPrefab;
        private Power _testPower;

        private class DummyGameState : GameState 
        {
            public override void StateUpdateClient() {}
            public override void StateUpdateServer() {}
        }

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            _networkManagerGo = new GameObject("NetworkManager");
            _networkManager = _networkManagerGo.AddComponent<NetworkManager>();
            _networkManager.NetworkConfig = new NetworkConfig
            {
                NetworkTransport = _networkManagerGo.AddComponent<Unity.Netcode.Transports.UTP.UnityTransport>(),
                // Disable Scene Management to avoid scene object registration issues in tests
                EnableSceneManagement = false 
            };
            
            _dummyCharPrefab = new GameObject("CharacterPrefab_" + Random.Range(1, 9999));
            _dummyCharPrefab.AddComponent<NetworkObject>();
            _dummyCharPrefab.AddComponent<Character>();
            
            _networkManager.NetworkConfig.Prefabs.Add(new NetworkPrefab { Prefab = _dummyCharPrefab });

            _networkManager.StartHost();

            _audioManagerGo = new GameObject("AudioManager");
            _audioManager = _audioManagerGo.AddComponent<GameAudioManager>();

            _gameManagerGo = new GameObject("GameManager");
            _gameManager = _gameManagerGo.AddComponent<GameManager>();
            _gameManager.ignoreGameLoop = true;
            
            var dummyState = ScriptableObject.CreateInstance<DummyGameState>();
            _gameManager.gameStates.Add(dummyState, new GameStateSettings());

            _characterManagerGo = new GameObject("CharacterManager");
            _characterManager = _characterManagerGo.AddComponent<CharacterManager>();
            _gameManager.characterManager = _characterManager;
            
            GameObject charactersParent = new GameObject("CharactersParent");
            charactersParent.AddComponent<NetworkObject>().Spawn();
            
            ReflectionHelper.SetPrivateField(_characterManager, "_charactersParent", charactersParent.transform);
            ReflectionHelper.SetPrivateField(_characterManager, "_characterPrefab", _dummyCharPrefab.GetComponent<NetworkObject>());

            // Setup a power
            GameObject powerGo = new GameObject("TestPower");
            var powerNetObj = powerGo.AddComponent<NetworkObject>();
            _testPower = powerGo.AddComponent<Power>();
            _testPower.powerName = "TestPower";
            powerNetObj.Spawn();
            _testPower.idHolderServer = _networkManager.LocalClientId;

            yield return null; 
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (_networkManager != null && _networkManager.IsListening)
            {
                _networkManager.Shutdown();
            }

            // Cleanup objects
            Object.Destroy(_gameManagerGo);
            Object.Destroy(_characterManagerGo);
            Object.Destroy(_audioManagerGo);
            Object.Destroy(_networkManagerGo);
            Object.Destroy(_dummyCharPrefab);
            
            yield return null;
        }

        [UnityTest]
        public IEnumerator Power_CanUse_ReturnsFalse_WhenCharacterIsChained()
        {
            Character testCharacter = _characterManager.AddNewCharacter(_networkManager.LocalClientId);
            yield return null;

            testCharacter.isChained.Value = true;
            _testPower.powerUseLeft.Value = 1;
            _testPower.isPassive = false;
            
            Assert.IsFalse(_testPower.CanUse(), "Power should not be usable when character is chained");
        }

        [UnityTest]
        public IEnumerator Power_CanUse_ReturnsFalse_WhenNoUsesLeft()
        {
            Character testCharacter = _characterManager.AddNewCharacter(_networkManager.LocalClientId);
            yield return null;

            testCharacter.isChained.Value = false;
            _testPower.powerUseLeft.Value = 0;
            
            Assert.IsFalse(_testPower.CanUse(), "Power should not be usable with 0 uses left");
        }
    }
}
