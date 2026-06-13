using System.Collections;
using System.Collections.Generic;
using ChatSystem;
using GameLogic;
using AudioSystem;
using Characters;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Unity.Netcode;
using Unity.Collections;
using NSubstitute;

namespace Tests.PlayMode
{
    public class ChatManagerTests
    {
        private GameObject _networkManagerGo;
        private NetworkManager _networkManager;
        private GameObject _gameManagerGo;
        private GameManager _gameManager;
        private GameObject _characterManagerGo;
        private CharacterManager _characterManager;
        private GameObject _chatManagerGo;
        private ChatManager _chatManager;

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
            
            _dummyCharPrefab = new GameObject("CharacterPrefab_Chat");
            _dummyCharPrefab.AddComponent<NetworkObject>();
            _dummyCharPrefab.AddComponent<Character>();
            _networkManager.NetworkConfig.Prefabs.Add(new NetworkPrefab { Prefab = _dummyCharPrefab });

            Assert.IsTrue(_networkManager.StartHost(), "NGO StartHost() failed — server did not start.");

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
            ReflectionHelper.SetPrivateField(_gameManager, "characterManager", _characterManager);
            _characterManager.GetComponent<NetworkObject>().Spawn();
            
            GameObject charactersParent = new GameObject("CharactersParent");
            charactersParent.AddComponent<NetworkObject>().Spawn();
            ReflectionHelper.SetPrivateField(_characterManager, "_charactersParent", charactersParent.transform);
            ReflectionHelper.SetPrivateField(_characterManager, "_characterPrefab", _dummyCharPrefab.GetComponent<NetworkObject>());

            // Setup AudioManager (needed for ChatManager)
            GameObject audioGo = new GameObject("AudioManager");
            audioGo.AddComponent<GameAudioManager>();

            // Setup ChatManager
            _chatManagerGo = new GameObject("ChatManager");
            _chatManagerGo.SetActive(false); // PRE-AWAKE INJECTION
            _chatManager = _chatManagerGo.AddComponent<ChatManager>();
            _chatManagerGo.AddComponent<NetworkObject>();
            _chatManagerGo.SetActive(true);
            _chatManager.GetComponent<NetworkObject>().Spawn();

            yield return NetworkTestHelper.WaitUntilAllSpawnedOrTimeout(_gameManager, _characterManager, _chatManager);
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (_networkManager != null && _networkManager.IsListening) _networkManager.Shutdown();
            yield return NetworkTestHelper.WaitUntilOrTimeout(() => _networkManager == null || !_networkManager.IsListening, 5f, "NGO did not stop listening within 5s after Shutdown().");

            ReflectionHelper.SetPrivateField(typeof(GameManager), "instance", null);
            ReflectionHelper.SetPrivateField(typeof(CharacterManager), "instance", null);
            ReflectionHelper.SetPrivateField(typeof(ChatManager), "instance", null);
            ReflectionHelper.SetPrivateField(typeof(GameAudioManager), "instance", null);

            Object.Destroy(_gameManagerGo);
            Object.Destroy(_characterManagerGo);
            Object.Destroy(_chatManagerGo);
            Object.Destroy(_networkManagerGo);
            Object.Destroy(_dummyCharPrefab);
            Object.Destroy(GameObject.Find("AudioManager"));
            yield return null;
        }

        [UnityTest]
        public IEnumerator ChatManager_DiscoverChat_TriggersEvent()
        {
            int resultId = -1;
            _chatManager.onChatDiscovered += (id) => resultId = id;
            
            _chatManager.DiscoverChat(100, "Secret Chat");

            Assert.AreEqual(100, resultId);
            Assert.IsTrue(_chatManager.discoveredChatIds.Contains(100));
            Assert.AreEqual("Secret Chat", _chatManager.GetChatWindowName(100));
            yield return null;
        }

        [UnityTest]
        public IEnumerator ChatManager_ChangeActiveChat_OnlyWorksIfDiscovered()
        {
            int generalId = (int)ChatWindowIDs.General;
            Assert.AreEqual(generalId, _chatManager.activeChatId);

            _chatManager.ChangeActiveChat(999);
            Assert.AreEqual(generalId, _chatManager.activeChatId, "Should not change to undiscovered chat");

            _chatManager.DiscoverChat(999);
            _chatManager.ChangeActiveChat(999);
            Assert.AreEqual(999, _chatManager.activeChatId, "Should change to discovered chat");
            yield return null;
        }

        [UnityTest]
        public IEnumerator ChatManager_ReceiveMessage_AddsToWindow()
        {
            int generalId = (int)ChatWindowIDs.General;
            ChatMessage msg = new ChatMessage(123, new FixedString512Bytes("Hello World"), generalId);
            
            bool eventReceived = false;
            _chatManager.onChatMessageReceived += (m) => eventReceived = true;

            _chatManager.ReceiveChatMessageRpc(msg);

            ChatWindow window = _chatManager.GetChatWindow(generalId);
            Assert.AreEqual(1, window.chatMessages.Count);
            Assert.AreEqual("Hello World", window.chatMessages[0].message.ToString());
            Assert.IsTrue(eventReceived);
            yield return null;
        }

        // Story 10.1 (Epic 10 / D4) — boot smoke for ChatManager injection (AC2, AC4): the chat
        // surface the powers now consume resolves through the composition root to the singleton, and a
        // chat message SENT in a host→bot context (sender clientId >= 100) still travels the full server
        // path. The bot is host-simulated, so this is host-local — no remote client needed. NFR5: the
        // server body routes the sent-notification via GetSafeRpcTarget(bot), the >= 100 redirect onto
        // the host (client 0), exercised verbatim — byte-identical after the powers stopped reading the
        // global. (The pure GetSafeRpcTarget interception proof lives in CharacterCommandBotFlowTests.)
        [UnityTest]
        public IEnumerator ChatManager_ResolvesThroughRoot_AndSendsHostToBot()
        {
            // The composition-root accessor the migrated Power/PowerComponent.chatManager base field
            // resolves to (CompositionRoot.For(nm).ChatManager). ChatManager is not de-singletonised, so
            // the root serves the single global instance — no registered root needed for this accessor.
            Assert.AreSame(_chatManager, CompositionRoot.For(_networkManager).ChatManager,
                "CompositionRoot.For(nm).ChatManager must resolve the spawned ChatManager singleton.");

            const ulong botClientId = 100; // >= 100 => host-simulated bot, intercepted by GetSafeRpcTarget.
            int generalId = (int)ChatWindowIDs.General;
            bool received = false;
            _chatManager.onChatMessageReceived += (_) => received = true;

            Assert.DoesNotThrow(
                () => _chatManager.SendChatMessageServerRpc(
                    new ChatMessage(botClientId, new FixedString512Bytes("bot says hi"), generalId)),
                "SendChatMessageServerRpc in a host→bot context must reach the server RPC body (it routes the " +
                "sent-notification through GetSafeRpcTarget(botClientId)).");

            yield return null;

            Assert.IsTrue(received, "The host→bot chat message must be delivered to the General window.");
        }
    }
}
