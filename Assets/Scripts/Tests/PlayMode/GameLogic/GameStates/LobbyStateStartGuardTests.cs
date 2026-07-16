using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Text.RegularExpressions;
using Characters;
using Characters.Powers;
using GameLogic;
using GameLogic.GameStates;
using NUnit.Framework;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.TestTools;

namespace Tests.PlayMode
{
    /// <summary>
    /// Server-side transition guard in <see cref="LobbyState.OnStartGameButtonPressed"/> (added coverage): when the
    /// connected player count exceeds the total roles to attribute, the method logs a warning and returns WITHOUT
    /// advancing the loop. Pinned via LogAssert — the expected warning proves the early-return branch ran (the
    /// success path logs nothing and would instead call Loop.NextGameState). The success path needs full loop
    /// machinery and is already covered by the GameLoopMachine EditMode arithmetic + the transition goldens.
    ///
    /// gameSettingsManager is left null, so LobbyState reads the total from the authored RoleAttributionState
    /// fallback (LobbyState.cs:37-40) — the simplest standalone wiring.
    /// </summary>
    [Category("LobbyState")]
    public class LobbyStateStartGuardTests
    {
        private GameObject _networkManagerGo;
        private NetworkManager _networkManager;
        private GameObject _gameManagerGo;
        private GameManager _gameManager;
        private GameObject _characterManagerGo;
        private CharacterManager _characterManager;
        private GameObject _dummyCharPrefab;
        private RoleAttributionState _roleAttribution;
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

            _dummyCharPrefab = new GameObject("CharacterPrefab_LobbyGuard");
            _dummyCharPrefab.AddComponent<NetworkObject>();
            _dummyCharPrefab.AddComponent<Character>();
            _networkManager.NetworkConfig.Prefabs.Add(new NetworkPrefab { Prefab = _dummyCharPrefab });

            Assert.IsTrue(_networkManager.StartHost(), "NGO StartHost() failed — server did not start.");

            _gameManagerGo = new GameObject("GameManager");
            _gameManagerGo.AddComponent<NetworkObject>();
            _gameManager = _gameManagerGo.AddComponent<GameManager>();
            _gameManager.ignoreGameLoop = true;
            // Two harmless DummyGameStates present BEFORE Spawn so SetupGameStates clones both: index 0 is
            // the current state and index 1 is where a successful start advances to (kept a Dummy so the
            // "allowed" path does not enter the heavy RoleAttribution distribution). ignoreGameLoop => the
            // GameLoopMachine does a plain +1, so 0 -> 1 is deterministic. RoleAttribution/Lobby are added
            // AFTER Spawn (below), landing at indices 2/3 — RoleAttribution only exists for the fallback lookup.
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

            // Authored fallback (gameSettingsManager stays null so LobbyState reads these): mandatory
            // (non-fakeable) roles = Robot(2) → MIN floor 2; total roles = Robot(2)+Oracle(1)=3 → MAX ceiling 3.
            _roleAttribution = ScriptableObject.CreateInstance<RoleAttributionState>();
            AddRole(_roleAttribution, RoleID.Robot, count: 2, canBeFake: false);
            AddRole(_roleAttribution, RoleID.Oracle, count: 1, canBeFake: true);
            _gameManager.gameStates.Add(_roleAttribution, new GameStateSettings());

            _lobbyState = ScriptableObject.CreateInstance<LobbyState>();
            _lobbyState.gameManager = _gameManager;
            _lobbyState.characterManager = _characterManager;
            // gameSettingsManager intentionally left null → LobbyState uses the RoleAttributionState fallback.
            _gameManager.gameStates.Add(_lobbyState, new GameStateSettings());

            yield return NetworkTestHelper.WaitUntilAllSpawnedOrTimeout(_gameManager, _characterManager);
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (_networkManager != null && _networkManager.IsListening) _networkManager.Shutdown();
            yield return NetworkTestHelper.WaitUntilOrTimeout(
                () => _networkManager == null || !_networkManager.IsListening, 5f,
                "NGO did not stop listening within 5s after Shutdown().");

            ReflectionHelper.SetPrivateField(typeof(GameManager), "instance", null);
            ReflectionHelper.SetPrivateField(typeof(CharacterManager), "instance", null);

            if (_roleAttribution != null)
            {
                foreach (RoleDataObject _key in new List<RoleDataObject>(_roleAttribution.roleAttributionDictionary.Keys))
                {
                    if (_key != null) Object.Destroy(_key);
                }
                Object.Destroy(_roleAttribution);
            }
            Object.Destroy(_gameManagerGo);
            Object.Destroy(_characterManagerGo);
            Object.Destroy(_networkManagerGo);
            Object.Destroy(_dummyCharPrefab);
            yield return null;
        }

