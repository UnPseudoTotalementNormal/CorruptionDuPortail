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
            ReflectionHelper.SetPrivateField(_gameManager, "characterManager", _characterManager);
            _characterManager.GetComponent<NetworkObject>().Spawn();
            
            GameObject charactersParent = new GameObject("CharactersParent");
            charactersParent.AddComponent<NetworkObject>().Spawn();
            ReflectionHelper.SetPrivateField(_characterManager, "_charactersParent", charactersParent.transform);
            ReflectionHelper.SetPrivateField(_characterManager, "_characterPrefab", _dummyCharPrefab.GetComponent<NetworkObject>());

            // Singletons
            new GameObject("RoleTargetSystem").AddComponent<RoleTargetSystem>().gameObject.AddComponent<NetworkObject>().Spawn();
            ReflectionHelper.SetPrivateField(new GameObject("BoardManager").AddComponent<BoardManager>(), "characterManager", _characterManager);
            new GameObject("AudioManager").AddComponent<GameAudioManager>();
            new GameObject("ChatManager").AddComponent<ChatManager>().gameObject.AddComponent<NetworkObject>().Spawn();
            new GameObject("LobbyPlayerInfoHolder").AddComponent<LobbyPlayerInfoHolder>().gameObject.AddComponent<NetworkObject>().Spawn();
            new GameObject("ChainingManager").AddComponent<ChainingManager>().gameObject.AddComponent<NetworkObject>().Spawn();
            // Story 7.4: PowerManager now reads CharacterManager from an injected [SerializeField] (lane A)
            // instead of the GameManager hub-hop; wire it like the production scene does (was implicit before).
            var _powerManager = new GameObject("PowerManager").AddComponent<PowerManager>();
            ReflectionHelper.SetPrivateField(_powerManager, "characterManager", _characterManager);
            // Story 8.3: PowerManager's game-loop reads now resolve through an injected [SerializeField]
            // gameManager (lane A); wire it like the production scene does (was the .instance locator before).
            ReflectionHelper.SetPrivateField(_powerManager, "gameManager", _gameManager);
            _powerManager.gameObject.AddComponent<NetworkObject>().Spawn();

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

        // Powers-POCO v2 wiring golden: PEyeOfTheVoid delegates OnGameStartedServer to EyeOfTheVoidDecision
        // → DiscoverChat(AnomalyOnly) for each anomaly-faction slot. Owner is the anomaly here, so the
        // discover RPC lands on the host and registers the AnomalyOnly chat.
        [UnityTest]
        public IEnumerator PEyeOfTheVoid_DiscoversAnomalyChatForAnomalies()
        {
            Character owner = _characterManager.AddNewCharacter(_networkManager.LocalClientId);
            yield return NetworkTestHelper.WaitUntilSpawnedOrTimeout(owner);
            owner.role = new Role { factionType = FactionType.anomaly };

            GameObject powerGo = new GameObject("EyeOfTheVoid");
            var power = powerGo.AddComponent<PEyeOfTheVoid>();
            powerGo.AddComponent<NetworkObject>().Spawn();
            power.ownerClientId.Value = _networkManager.LocalClientId;
            yield return NetworkTestHelper.WaitUntilSpawnedOrTimeout(power);

            Assert.IsFalse(ChatManager.instance.discoveredChatIds.Contains((int)ChatWindowIDs.AnomalyOnly));

            power.OnGameStartedServer();
            yield return null;

            Assert.IsTrue(ChatManager.instance.discoveredChatIds.Contains((int)ChatWindowIDs.AnomalyOnly),
                "EyeOfTheVoid should discover the anomaly-only chat for an anomaly-faction player.");
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

        // Powers-POCO v2 wiring golden: PReincarnation delegates its server RPC to ReincarnationDecision →
        // NewTargeting + SetPassiveBroadcast(true) + GrantRolePowers. The passive broadcast and the engine
        // power grants are power-local (ISetPassiveState / IGrantRolePowers via SelfState).
        [UnityTest]
        public IEnumerator PReincarnation_GrantsTargetRolePowersAndGoesPassive()
        {
            Character owner = _characterManager.AddNewCharacter(_networkManager.LocalClientId);
            Character target = _characterManager.AddNewCharacter(556);
            yield return NetworkTestHelper.WaitUntilAllSpawnedOrTimeout(owner, target);

            owner.role = new Role { roleID = RoleID.Dryade };

            GameObject grantedGo = new GameObject("GrantedPower");
            grantedGo.AddComponent<NetworkObject>();
            var grantedPower = grantedGo.AddComponent<Power>();
            grantedPower.powerName = "Reincarnated";
            grantedGo.GetComponent<NetworkObject>().Spawn();
            target.role = new Role { roleID = RoleID.Omniscient };
            target.role.powers.Add(grantedPower);

            GameObject powerGo = new GameObject("Reincarnation");
            var power = powerGo.AddComponent<PReincarnation>();
            power.isPassive = false;
            powerGo.AddComponent<NetworkObject>().Spawn();
            power.ownerClientId.Value = _networkManager.LocalClientId;
            yield return NetworkTestHelper.WaitUntilSpawnedOrTimeout(power);

            ReflectionHelper.InvokePrivateMethod(power, "ReincarnatePlayerRpc", target.ownerClientId.Value);

            float timeout = Time.time + 2.0f;
            yield return new WaitUntil(() =>
                owner.role.powers.Any(p => p.powerName == grantedPower.powerName) || Time.time > timeout);

            Assert.IsTrue(power.isPassive, "Reincarnation should broadcast isPassive=true.");
            Assert.IsTrue(owner.role.powers.Any(p => p.powerName == grantedPower.powerName),
                "Reincarnation should grant the target role's powers to the owner.");
        }

        // Powers-POCO v2 wiring golden: PClandestineObservation delegates DeclareAllTargetFocusServer to
        // ClandestineObservationDecision → a single ChatBroadcast announcing the targeting count to the owner.
        // No character carries the observed role here, so it takes the "0." branch. The server-authored
        // message reaches the owner (host) via onChatMessageReceived.
        [UnityTest]
        public IEnumerator PClandestineObservation_AnnouncesTargetingCountToOwner()
        {
            Character owner = _characterManager.AddNewCharacter(_networkManager.LocalClientId);
            yield return NetworkTestHelper.WaitUntilSpawnedOrTimeout(owner);
            owner.role = new Role { roleID = RoleID.Dryade };

            GameObject powerGo = new GameObject("Clandestine");
            var power = powerGo.AddComponent<PClandestineObservation>();
            power.targetRoleID = RoleID.Omniscient; // no character carries it -> the "0." branch
            power.isPassive = false;                // CanUse rejects passive powers; matches the live prefab
            power.hasToBeAwakened = false;
            powerGo.AddComponent<NetworkObject>().Spawn();
            power.ownerClientId.Value = _networkManager.LocalClientId;
            yield return NetworkTestHelper.WaitUntilSpawnedOrTimeout(power);
            power.powerUseLeft.Value = 1;

            bool received = false;
            ulong sender = 0;
            ChatManager.instance.onChatMessageReceived += _m => { received = true; sender = _m.senderClientId; };

            power.DeclareAllTargetFocusServer();
            yield return null;

            Assert.IsTrue(received, "ClandestineObservation should announce the targeting count to the owner.");
            Assert.AreEqual(ChatManager.SERVER_CLIENT_ID, sender,
                "The announcement should come from the server sender id.");
        }

        // Powers-POCO v2 wiring golden: PBoundByInk delegates its server RPC to BoundByInkDecision →
        // NewTargeting + DiscoverChat(power chat id, read via IInkChatState) + RegisterInkTarget (writes the
        // ink lists via IInkTargetRegister). Asserts the picked target is registered.
        [UnityTest]
        public IEnumerator PBoundByInk_RegistersPickedTargetAsInkTarget()
        {
            Character owner = _characterManager.AddNewCharacter(_networkManager.LocalClientId);
            Character target = _characterManager.AddNewCharacter(4321);
            yield return NetworkTestHelper.WaitUntilAllSpawnedOrTimeout(owner, target);

            GameObject powerGo = new GameObject("BoundByInk");
            var power = powerGo.AddComponent<PBoundByInk>();
            powerGo.AddComponent<NetworkObject>().Spawn();
            power.ownerClientId.Value = _networkManager.LocalClientId;
            yield return NetworkTestHelper.WaitUntilSpawnedOrTimeout(power);

            power.OnGameStartedServer(); // attributes the ink chat id (powerChatId)

            ReflectionHelper.InvokePrivateMethod(power, "OnCardClickedRpc", target.ownerClientId.Value);
            yield return null;

            var currentTargets = (List<ulong>)ReflectionHelper.GetPrivateField(power, "currentTargets");
            Assert.IsTrue(currentTargets.Contains(target.ownerClientId.Value),
                "BoundByInk should register the picked character as an ink target.");
        }

        // Powers-POCO v2 wiring golden: PVisionOfTheImpossible delegates its server RPC to
        // VisionOfTheImpossibleDecision. The picked (character ids, guessed roles) are reduced to a
        // VisionGuess list via IVisionGuesses; the decision targets each guessed character until the FIRST
        // whose role matches a guessed role (then stops), and announces a found/not-found line to the owner.
        // Here character #1 carries a guessed role → it is targeted and the "found" line is broadcast.
        [UnityTest]
        public IEnumerator PVisionOfTheImpossible_TargetsMatchAndAnnouncesToOwner()
        {
            Character owner = _characterManager.AddNewCharacter(_networkManager.LocalClientId);
            Character match = _characterManager.AddNewCharacter(881);
            Character other = _characterManager.AddNewCharacter(882);
            yield return NetworkTestHelper.WaitUntilAllSpawnedOrTimeout(owner, match, other);

            match.role = new Role { roleID = RoleID.Omniscient }; // carries a guessed role
            other.role = new Role { roleID = RoleID.Dryade };     // does not

            GameObject powerGo = new GameObject("VisionOfTheImpossible");
            var power = powerGo.AddComponent<PVisionOfTheImpossible>();
            powerGo.AddComponent<NetworkObject>().Spawn();
            power.ownerClientId.Value = _networkManager.LocalClientId;
            yield return NetworkTestHelper.WaitUntilSpawnedOrTimeout(power);

            bool received = false;
            ulong sender = 0;
            string body = null;
            ChatManager.instance.onChatMessageReceived += _m => { received = true; sender = _m.senderClientId; body = _m.message.ToString(); };

            var _guessedCharacterIds = new[] { match.ownerClientId.Value, other.ownerClientId.Value };
            var _guessedRoles = new[] { new Role { roleID = RoleID.Omniscient } };
            ReflectionHelper.InvokePrivateMethod(power, "OnVisionGuessServerRpc", _guessedCharacterIds, _guessedRoles);
            yield return null;

            Assert.IsTrue(received, "Vision should announce the guess result to the owner.");
            Assert.AreEqual(ChatManager.SERVER_CLIENT_ID, sender, "The announcement should come from the server sender id.");
            Assert.IsTrue(RoleTargetSystem.instance.GetAllTargetingDataForTarget(match.ownerClientId.Value).Count > 0,
                "Vision should target the matched character.");
            StringAssert.Contains("est l'un de ces personnages", body,
                "A matched guess should announce the 'found' line.");
        }
    }
}
