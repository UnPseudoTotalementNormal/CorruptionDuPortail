using System.Collections;
using Characters;
using Characters.Powers;
using Characters.Powers.Target;
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

namespace Tests.PlayMode
{
    public class CorruptionTests
    {
        private GameObject _networkManagerGo;
        private NetworkManager _networkManager;
        private GameObject _gameManagerGo;
        private GameManager _gameManager;
        private GameObject _characterManagerGo;
        private CharacterManager _characterManager;
        private GameObject _revealerGo;
        private GameInfoRevealer _revealer;
        private GameObject _compositionRootGo;

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
            
            _dummyCharPrefab = new GameObject("CharacterPrefab_Corruption");
            _dummyCharPrefab.AddComponent<NetworkObject>();
            _dummyCharPrefab.AddComponent<Character>();
            _networkManager.NetworkConfig.Prefabs.Add(new NetworkPrefab { Prefab = _dummyCharPrefab });

            Assert.IsTrue(_networkManager.StartHost(), "NGO StartHost() failed — server did not start.");

            // 1. Setup GameManager
            _gameManagerGo = new GameObject("GameManager");
            _gameManagerGo.AddComponent<NetworkObject>();
            _gameManager = _gameManagerGo.AddComponent<GameManager>();
            _gameManager.ignoreGameLoop = true;
            var dummyState = ScriptableObject.CreateInstance<DummyGameState>();
            _gameManager.gameStates.Add(dummyState, new GameStateSettings());
            _gameManager.GetComponent<NetworkObject>().Spawn();

            // 2. Setup CharacterManager
            _characterManagerGo = new GameObject("CharacterManager");
            _characterManagerGo.AddComponent<NetworkObject>();
            _characterManager = _characterManagerGo.AddComponent<CharacterManager>();
            ReflectionHelper.SetPrivateField(_gameManager, "characterManager", _characterManager);
            _characterManager.GetComponent<NetworkObject>().Spawn();
            
            GameObject charactersParent = new GameObject("CharactersParent");
            charactersParent.AddComponent<NetworkObject>().Spawn();
            ReflectionHelper.SetPrivateField(_characterManager, "_charactersParent", charactersParent.transform);
            ReflectionHelper.SetPrivateField(_characterManager, "_characterPrefab", _dummyCharPrefab.GetComponent<NetworkObject>());

            // 3. Setup Revealer
            _revealerGo = new GameObject("GameInfoRevealer");
            _revealerGo.AddComponent<NetworkObject>();
            _revealer = _revealerGo.AddComponent<GameInfoRevealer>();
            _revealer.GetComponent<NetworkObject>().Spawn();
            ReflectionHelper.SetPrivateField(_gameManager, "gameInfoRevealer", _revealer);
            ReflectionHelper.SetPrivateField(_revealer, "characterManager", _characterManager);
            // Story 8.3: GameInfoRevealer's game-loop reads now resolve through an injected [SerializeField]
            // gameManager (lane A), so the harness wires it like the production scene does.
            ReflectionHelper.SetPrivateField(_revealer, "gameManager", _gameManager);

            // Story 7.5: powers spawned in the test body resolve the revealer via
            // CompositionRoot.For(nm).GameInfoRevealer (the GameManager pass-through is gone), so the
            // harness registers a CompositionRoot for this NM.
            _compositionRootGo = NetworkTestHelper.RegisterCompositionRoot(_gameManager, _characterManager, _revealer);

            // 4. Setup RTS
            GameObject rtsGo = new GameObject("RoleTargetSystem");
            rtsGo.AddComponent<RoleTargetSystem>();
            rtsGo.AddComponent<NetworkObject>().Spawn();

            // 5. Setup Board
            GameObject boardGo = new GameObject("BoardManager");
            var _boardManager = boardGo.AddComponent<BoardManager>();
            ReflectionHelper.SetPrivateField(_boardManager, "characterManager", _characterManager);