        private static void AddRole(RoleAttributionState _state, RoleID _id, int count, bool canBeFake)
        {
            RoleDataObject _roleData = ScriptableObject.CreateInstance<RoleDataObject>();
            _roleData.role = new Role { roleID = _id, roleName = _id.ToString() };
            _roleData.powers = new List<Power>();
            // canBeFake=false (mandatory) ⇒ forced == max; canBeFake=true ⇒ forced == 0 (whole pool fakeable).
            _state.roleAttributionDictionary.Add(_roleData, new RoleAttributionSetting { max = count, forced = canBeFake ? 0 : count });
        }

        [UnityTest]
        public IEnumerator OnStartGameButtonPressed_MorePlayersThanRoles_WarnsAndDoesNotAdvance()
        {
            // 4 players, only 3 total roles to attribute → the coverage rule (Σmax ≥ players) must fail. The gate
            // now delegates to CompositionValidator; the warning carries the "Pool trop petit" reason.
            Character c1 = _characterManager.AddNewCharacter(1);
            Character c2 = _characterManager.AddNewCharacter(2);
            Character c3 = _characterManager.AddNewCharacter(3);
            Character c4 = _characterManager.AddNewCharacter(4);
            yield return NetworkTestHelper.WaitUntilAllSpawnedOrTimeout(c1, c2, c3, c4);

            LogAssert.Expect(LogType.Warning, new Regex("invalid composition: Pool trop petit"));
            _lobbyState.OnStartGameButtonPressed();

            // The warning expectation alone proves the early-return branch ran; an unmet expectation fails the test.
            Assert.AreEqual(0, _gameManager.currentGameStateIndex.Value, "Blocked start must not advance the state.");
            yield return null;
        }

        [UnityTest]
        public IEnumerator OnStartGameButtonPressed_FewerPlayersThanMandatoryRoles_WarnsAndDoesNotAdvance()
        {
            // [LEAVE][PHASE 4] AC1: 1 player < 2 guaranteed reals (Robot forced==2) → the guaranteed-fit rule must
            // fail (it subsumes the old Σforced ≤ players floor). Below the max (1 <= 3), so coverage stays silent.
            Character c1 = _characterManager.AddNewCharacter(1);
            yield return NetworkTestHelper.WaitUntilAllSpawnedOrTimeout(c1);

            LogAssert.Expect(LogType.Warning, new Regex("invalid composition: Trop de r"));
            _lobbyState.OnStartGameButtonPressed();

            Assert.AreEqual(0, _gameManager.currentGameStateIndex.Value, "Below-minimum start must not advance the state.");
            yield return null;
        }

        [UnityTest]
        public IEnumerator OnStartGameButtonPressed_PlayersBetweenMinAndMax_AdvancesTheState()
        {
            // [LEAVE][PHASE 4] AC2: 2 players is >= min(2) and <= max(3) → BOTH guards pass and the start
            // advances the loop (0 -> 1, a harmless DummyGameState). Proves no regression from the new min gate.
            Character c1 = _characterManager.AddNewCharacter(1);
            Character c2 = _characterManager.AddNewCharacter(2);
            yield return NetworkTestHelper.WaitUntilAllSpawnedOrTimeout(c1, c2);

            _lobbyState.OnStartGameButtonPressed();

            Assert.AreEqual(1, _gameManager.currentGameStateIndex.Value, "A valid start (min<=players<=max) must advance the state.");
            // Let the state-transition RPCs flush; an unexpected error here would fail the test.
            yield return null;
            yield return null;
        }

        [UnityTest]
        public IEnumerator ResetSessionStatics_ClearsInstanceAndRegistry_NoStaleSurvivor()
        {
            // [LEAVE][PHASE 4] AC4: the explicit session reset used by the return-to-menu paths must leave no
            // stale static behind. The fixture spawned a GameManager → it claimed the static instance + the
            // per-NM registry entry (verified as a precondition).
            Assert.AreSame(_gameManager, GameManager.instance, "Precondition: fixture GameManager owns the static instance.");
            Assert.AreSame(_gameManager, GameManager.For(_networkManager), "Precondition: fixture GameManager is registered for its NM.");

            GameManager.ResetSessionStatics();
            CompositionRoot.ResetSessionStatics(); // idempotent + null-safe even with no root registered

            Assert.IsNull(GameManager.instance, "instance must be cleared — no stale singleton into the next session.");
            Assert.IsNull(GameManager.For(_networkManager), "registry entry must be cleared — no stale per-NM manager.");
            yield return null;
        }

