using System.Collections;
using GameLogic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Unity.Netcode;

namespace Tests.PlayMode
{
    public class GameManagerTests
    {
        private GameObject _gameManagerGo;
        private GameManager _gameManager;
        private GameObject _networkManagerGo;
        private NetworkManager _networkManager;

        // Dummy GameState for testing
        private class DummyGameState : GameState 
        {
            public override void StateUpdateClient() {}
            public override void StateUpdateServer() {}
        }

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            // Setup NetworkManager
            _networkManagerGo = new GameObject("NetworkManager");
            _networkManager = _networkManagerGo.AddComponent<NetworkManager>();
            _networkManager.NetworkConfig = new NetworkConfig
            {
                NetworkTransport = _networkManagerGo.AddComponent<Unity.Netcode.Transports.UTP.UnityTransport>()
            };
            
            _networkManager.StartHost();

            // Setup GameManager
            _gameManagerGo = new GameObject("GameManager");
            _gameManager = _gameManagerGo.AddComponent<GameManager>();
            _gameManager.ignoreGameLoop = true; // Prevent update loop crashes

            // Add at least one dummy state to avoid ElementAt(0) error if ignoreGameLoop is ignored
            var dummyState = ScriptableObject.CreateInstance<DummyGameState>();
            _gameManager.gameStates.Add(dummyState, new GameStateSettings());

            yield return null; 
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (_networkManager != null && _networkManager.IsListening)
            {
                _networkManager.Shutdown();
            }

            Object.Destroy(_gameManagerGo);
            Object.Destroy(_networkManagerGo);
            
            yield return null;
        }

        [UnityTest]
        public IEnumerator GameManager_Singleton_IsInitialized()
        {
            Assert.IsNotNull(GameManager.instance);
            Assert.AreEqual(_gameManager, GameManager.instance);
            yield return null;
        }

        [UnityTest]
        public IEnumerator GameManager_InitialState_IsZero()
        {
            // Wait for NetworkSpawn if possible (might need to attach NetworkObject)
            Assert.AreEqual(0, _gameManager.gameLoopCount);
            yield return null;
        }
    }
}
