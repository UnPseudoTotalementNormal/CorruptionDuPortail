using System.Collections;
using System.Collections.Generic;
using Characters;
using GameLogic;
using GameLogic.GameSettings;
using GameLogic.GameStates;
using NUnit.Framework;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.TestTools;

namespace Tests.PlayMode
{
    /// <summary>
    /// Server-side ready-to-start logic (feat/lobby-ready-system): the game auto-starts only when every census
    /// player is ready AND the composition gate passes; the dev "Démarrage forcé" skips the all-ready condition
    /// but not the composition gate; simulated bots are auto-ready. Uses a host NGO harness with a real
    /// LobbyPlayerInfoHolder + a registered CompositionRoot so LobbyState.OnStartStateServer wires the census
    /// subscription for real (the auto-start then fires from a ready change, not a hand-invoked method).
    /// </summary>
    [Category("LobbyReady")]
    public class LobbyReadyAutoStartTests
    {
        private GameObject _networkManagerGo;
        private NetworkManager _networkManager;
        private GameObject _gameManagerGo;
        private GameManager _gameManager;
        private GameObject _characterManagerGo;
        private CharacterManager _characterManager;
        private GameObject _dummyCharPrefab;
        private GameObject _compositionRootGo;
        private GameObject _revealerGo;
        private GameObject _lobbyInfoGo;
        private Network.LobbyPlayerInfoHolder _lobbyInfo;
        private RoleAttributionState _roleAttribution;
        private CompositionRuleSet _rules;
        private LobbyState _lobbyState;

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

            _dummyCharPrefab = new GameObject("CharacterPrefab_LobbyReady");
            _dummyCharPrefab.AddComponent<NetworkObject>();
            _dummyCharPrefab.AddComponent<Character>();
            _networkManager.NetworkConfig.Prefabs.Add(new NetworkPrefab { Prefab = _dummyCharPrefab });

            Assert.IsTrue(_networkManager.StartHost(), "NGO StartHost() failed — server did not start.");

            _gameManagerGo = new GameObject("GameManager");
            _gameManagerGo.AddComponent<NetworkObject>();
            _gameManager = _gameManagerGo.AddComponent<GameManager>();
            _gameManager.ignoreGameLoop = true;
            // index 0 = current state, index 1 = where a successful start advances to (a harmless Dummy).
            _gameManager.gameStates.Add(ScriptableObject.CreateInstance<DummyGameState>(), new GameStateSettings());
            _gameManager.gameStates.Add(ScriptableObject.CreateInstance<DummyGameState>(), new GameStateSettings());
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

            // Valid composition: anomaly max2/forced0 + chosen max2/forced0 with the default rule set (anomaly≥1,
            // chosen≥1) is valid for 2 players (Σmax4≥2, guaranteed 2≤2, both factions covered).
            _rules = ScriptableObject.CreateInstance<CompositionRuleSet>();
            _roleAttribution = ScriptableObject.CreateInstance<RoleAttributionState>();
            _roleAttribution.gameManager = _gameManager;
            _roleAttribution.characterManager = _characterManager;
            ReflectionHelper.SetPrivateField(_roleAttribution, "_compositionRules", _rules);
            AddRole(_roleAttribution, FactionType.anomaly, RoleID.Robot, 2, 0);
            AddRole(_roleAttribution, FactionType.chosen, RoleID.Oracle, 2, 0);
            _gameManager.gameStates.Add(_roleAttribution, new GameStateSettings());

            _lobbyState = ScriptableObject.CreateInstance<LobbyState>();
            _lobbyState.gameManager = _gameManager;
            _lobbyState.characterManager = _characterManager;
            _gameManager.gameStates.Add(_lobbyState, new GameStateSettings());

            // CompositionRoot.Awake asserts a wired GameInfoRevealer, so the harness provides one (mirrors the
            // VisionPowerTests setup). Not used by the ready path — just satisfies the root's non-null contract.
            _revealerGo = new GameObject("GameInfoRevealer");
            _revealerGo.AddComponent<NetworkObject>();
            var _revealer = _revealerGo.AddComponent<GameInfoRevealer>();
            _revealer.GetComponent<NetworkObject>().Spawn();
            ReflectionHelper.SetPrivateField(_gameManager, "gameInfoRevealer", _revealer);
            ReflectionHelper.SetPrivateField(_revealer, "characterManager", _characterManager);
            ReflectionHelper.SetPrivateField(_revealer, "gameManager", _gameManager);