            // 6. Setup Audio
            GameObject audioGo = new GameObject("AudioManager");
            audioGo.AddComponent<GameAudioManager>();

            // 7. Setup Chat
            GameObject chatGo = new GameObject("ChatManager");
            chatGo.AddComponent<ChatManager>();
            chatGo.AddComponent<NetworkObject>().Spawn();

            // 8. Setup Lobby
            GameObject lobbyGo = new GameObject("LobbyPlayerInfoHolder");
            lobbyGo.AddComponent<LobbyPlayerInfoHolder>();
            lobbyGo.AddComponent<NetworkObject>().Spawn();

            // 9. Setup Chaining (powers-POCO v2 wiring goldens that chain, e.g. ChainedByShadows)
            GameObject chainingGo = new GameObject("ChainingManager");
            chainingGo.AddComponent<ChainingManager>();
            chainingGo.AddComponent<NetworkObject>().Spawn();

            yield return NetworkTestHelper.WaitUntilAllSpawnedOrTimeout(_gameManager, _characterManager, _revealer);
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

            Object.Destroy(_gameManagerGo);
            Object.Destroy(_characterManagerGo);
            Object.Destroy(_revealerGo);
            Object.Destroy(_compositionRootGo);
            Object.Destroy(GameObject.Find("RoleTargetSystem"));
            Object.Destroy(GameObject.Find("BoardManager"));
            Object.Destroy(GameObject.Find("ChatManager"));
            Object.Destroy(GameObject.Find("LobbyPlayerInfoHolder"));
            Object.Destroy(GameObject.Find("ChainingManager"));
            Object.Destroy(_networkManagerGo);
            Object.Destroy(_dummyCharPrefab);
            Object.Destroy(GameObject.Find("AudioManager"));
            yield return null;
        }

        [UnityTest]
        public IEnumerator PAutoCorruption_CorruptsOwnerAtStart()
        {
            Character owner = _characterManager.AddNewCharacter(_networkManager.LocalClientId);
            yield return NetworkTestHelper.WaitUntilSpawnedOrTimeout(owner);
            
            Assert.IsFalse(owner.isCorrupted.Value);

            GameObject powerGo = new GameObject("AutoCorruption");
            var power = powerGo.AddComponent<PAutoCorruption>();
            power.ownerClientId.Value = _networkManager.LocalClientId;
            powerGo.AddComponent<NetworkObject>().Spawn();

            yield return NetworkTestHelper.WaitUntilSpawnedOrTimeout(power);

            power.OnGameStartedServer();
            yield return null;

            Assert.IsTrue(owner.isCorrupted.Value, "PAutoCorruption should corrupt its owner");
        }

        // Powers-POCO v2 wiring golden: PCorruptionParanoia now delegates OnGameStartedServer to
        // CorruptionParanoiaDecision → RevealInfo → RevealInfoExecutor.SendRevealLevelRpc. This proves the
        // in-place decision→dispatch path lights up the same personal-corruption reveal as the old inline call.
        [UnityTest]
        public IEnumerator PCorruptionParanoia_RevealsOwnCorruptionAtStart()
        {
            Character owner = _characterManager.AddNewCharacter(_networkManager.LocalClientId);
            yield return NetworkTestHelper.WaitUntilSpawnedOrTimeout(owner);

            Assert.AreEqual(RevealLevel.False,
                _revealer.GetCharacterInfo(owner.ownerClientId.Value).isCorruptRevealed);

            GameObject powerGo = new GameObject("CorruptionParanoia");
            var power = powerGo.AddComponent<PCorruptionParanoia>();
            power.ownerClientId.Value = _networkManager.LocalClientId;
            powerGo.AddComponent<NetworkObject>().Spawn();

            yield return NetworkTestHelper.WaitUntilSpawnedOrTimeout(power);

            power.OnGameStartedServer();
            yield return null;

            Assert.AreEqual(RevealLevel.Personal,
                _revealer.GetCharacterInfo(owner.ownerClientId.Value).isCorruptRevealed,
                "PCorruptionParanoia should reveal the owner's own corruption (Personal) at game start.");
        }