        // ─────────────────── [LEAVE][PHASE 5] leave-pipeline branches (tasks 1 & 11) ───────────────────
        // These reuse this fixture because it is the only host-only harness that wires a REAL LobbyState as a
        // seeded state (so GameManager.HandlePlayerLeft's `is LobbyState` phase signal resolves to the lobby
        // branch) alongside a live GameManager + CharacterManager. HandlePlayerLeft is the single private
        // OnClientDisconnectCallback reaction; we invoke it directly (the callback wiring itself is exercised by
        // PlayerLeaveMidGameTests over the loopback substrate).

        [UnityTest]
        public IEnumerator HandlePlayerLeft_LobbyLeave_RemovesCharacter_RecordsDeparted_NoError()
        {
            // [LEAVE][PHASE 5] Task 1 — a client leaving while LobbyState is the current state must take the
            // pipeline's LOBBY branch: the character is REMOVED (not chained), exactly once, with no error. The
            // ancillary despawns (avatar via AvatarManager, playerInfos via LobbyPlayerInfoHolder) keep their OWN
            // server-gated subscriptions and are covered by their own tests (e.g. AvatarSpawnTests) — this fixture
            // wires neither, so this asserts the branch's own effect (RemoveCharacter + departed bookkeeping).
            const ulong leaverId = 5;
            Character leaver = _characterManager.AddNewCharacter(leaverId);
            Assert.IsNotNull(leaver, "AddNewCharacter returned null for the lobby leaver.");
            yield return NetworkTestHelper.WaitUntilAllSpawnedOrTimeout(leaver);
            yield return NetworkTestHelper.WaitUntilOrTimeout(
                () => _characterManager.GetCharacter(leaverId, false) != null, 5f,
                "CharacterManager never projected the lobby leaver before the leave.");

            // Make the real LobbyState the current state so HandlePlayerLeft's `is LobbyState` check is true.
            int lobbyIndex = _gameManager.GetGameStateIndex(_lobbyState);
            Assert.GreaterOrEqual(lobbyIndex, 0, "Seeded LobbyState index not found.");
            _gameManager.currentGameStateIndex.Value = lobbyIndex;
            Assert.IsInstanceOf<LobbyState>(_gameManager.GetGameState(_gameManager.currentGameStateIndex.Value),
                "Precondition: the current game state must be the LobbyState for the lobby-leave branch.");

            InvokeHandlePlayerLeft(leaverId);

            // Lobby branch removes (despawns) the character — bounded wait for it to drop from the projection.
            yield return NetworkTestHelper.WaitUntilOrTimeout(
                () => _characterManager.GetCharacter(leaverId, false) == null, 5f,
                "Lobby leave did not REMOVE the leaver's character (pipeline lobby branch).");
            Assert.IsTrue(_gameManager.HasClientLeft(leaverId),
                "The departed real client must be recorded in the leave bookkeeping.");

            // Exactly once / idempotent: a second invocation on the already-removed seat must not throw and must
            // leave the character removed (no resurrection, no double-remove error).
            InvokeHandlePlayerLeft(leaverId);
            yield return null;
            Assert.IsNull(_characterManager.GetCharacter(leaverId, false),
                "A repeat lobby leave must keep the character removed (idempotent, no error).");
        }