            // A registered CompositionRoot so the holder resolves its CharacterManager on spawn and LobbyState
            // resolves the holder (CompositionRoot.LobbyPlayerInfoHolder => the singleton set in Awake).
            _compositionRootGo = NetworkTestHelper.RegisterCompositionRoot(_gameManager, _characterManager, _revealer);

            _lobbyInfoGo = new GameObject("LobbyPlayerInfoHolder");
            _lobbyInfoGo.AddComponent<NetworkObject>();
            _lobbyInfo = _lobbyInfoGo.AddComponent<Network.LobbyPlayerInfoHolder>();
            _lobbyInfo.GetComponent<NetworkObject>().Spawn();

            yield return NetworkTestHelper.WaitUntilAllSpawnedOrTimeout(_gameManager, _characterManager, _lobbyInfo);
            // The holder auto-adds the host's census entry on spawn (via its connect flow).
            yield return NetworkTestHelper.WaitUntilOrTimeout(
                () => _lobbyInfo.playerInfos.Count >= 1, 5f, "Holder never added the host census entry.");
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (_networkManager != null && _networkManager.IsListening) _networkManager.Shutdown();
            yield return NetworkTestHelper.WaitUntilOrTimeout(
                () => _networkManager == null || !_networkManager.IsListening, 5f, "NGO did not stop listening.");

            ReflectionHelper.SetPrivateField(typeof(GameManager), "instance", null);
            ReflectionHelper.SetPrivateField(typeof(CharacterManager), "instance", null);
            CompositionRoot.ResetSessionStatics();

            if (_roleAttribution != null)
            {
                foreach (RoleDataObject _key in new List<RoleDataObject>(_roleAttribution.roleAttributionDictionary.Keys))
                    if (_key != null) Object.Destroy(_key);
                Object.Destroy(_roleAttribution);
            }
            if (_rules != null) Object.Destroy(_rules);
            if (_compositionRootGo != null) Object.Destroy(_compositionRootGo);
            if (_revealerGo != null) Object.Destroy(_revealerGo);
            Object.Destroy(_gameManagerGo);
            Object.Destroy(_characterManagerGo);
            Object.Destroy(_lobbyInfoGo);
            Object.Destroy(_networkManagerGo);
            Object.Destroy(_dummyCharPrefab);
            yield return null;
        }

        private static void AddRole(RoleAttributionState _state, FactionType _faction, RoleID _id, int max, int forced)
        {
            RoleDataObject _roleData = ScriptableObject.CreateInstance<RoleDataObject>();
            _roleData.role = new Role { roleID = _id, roleName = _id.ToString(), factionType = _faction };
            _roleData.powers = new List<Characters.Powers.Power>();
            _state.roleAttributionDictionary.Add(_roleData, new RoleAttributionSetting { max = max, forced = forced });
        }

        // Adds n game participants (clientIds 1..n): a spawned Character AND a ready census entry each, so the
        // census owners match the characters — exactly what AllParticipantsReady (the auto-start gate) checks.
        private IEnumerator AddParticipants(int n)
        {
            var chars = new List<Character>();
            for (ulong id = 1; id <= (ulong)n; id++)
            {
                chars.Add(_characterManager.AddNewCharacter(id));
                _lobbyInfo.AddDebugPlayer(id, "P" + id); // census entry, auto-ready
            }
            yield return NetworkTestHelper.WaitUntilAllSpawnedOrTimeout(chars.ToArray());
        }

        // Drives the server tick (the auto-start poll) until the condition holds or it times out.
        private IEnumerator PumpUntil(System.Func<bool> condition, string failure)
        {
            float _elapsed = 0f;
            while (!condition())
            {
                _lobbyState.StateUpdateServer();
                if (_elapsed >= 5f) { Assert.Fail(failure); yield break; }
                _elapsed += Time.deltaTime;
                yield return null;
            }
        }

        // Pumps a bounded number of ticks and asserts the state NEVER advanced (a real negative check, not a
        // two-frame window a deferred advance could slip past).
        private IEnumerator PumpAndAssertNoAdvance(int ticks, string failure)
        {
            for (int i = 0; i < ticks; i++)
            {
                _lobbyState.StateUpdateServer();
                Assert.AreEqual(0, _gameManager.currentGameStateIndex.Value, failure);
                yield return null;
            }
        }