        [UnityTest]
        public IEnumerator PCorruptingMark_CorruptsTarget()
        {
            Character owner = _characterManager.AddNewCharacter(_networkManager.LocalClientId);
            Character target = _characterManager.AddNewCharacter(999);
            yield return NetworkTestHelper.WaitUntilAllSpawnedOrTimeout(owner, target);

            GameObject powerGo = new GameObject("CorruptingMark");
            var power = powerGo.AddComponent<PCorruptingMark>();
            power.ownerClientId.Value = _networkManager.LocalClientId;
            powerGo.AddComponent<NetworkObject>().Spawn();
            
            yield return NetworkTestHelper.WaitUntilSpawnedOrTimeout(power);

            ReflectionHelper.InvokePrivateMethod(power, "OnCardClickedRpc", target.ownerClientId.Value);
            yield return null;

            Assert.IsTrue(target.isCorrupted.Value, "PCorruptingMark should corrupt the target");
        }

        // Powers-POCO v2 wiring golden: PChainedByTheShadows delegates its char+role RPC to
        // ChainedByShadowsDecision → NewTargeting + (role matches) reveal + (chosen) AddToChain. The picked
        // role's owner is the secondary slot the roster compares against (here the target itself).
        [UnityTest]
        public IEnumerator PChainedByShadows_RevealsAndChainsMatchingChosenTarget()
        {
            Character owner = _characterManager.AddNewCharacter(_networkManager.LocalClientId);
            Character target = _characterManager.AddNewCharacter(4242);
            yield return NetworkTestHelper.WaitUntilAllSpawnedOrTimeout(owner, target);

            target.role = new Role { factionType = FactionType.chosen, roleName = "Chosen-Test" };
            var compareRole = new Role { roleName = "Chosen-Test", ownerClientId = target.ownerClientId.Value };

            GameObject powerGo = new GameObject("ChainedByShadows");
            var power = powerGo.AddComponent<PChainedByTheShadows>();
            power.ownerClientId.Value = _networkManager.LocalClientId;
            powerGo.AddComponent<NetworkObject>().Spawn();
            yield return NetworkTestHelper.WaitUntilSpawnedOrTimeout(power);

            ReflectionHelper.InvokePrivateMethod(power, "TryCorruptCharacterServerRpc",
                target.ownerClientId.Value, compareRole);
            yield return null;

            Assert.IsTrue(ChainingManager.instance.chainingPlayers.Contains(target.ownerClientId.Value),
                "ChainedByShadows should chain a matching-role chosen target.");
            Assert.AreEqual(RevealLevel.Personal,
                _revealer.GetCharacterInfo(target.ownerClientId.Value, owner.ownerClientId.Value).isRoleRevealed,
                "ChainedByShadows should reveal the matching target's role to the owner.");
        }