        [UnityTest]
        public IEnumerator HandlePlayerLeft_CalledTwice_LobbyLeaver_RemovesOnce_NoOp_NoError()
        {
            // [LIVENESS B2] §8.7c — the LOBBY-branch counterpart to PlayerLeaveIdempotencyTests (which only
            // exercises the mid-game/chaining branch). Two ignition sources (liveness + transport backstop) now
            // call HandlePlayerLeft; when the current state is a real LobbyState the FIRST call must run the
            // characterManager.RemoveCharacter branch EXACTLY ONCE and every later call must be a total no-op —
            // no NRE, no double-remove. Proven by COUNTING the lobby-remove log (the second call short-circuits
            // at the idempotency guard BEFORE that log), not merely by the end-state — a double-remove that
            // happened to leave the same end-state would slip past an end-state-only check.
            const ulong leaverId = 8;
            const string lobbyRemoveLogFragment = "left in lobby";

            Character leaver = _characterManager.AddNewCharacter(leaverId);
            Assert.IsNotNull(leaver, "AddNewCharacter returned null for the lobby leaver.");
            yield return NetworkTestHelper.WaitUntilAllSpawnedOrTimeout(leaver);
            yield return NetworkTestHelper.WaitUntilOrTimeout(
                () => _characterManager.GetCharacter(leaverId, false) != null, 5f,
                "CharacterManager never projected the lobby leaver before the leave.");

            // Make the real LobbyState the current state so the pipeline takes the `is LobbyState` lobby branch.
            int lobbyIndex = _gameManager.GetGameStateIndex(_lobbyState);
            Assert.GreaterOrEqual(lobbyIndex, 0, "Seeded LobbyState index not found.");
            _gameManager.currentGameStateIndex.Value = lobbyIndex;
            Assert.IsInstanceOf<LobbyState>(_gameManager.GetGameState(_gameManager.currentGameStateIndex.Value),
                "Precondition: the current game state must be the LobbyState for the lobby-leave branch.");

            var _errors = new List<string>();
            int _lobbyRemoveLogCount = 0;
            Application.LogCallback _handler = (condition, stackTrace, type) =>
            {
                if (condition != null && condition.Contains(lobbyRemoveLogFragment)) _lobbyRemoveLogCount++;
                if (type != LogType.Error && type != LogType.Exception && type != LogType.Assert) return;
                _errors.Add($"{type}: {condition}");
            };

            bool _prevIgnore = LogAssert.ignoreFailingMessages;
            LogAssert.ignoreFailingMessages = true;
            Application.logMessageReceived += _handler;
            try
            {
                InvokeHandlePlayerLeft(leaverId); // first ignition source — runs the lobby-remove branch
                InvokeHandlePlayerLeft(leaverId); // redundant second source — must short-circuit, total no-op
                yield return null;
            }
            finally
            {
                Application.logMessageReceived -= _handler;
                LogAssert.ignoreFailingMessages = _prevIgnore;
            }

            yield return NetworkTestHelper.WaitUntilOrTimeout(
                () => _characterManager.GetCharacter(leaverId, false) == null, 5f,
                "Lobby leave did not REMOVE the leaver's character (pipeline lobby branch).");

            Assert.AreEqual(1, _lobbyRemoveLogCount,
                "The lobby-remove branch must run EXACTLY ONCE across two calls — the second call is a no-op.");
            Assert.IsTrue(_gameManager.HasClientLeft(leaverId),
                "The departed real client must be recorded in the leave bookkeeping.");
            Assert.IsNull(_characterManager.GetCharacter(leaverId, false),
                "A repeat lobby leave must keep the character removed (idempotent).");
            Assert.IsEmpty(_errors,
                "The redundant lobby leave must not NRE or log any server-side error:\n" + string.Join("\n", _errors));
        }

        [UnityTest]
        public IEnumerator HandlePlayerLeft_SimulatedBotId_EarlyReturns_NoStateMutation()
        {
            // [LEAVE][PHASE 5] Task 11 — bots use clientId >= 100 and never open a transport connection, so they
            // never fire the disconnect callback. HandlePlayerLeft still defends the boundary: a stray >= 100 call
            // must EARLY-RETURN before any bookkeeping or state mutation (no departed record, no chain, no remove).
            const ulong realId = 2;
            const ulong botId = 100; // >= 100 → simulated-bot range

            Character real = _characterManager.AddNewCharacter(realId);
            Assert.IsNotNull(real, "AddNewCharacter returned null for the real character.");
            yield return NetworkTestHelper.WaitUntilAllSpawnedOrTimeout(real);
            yield return NetworkTestHelper.WaitUntilOrTimeout(
                () => _characterManager.GetCharacter(realId, false) != null, 5f,
                "CharacterManager never projected the real character.");

            InvokeHandlePlayerLeft(botId);
            yield return null;

            Assert.IsFalse(_gameManager.HasClientLeft(botId),
                "A bot id (>= 100) must NOT be recorded as departed — HandlePlayerLeft must early-return.");
            Assert.IsFalse(_gameManager.HasClientLeft(realId),
                "The unrelated real client must be untouched by a bot-id leave call.");
            Assert.IsNotNull(_characterManager.GetCharacter(realId, false),
                "A bot-id leave must not remove any real character.");
            Assert.IsFalse(real.isChained.Value,
                "A bot-id leave must not chain any real character (the pipeline never ran).");
        }

        // Invokes the single-reaction pipeline directly. The disconnect-callback wiring is exercised end-to-end
        // by PlayerLeaveMidGameTests; here we drive the branch logic on a live server. HandlePlayerLeft was made
        // public in [LIVENESS B2] (the liveness layer is a second ignition source of it), so the lookup accepts
        // Public | NonPublic to stay robust to the visibility.
        private void InvokeHandlePlayerLeft(ulong _clientId)
        {
            MethodInfo _method = typeof(GameManager).GetMethod("HandlePlayerLeft",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            Assert.IsNotNull(_method, "GameManager.HandlePlayerLeft not found (renamed?).");
            _method.Invoke(_gameManager, new object[] { _clientId });
        }
    }
}