        private ulong HostId => _networkManager.LocalClient.ClientId;

        [UnityTest]
        public IEnumerator AutoStart_AllParticipantsReadyAndValidComposition_Advances()
        {
            yield return AddParticipants(2);   // 2 characters + 2 ready census entries; composition valid for 2
            _lobbyState.OnStartStateServer();

            yield return PumpUntil(() => _gameManager.currentGameStateIndex.Value == 1,
                "All participants ready + valid composition must auto-advance the state.");
            _lobbyState.OnEndStateServer();
        }

        [UnityTest]
        public IEnumerator AutoStart_OneNotReady_DoesNotAdvance()
        {
            yield return AddParticipants(2);
            _lobbyInfo.SetReadyServer(1, false); // participant 1 un-readied → blocks
            _lobbyState.OnStartStateServer();

            yield return PumpAndAssertNoAdvance(5, "A not-ready participant must block auto-start.");
            _lobbyState.OnEndStateServer();
        }

        [UnityTest]
        public IEnumerator AutoStart_AllReadyButInvalidComposition_DoesNotAdvance()
        {
            yield return AddParticipants(5); // all ready, but 5 players > Σmax(4) → composition invalid
            _lobbyState.OnStartStateServer();

            // The auto-start poll checks the composition SILENTLY (no warning spam); assert it never starts.
            yield return PumpAndAssertNoAdvance(5, "All-ready but invalid composition must NOT auto-start.");
            _lobbyState.OnEndStateServer();
        }

        [UnityTest]
        public IEnumerator ForceStart_SkipsAllReady_WithValidComposition_Advances()
        {
            yield return AddParticipants(2);
            _lobbyInfo.SetReadyServer(1, false); // not all ready...
            _lobbyInfo.SetReadyServer(2, false);
            _lobbyState.OnStartStateServer();

            Assert.IsFalse(_lobbyInfo.AllReady(), "precondition: not all-ready");
            _lobbyState.ForceStart();            // ...but force-start skips the all-ready condition

            yield return PumpUntil(() => _gameManager.currentGameStateIndex.Value == 1,
                "Force-start with a valid composition must advance even when nobody is ready.");
            _lobbyState.OnEndStateServer();
        }

        [UnityTest]
        public IEnumerator ForceStart_InvalidComposition_DoesNotAdvance()
        {
            yield return AddParticipants(5); // 5 players > Σmax(4) → coverage fails → composition invalid
            _lobbyState.OnStartStateServer();

            LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex("invalid composition"));
            _lobbyState.ForceStart();
            yield return null;

            Assert.AreEqual(0, _gameManager.currentGameStateIndex.Value,
                "Force-start must still enforce the composition gate.");
            _lobbyState.OnEndStateServer();
        }

        [UnityTest]
        public IEnumerator SimulatedBot_IsAutoReady()
        {
            _lobbyInfo.AddDebugPlayer(100, "Bot"); // clientId >= 100 = simulated bot
            yield return NetworkTestHelper.WaitUntilOrTimeout(
                () => _lobbyInfo.GetPlayerInfo(100).playerClientId == 100, 5f, "Bot entry not added.");

            Assert.IsTrue(_lobbyInfo.GetPlayerInfo(100).isReady, "A simulated bot must be auto-ready.");
        }

        [UnityTest]
        public IEnumerator SetReadyServer_FlipsFlag_AndTallyReflectsIt()
        {
            _lobbyInfo.SetReadyServer(HostId, true);
            yield return null;

            Assert.IsTrue(_lobbyInfo.GetPlayerInfo(HostId).isReady, "SetReadyServer must set the entry's flag.");
            Assert.AreEqual(1, _lobbyInfo.ReadyCount());
            Assert.IsTrue(_lobbyInfo.AllReady(), "Single ready census entry ⇒ all-ready.");

            _lobbyInfo.SetReadyServer(HostId, false);
            yield return null;
            Assert.IsFalse(_lobbyInfo.AllReady(), "Un-readying the only player clears all-ready.");
            Assert.AreEqual(0, _lobbyInfo.ReadyCount());
        }
    }
}
