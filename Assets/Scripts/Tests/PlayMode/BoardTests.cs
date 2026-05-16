using System.Collections;
using System.Collections.Generic;
using Board;
using Board.CardComponents;
using Characters;
using GameLogic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Unity.Netcode;
using System.Linq;
using NSubstitute;
using TMPro;
using UnityEngine.UI;

namespace Tests.PlayMode
{
    public class BoardTests
    {
        private GameObject _networkManagerGo;
        private NetworkManager _networkManager;
        private GameObject _gameManagerGo;
        private GameManager _gameManager;
        private GameObject _characterManagerGo;
        private CharacterManager _characterManager;
        private GameObject _boardManagerGo;
        private BoardManager _boardManager;

        private GameObject _dummyCharPrefab;

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
                EnableSceneManagement = false 
            };
            
            _dummyCharPrefab = new GameObject("CharacterPrefab_Board");
            _dummyCharPrefab.AddComponent<NetworkObject>();
            _dummyCharPrefab.AddComponent<Character>();
            _networkManager.NetworkConfig.Prefabs.Add(new NetworkPrefab { Prefab = _dummyCharPrefab });

            _networkManager.StartHost();

            _gameManagerGo = new GameObject("GameManager");
            _gameManagerGo.AddComponent<NetworkObject>();
            _gameManager = _gameManagerGo.AddComponent<GameManager>();
            _gameManager.ignoreGameLoop = true;
            var dummyState = ScriptableObject.CreateInstance<DummyGameState>();
            _gameManager.gameStates.Add(dummyState, new GameStateSettings());
            _gameManager.GetComponent<NetworkObject>().Spawn();

            _characterManagerGo = new GameObject("CharacterManager");
            _characterManagerGo.AddComponent<NetworkObject>();
            _characterManager = _characterManagerGo.AddComponent<CharacterManager>();
            _gameManager.characterManager = _characterManager;
            _characterManager.GetComponent<NetworkObject>().Spawn();
            
            GameObject charactersParent = new GameObject("CharactersParent");
            charactersParent.AddComponent<NetworkObject>().Spawn();
            ReflectionHelper.SetPrivateField(_characterManager, "_charactersParent", charactersParent.transform);
            ReflectionHelper.SetPrivateField(_characterManager, "_characterPrefab", _dummyCharPrefab.GetComponent<NetworkObject>());

            _boardManagerGo = new GameObject("BoardManager");
            _boardManagerGo.AddComponent<NetworkObject>();
            _boardManager = _boardManagerGo.AddComponent<BoardManager>();
            _boardManager.GetComponent<NetworkObject>().Spawn();

            yield return new WaitUntil(() => _gameManager.IsSpawned && _characterManager.IsSpawned && _boardManager.IsSpawned);
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (_networkManager != null && _networkManager.IsListening) _networkManager.Shutdown();
            
            ReflectionHelper.SetPrivateField(typeof(GameManager), "instance", null);
            ReflectionHelper.SetPrivateField(typeof(CharacterManager), "instance", null);
            ReflectionHelper.SetPrivateField(typeof(BoardManager), "instance", null);

            Object.Destroy(_gameManagerGo);
            Object.Destroy(_characterManagerGo);
            Object.Destroy(_boardManagerGo);
            Object.Destroy(_networkManagerGo);
            Object.Destroy(_dummyCharPrefab);
            yield return null;
        }

        private Card CreateMockCard(string name)
        {
            GameObject cardGo = new GameObject(name);
            cardGo.SetActive(false); // CRITICAL: Disable to prevent Awake
            
            Card card = cardGo.AddComponent<Card>();
            
            // Inject mocks BEFORE enabling the object
            ReflectionHelper.SetPrivateField(card, "visualUpdater", Substitute.For<ICardDisplay>());
            ReflectionHelper.SetPrivateField(card, "animationHandler", Substitute.For<ICardAnimation>());
            
            cardGo.SetActive(true); // Now Awake can run safely with Mocks
            return card;
        }

        [UnityTest]
        public IEnumerator BoardManager_TrackVisibleCards_WhenCharactersAreUpdated()
        {
            Assert.AreEqual(0, _boardManager.visibleCards.Count);

            Card card = CreateMockCard("CardInstance");
            _boardManager.visibleCards.Add(card);

            Character character = _characterManager.AddNewCharacter(_networkManager.LocalClientId);
            yield return new WaitUntil(() => character.IsSpawned);

            card.characterInfo = character;
            
            ReflectionHelper.InvokePrivateMethod(_boardManager, "OnCharacterListUpdated", _characterManager.GetCharacters(false).ToList());
            
            Assert.IsTrue(_boardManager.hasAllCardsShown, "BoardManager should detect that all real players have cards");
        }

        [UnityTest]
        public IEnumerator Card_SetInfo_CorrectlyAssignsData()
        {
            Card card = CreateMockCard("CardInstance_SetInfo");
            
            Character character = _characterManager.AddNewCharacter(111);
            yield return new WaitUntil(() => character.IsSpawned);
            
            Role role = new Role { roleName = "TestRole" };
            character.role = role;

            card.SetInfo(character);

            Assert.AreEqual(character, card.characterInfo);
            Assert.AreEqual("TestRole", card.roleInfo.roleName.ToString());
            
            Object.Destroy(card.gameObject);
        }
    }
}
