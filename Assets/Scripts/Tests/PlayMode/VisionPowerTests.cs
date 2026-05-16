using System.Collections;
using Characters;
using Characters.Powers;
using GameLogic;
using AudioSystem;
using RoleTarget;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Unity.Netcode;
using System.Collections.Generic;

namespace Tests.PlayMode
{
    public class VisionPowerTests
    {
        private GameObject _networkManagerGo;
        private NetworkManager _networkManager;
        private GameObject _gameManagerGo;
        private GameManager _gameManager;
        private GameObject _characterManagerGo;
        private CharacterManager _characterManager;
        private GameObject _revealerGo;
        private GameInfoRevealer _revealer;

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
            
            _dummyCharPrefab = new GameObject("CharacterPrefab_Vision");
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

            _revealerGo = new GameObject("GameInfoRevealer");
            _revealerGo.AddComponent<NetworkObject>();
            _revealer = _revealerGo.AddComponent<GameInfoRevealer>();
            _revealer.GetComponent<NetworkObject>().Spawn();
            _gameManager.gameInfoRevealer = _revealer;

            // Setup RoleTargetSystem (singleton)
            GameObject rtsGo = new GameObject("RoleTargetSystem");
            var rtsNetObj = rtsGo.AddComponent<NetworkObject>();
            var rts = rtsGo.AddComponent<RoleTargetSystem>();
            rtsNetObj.Spawn();

            // Setup BoardManager (singleton - in root namespace)
            GameObject boardManagerGo = new GameObject("BoardManager");
            boardManagerGo.AddComponent<BoardManager>();

            // Setup AudioManager (needed for Power)
            GameObject audioGo = new GameObject("AudioManager");
            audioGo.AddComponent<GameAudioManager>();

            // Deterministic wait for all network objects to be ready
            yield return new WaitUntil(() => _gameManager.IsSpawned && _characterManager.IsSpawned && _revealer.IsSpawned);
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (_networkManager != null && _networkManager.IsListening) _networkManager.Shutdown();
            
            // Note: Singletons are cleaned up by Object.Destroy calling OnDestroy in original scripts
            // or automatically handled by Unity on scene/test teardown.
            // Explicitly setting back to null via reflection if needed for read-only ones.
            ReflectionHelper.SetPrivateField(typeof(GameManager), "instance", null);
            ReflectionHelper.SetPrivateField(typeof(CharacterManager), "instance", null);
            ReflectionHelper.SetPrivateField(typeof(RoleTargetSystem), "instance", null);
            ReflectionHelper.SetPrivateField(typeof(BoardManager), "instance", null);
            ReflectionHelper.SetPrivateField(typeof(GameAudioManager), "instance", null);

            Object.Destroy(_gameManagerGo);
            Object.Destroy(_characterManagerGo);
            Object.Destroy(_revealerGo);
            Object.Destroy(GameObject.Find("RoleTargetSystem"));
            Object.Destroy(GameObject.Find("BoardManager"));
            Object.Destroy(_networkManagerGo);
            Object.Destroy(_dummyCharPrefab);
            Object.Destroy(GameObject.Find("AudioManager"));
            yield return null;
        }

        [UnityTest]
        public IEnumerator PCorruptionInsight_RevealsCorruptionAtStart()
        {
            // Create Owner
            Character owner = _characterManager.AddNewCharacter(_networkManager.LocalClientId);
            // Create Target
            Character target = _characterManager.AddNewCharacter(12345); // Other client
            yield return new WaitUntil(() => owner.IsSpawned && target.IsSpawned);

            GameObject powerGo = new GameObject("CorruptionInsight");
            var powerNetObj = powerGo.AddComponent<NetworkObject>();
            var power = powerGo.AddComponent<PCorruptionInsight>();
            powerNetObj.Spawn();
            power.ownerClientId.Value = _networkManager.LocalClientId;
            
            yield return new WaitUntil(() => power.IsSpawned);

            // Initial state: not revealed
            var info = _revealer.GetCharacterInfo(target.ownerClientId.Value, owner.ownerClientId.Value);
            Assert.AreEqual(RevealLevel.False, info.isCorruptRevealed);

            // Trigger start
            power.OnGameStartedServer();
            yield return null; 

            // Result: revealed
            info = _revealer.GetCharacterInfo(target.ownerClientId.Value, owner.ownerClientId.Value);
            Assert.AreEqual(RevealLevel.Personal, info.isCorruptRevealed, "CorruptionInsight should reveal corruption level to owner");
        }

        [UnityTest]
        public IEnumerator POmniscience_RevealsRoleOnUsage()
        {
            // Create Owner
            Character owner = _characterManager.AddNewCharacter(_networkManager.LocalClientId);
            // Create Target
            Character target = _characterManager.AddNewCharacter(54321);
            yield return new WaitUntil(() => owner.IsSpawned && target.IsSpawned);

            GameObject powerGo = new GameObject("Omniscience");
            var powerNetObj = powerGo.AddComponent<NetworkObject>();
            var power = powerGo.AddComponent<POmniscience>();
            powerNetObj.Spawn();
            power.ownerClientId.Value = _networkManager.LocalClientId;

            yield return new WaitUntil(() => power.IsSpawned);

            // Manual trigger of the "OnClick" logic because BoardManager is too complex to mock here
            ReflectionHelper.InvokePrivateMethod(power, "OnCardClickedRpc", target.ownerClientId.Value);
            yield return null;

            var info = _revealer.GetCharacterInfo(target.ownerClientId.Value, owner.ownerClientId.Value);
            Assert.AreEqual(RevealLevel.Personal, info.isRoleRevealed, "Omniscience should reveal role to owner");
            Assert.AreEqual(target.ownerClientId.Value, power.hackedCharacterClientId, "Omniscience should store the target ID");
        }
    }
}
