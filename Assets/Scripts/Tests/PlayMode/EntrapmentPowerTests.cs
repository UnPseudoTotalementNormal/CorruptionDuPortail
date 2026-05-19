using System.Collections;
using Characters;
using Characters.Powers;
using GameLogic;
using AudioSystem;
using RoleTarget;
using Board;
using ChatSystem;
using Network;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Unity.Netcode;
using System.Collections.Generic;
using System.Linq;

namespace Tests.PlayMode
{
    public class EntrapmentPowerTests
    {
        private GameObject _networkManagerGo;
        private NetworkManager _networkManager;
        private GameObject _gameManagerGo;
        private GameManager _gameManager;
        private GameObject _characterManagerGo;
        private CharacterManager _characterManager;

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
            
            _dummyCharPrefab = new GameObject("CharacterPrefab_Entrapment");
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
            _gameManager.characterManager = _characterManager;
            _characterManager.GetComponent<NetworkObject>().Spawn();
            
            GameObject charactersParent = new GameObject("CharactersParent");
            charactersParent.AddComponent<NetworkObject>().Spawn();
            ReflectionHelper.SetPrivateField(_characterManager, "_charactersParent", charactersParent.transform);
            ReflectionHelper.SetPrivateField(_characterManager, "_characterPrefab", _dummyCharPrefab.GetComponent<NetworkObject>());

            // Singletons
            new GameObject("RoleTargetSystem").AddComponent<RoleTargetSystem>().gameObject.AddComponent<NetworkObject>().Spawn();
            new GameObject("BoardManager").AddComponent<BoardManager>();
            new GameObject("AudioManager").AddComponent<GameAudioManager>();
            new GameObject("ChatManager").AddComponent<ChatManager>().gameObject.AddComponent<NetworkObject>().Spawn();
            new GameObject("LobbyPlayerInfoHolder").AddComponent<LobbyPlayerInfoHolder>().gameObject.AddComponent<NetworkObject>().Spawn();
            new GameObject("ChainingManager").AddComponent<ChainingManager>().gameObject.AddComponent<NetworkObject>().Spawn();
            new GameObject("PowerManager").AddComponent<PowerManager>().gameObject.AddComponent<NetworkObject>().Spawn();

            yield return NetworkTestHelper.WaitUntilAllSpawnedOrTimeout(_gameManager, _characterManager);
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (_networkManager != null && _networkManager.IsListening) _networkManager.Shutdown();
            yield return NetworkTestHelper.WaitUntilOrTimeout(() => _networkManager == null || !_networkManager.IsListening, 5f, "NGO did not stop listening within 5s after Shutdown().");

            ReflectionHelper.SetPrivateField(typeof(GameManager), "instance", null);
            ReflectionHelper.SetPrivateField(typeof(CharacterManager), "instance", null);
            ReflectionHelper.SetPrivateField(typeof(RoleTargetSystem), "instance", null);
            ReflectionHelper.SetPrivateField(typeof(BoardManager), "instance", null);
            ReflectionHelper.SetPrivateField(typeof(GameAudioManager), "instance", null);
            ReflectionHelper.SetPrivateField(typeof(ChatManager), "instance", null);
            ReflectionHelper.SetPrivateField(typeof(LobbyPlayerInfoHolder), "instance", null);
            ReflectionHelper.SetPrivateField(typeof(ChainingManager), "instance", null);
            ReflectionHelper.SetPrivateField(typeof(PowerManager), "instance", null);

            Object.Destroy(_gameManagerGo);
            Object.Destroy(_characterManagerGo);
            Object.Destroy(GameObject.Find("RoleTargetSystem"));
            Object.Destroy(GameObject.Find("BoardManager"));
            Object.Destroy(GameObject.Find("ChatManager"));
            Object.Destroy(GameObject.Find("LobbyPlayerInfoHolder"));
            Object.Destroy(GameObject.Find("ChainingManager"));
            Object.Destroy(GameObject.Find("PowerManager"));
            Object.Destroy(_networkManagerGo);
            Object.Destroy(_dummyCharPrefab);
            Object.Destroy(GameObject.Find("AudioManager"));
            yield return null;
        }