        // Powers-POCO v2 wiring golden: PHighPriorityBounty delegates its server RPC to
        // HighPriorityBountyDecision → self-target, then (robot branch) eliminate + broadcast + public-reveal.
        [UnityTest]
        public IEnumerator PHighPriorityBounty_EliminatesAndRevealsRobotTarget()
        {
            Character owner = _characterManager.AddNewCharacter(_networkManager.LocalClientId);
            Character target = _characterManager.AddNewCharacter(8888);
            yield return NetworkTestHelper.WaitUntilAllSpawnedOrTimeout(owner, target);

            target.role = new Role { roleID = RoleID.Robot };
            owner.role = new Role { roleName = "Hunter" };

            GameObject powerGo = new GameObject("HighPriorityBounty");
            var power = powerGo.AddComponent<PHighPriorityBounty>();
            power.ownerClientId.Value = _networkManager.LocalClientId;
            powerGo.AddComponent<NetworkObject>().Spawn();
            yield return NetworkTestHelper.WaitUntilSpawnedOrTimeout(power);

            Assert.IsFalse(target.isEliminated.Value);

            ReflectionHelper.InvokePrivateMethod(power, "OnCardClickedRpc", target.ownerClientId.Value);
            yield return null;

            Assert.IsTrue(target.isEliminated.Value, "HighPriorityBounty should eliminate a robot target.");
            Assert.AreEqual(RevealLevel.Public,
                _revealer.GetCharacterInfo(target.ownerClientId.Value, owner.ownerClientId.Value).isRoleRevealed,
                "HighPriorityBounty should publicly reveal a robot target's role.");
        }
[UnityTest]
public IEnumerator PBlessing_MakesTargetUntargetableForCorruption()
{
    Character owner = _characterManager.AddNewCharacter(_networkManager.LocalClientId);
    Character target = _characterManager.AddNewCharacter(888);
    yield return NetworkTestHelper.WaitUntilAllSpawnedOrTimeout(owner, target);

    GameObject powerGo = new GameObject("Blessing");
    var blessingPower = powerGo.AddComponent<PBlessing>();
    powerGo.AddComponent<NetworkObject>().Spawn();

    GameObject corruptGo = new GameObject("CorruptingMark");
    var corruptPower = corruptGo.AddComponent<PCorruptingMark>();
    // Simulate that CorruptingMark DOES NOT include Blessed targets
    corruptPower.targetIncludeFlags = TargetIncludeFlags.Anomaly | TargetIncludeFlags.Chosen | TargetIncludeFlags.Marginal; 
    corruptGo.AddComponent<NetworkObject>().Spawn();

    yield return NetworkTestHelper.WaitUntilAllSpawnedOrTimeout(blessingPower, corruptPower);

    // 1. Target is NOT blessed -> Should be targetable
    Assert.IsTrue(corruptPower.CheckIsTargetValid(target.ownerClientId.Value, TargetUtils.TargetType.Character), "Target should be valid before blessing");

    // 2. Bless the target
    target.isBlessed.Value = true;
    yield return null;

    // 3. Target IS blessed -> Should be UN-targetable
    Assert.IsFalse(corruptPower.CheckIsTargetValid(target.ownerClientId.Value, TargetUtils.TargetType.Character), "Blessed target should NOT be valid for corruption targeting");
}

        [UnityTest]
        public IEnumerator SetRevealLevelRpc_PersonalReveal_FiresUiRefresh_ForNonHostViewer()
        {
            // Regression: the reveal RPC used to pass the 0 storage sentinel as the observer id, so on a
            // non-host client (GetLocalClientId() != 0) the UI-refresh guard was silently skipped — the
            // role data was set but the card never re-rendered. Simulate a non-host viewer via the debug
            // possession seam and assert the UI-refresh event fires.
            _characterManager.SetPossessedIdentity(1);
            try
            {
                Character target = _characterManager.AddNewCharacter(777);
                yield return NetworkTestHelper.WaitUntilSpawnedOrTimeout(target);

                bool _uiRefreshFired = false;
                _revealer.onCharacterInfoRevealedChanged += () => _uiRefreshFired = true;

                _revealer.SetRevealLevelRpc(target.ownerClientId.Value,
                    nameof(CharacterInfoReveal.isRoleRevealed), RevealLevel.Personal, true);
                yield return null;

                Assert.AreEqual(RevealLevel.Personal,
                    _revealer.GetCharacterInfo(target.ownerClientId.Value).isRoleRevealed,
                    "Reveal data must be written.");
                Assert.IsTrue(_uiRefreshFired,
                    "onCharacterInfoRevealedChanged must fire so the non-host viewer's card re-renders.");
            }
            finally
            {
                _characterManager.SetPossessedIdentity(null);
            }
        }
    }
}
