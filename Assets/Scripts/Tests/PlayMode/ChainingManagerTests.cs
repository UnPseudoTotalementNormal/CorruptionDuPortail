using System.Collections;
using GameLogic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Unity.Netcode;

namespace Tests.PlayMode
{
    public class ChainingManagerTests
    {
        private GameObject _networkManagerGo;
        private NetworkManager _networkManager;
        private GameObject _chainingManagerGo;
        private ChainingManager _chainingManager;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            _networkManagerGo = new GameObject("NetworkManager");
            _networkManager = _networkManagerGo.AddComponent<NetworkManager>();
            _networkManager.NetworkConfig = new NetworkConfig
            {
                NetworkTransport = _networkManagerGo.AddComponent<Unity.Netcode.Transports.UTP.UnityTransport>()
            };
            _networkManager.StartHost();

            _chainingManagerGo = new GameObject("ChainingManager");
            // Important: ChainingManager must have a NetworkObject to use NetworkList/Rpc
            var netObj = _chainingManagerGo.AddComponent<NetworkObject>();
            _chainingManager = _chainingManagerGo.AddComponent<ChainingManager>();
            
            netObj.Spawn();

            yield return null; 
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (_networkManager != null && _networkManager.IsListening)
            {
                _networkManager.Shutdown();
            }

            Object.Destroy(_chainingManagerGo);
            Object.Destroy(_networkManagerGo);
            yield return null;
        }

        [UnityTest]
        public IEnumerator ChainingManager_AddCharacter_AddsToListOnServer()
        {
            ulong characterId = 123;
            _chainingManager.AddCharacterToChainingList(characterId);

            Assert.IsTrue(_chainingManager.chainingPlayers.Contains(characterId));
            Assert.AreEqual(1, _chainingManager.chainingPlayers.Count);
            yield return null;
        }

        [UnityTest]
        public IEnumerator ChainingManager_AddDuplicateCharacter_DoesNotAddTwice()
        {
            ulong characterId = 456;
            _chainingManager.AddCharacterToChainingList(characterId);
            _chainingManager.AddCharacterToChainingList(characterId);

            Assert.AreEqual(1, _chainingManager.chainingPlayers.Count);
            yield return null;
        }
    }
}