        [UnityTest]
        public IEnumerator PTruthChains_AddsAnomalyToChainingList()
        {
            Character owner = _characterManager.AddNewCharacter(_networkManager.LocalClientId);
            Character target = _characterManager.AddNewCharacter(777);
            yield return NetworkTestHelper.WaitUntilAllSpawnedOrTimeout(owner, target);

            // Target is Anomaly
            target.role = new Role { factionType = FactionType.anomaly };

            GameObject powerGo = new GameObject("TruthChains");
            var power = powerGo.AddComponent<PTruthChains>();
            powerGo.AddComponent<NetworkObject>().Spawn();
            power.ownerClientId.Value = _networkManager.LocalClientId;
            yield return NetworkTestHelper.WaitUntilSpawnedOrTimeout(power);

            ReflectionHelper.InvokePrivateMethod(power, "OnCardClickedRpc", target.ownerClientId.Value);
            yield return null;

            Assert.IsTrue(ChainingManager.instance.chainingPlayers.Contains(target.ownerClientId.Value), 
                "TruthChains should add Anomaly target to chaining list");
        }

        [UnityTest]
        public IEnumerator PTruthChains_DoesNotAddNonAnomalyToChainingList()
        {
            Character owner = _characterManager.AddNewCharacter(_networkManager.LocalClientId);
            Character target = _characterManager.AddNewCharacter(666);
            yield return NetworkTestHelper.WaitUntilAllSpawnedOrTimeout(owner, target);

            // Target is NOT Anomaly (e.g. Chosen)
            target.role = new Role { factionType = FactionType.chosen };

            GameObject powerGo = new GameObject("TruthChains");
            var power = powerGo.AddComponent<PTruthChains>();
            powerGo.AddComponent<NetworkObject>().Spawn();
            power.ownerClientId.Value = _networkManager.LocalClientId;
            yield return NetworkTestHelper.WaitUntilSpawnedOrTimeout(power);

            ReflectionHelper.InvokePrivateMethod(power, "OnCardClickedRpc", target.ownerClientId.Value);
            yield return null;

            Assert.IsFalse(ChainingManager.instance.chainingPlayers.Contains(target.ownerClientId.Value), 
                "TruthChains should NOT add non-Anomaly target to chaining list");
        }

        [UnityTest]
        public IEnumerator PLegacy_InheritsPowerWhenTargetIsChained()
        {
            Character owner = _characterManager.AddNewCharacter(_networkManager.LocalClientId);
            Character target = _characterManager.AddNewCharacter(555);
            yield return NetworkTestHelper.WaitUntilAllSpawnedOrTimeout(owner, target);

            target.role = new Role { roleID = RoleID.Omniscient }; // Target role
            owner.role = new Role { roleID = RoleID.Dryade }; // Owner role

            GameObject powerGo = new GameObject("Legacy");
            var power = powerGo.AddComponent<PLegacy>();
            
            // Create the power to inherit
            GameObject inheritedGo = new GameObject("InheritedPower");
            inheritedGo.AddComponent<NetworkObject>();
            var inheritedPower = inheritedGo.AddComponent<Power>();
            inheritedPower.powerName = "Inherited";
            inheritedGo.GetComponent<NetworkObject>().Spawn();

            power.roleForLegacy = RoleID.Omniscient;
            power.legacyPower = inheritedPower;

            powerGo.AddComponent<NetworkObject>().Spawn();
            power.ownerClientId.Value = _networkManager.LocalClientId;
            yield return NetworkTestHelper.WaitUntilSpawnedOrTimeout(power);

            // Trigger StartServer logic to subscribe to events
            power.OnGameStartedServer();

            // Simulate target being chained
            target.isChained.Value = true;
            
            // Wait for the asynchronous power transfer to complete (WaitUntil with timeout)
            float timeout = Time.time + 2.0f;
            yield return new WaitUntil(() => power.isLegacyInherited && owner.role.powers.Any(p => p.powerName == inheritedPower.powerName) || Time.time > timeout);

            Assert.IsTrue(power.isLegacyInherited, "Legacy should be marked as inherited");
            Assert.IsTrue(owner.role.powers.Any(p => p.powerName == inheritedPower.powerName), "Owner should have received a clone of the inherited power within timeout");
        }
    }
}
