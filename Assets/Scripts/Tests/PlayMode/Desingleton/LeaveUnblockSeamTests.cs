using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using Characters;
using GameLogic;
using GameLogic.GameStates;
using NUnit.Framework;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.TestTools;

namespace Tests.PlayMode.Desingleton
{
    /// <summary>
    /// [LEAVE] Phase 2 (epic-player-leave-stability) — host-only PlayMode smokes for the per-state unblock SEAMS
    /// the leave pipeline calls after chaining a mid-game leaver. Each state exposes a public
    /// <c>OnPlayerLeftServer</c> (or, for Victory, <c>TryResolveVictoryNow</c>) that
    /// <c>GameManager.UnblockCurrentStateAfterLeave</c> dispatches to. These call the seams DIRECTLY on a live
    /// server so the drain/guard logic is proven without the full scene graph (the DummyGameState 2-NM fixture
    /// cannot run the real states). What is NOT covered here — the actual GameEndingState transition + RPC
    /// fan-out on a real win, and the full night/vote scene flow — is the Phase 5 integration harness.
    ///
    /// Self-contained host harness (mirrors VictoryLoopRepointTests), NOT MultiClientGameFixture: these seams are
    /// server-authoritative and need no second client. Documented exception to the "route through
    /// NetworkTestHelper" rule, same as the sibling desingleton/migration fixtures.
    /// </summary>
    [Category("Desingleton")]
    public class LeaveUnblockSeamTests
    {
        private GameObject _networkManagerGo;
        private NetworkManager _networkManager;
        private GameObject _gameManagerGo;
        private GameManager _gameManager;
        private GameObject _characterManagerGo;
        private CharacterManager _characterManager;
        private GameObject _charactersParentGo;
        private GameObject _dummyCharPrefab;

        private readonly List<GameState> _seededStates = new();
        private readonly List<ScriptableObject> _standaloneStates = new();

        // NOTE: the seeded dummy state is a TOP-LEVEL type (SeamDummyState below), NOT a nested class. The portal
        // smoke transitions the loop (NextGameState -> SwitchGameState -> DoStateMethodRpc(GetType().FullName, ...)),
        // which serializes the state type's FullName into a FixedString64Bytes (≤63 bytes). A nested test type's
        // FullName ("...LeaveUnblockSeamTests+DummyGameState") overflows that and throws — a top-level type in this
        // short namespace stays well under the limit, matching real GameState FullNames.

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

            _dummyCharPrefab = new GameObject("CharacterPrefab_LeaveSeam");
            _dummyCharPrefab.AddComponent<NetworkObject>();
            _dummyCharPrefab.AddComponent<Character>();
            _networkManager.NetworkConfig.Prefabs.Add(new NetworkPrefab { Prefab = _dummyCharPrefab });

            Assert.IsTrue(_networkManager.StartHost(), "NGO StartHost() failed — server did not start.");

            // GameManager (not spawned yet — wire characterManager FIRST so SetupGameStates threads it into the
            // cloned states, which TryResolveVictoryNow reads through).
            _gameManagerGo = new GameObject("GameManager");
            _gameManagerGo.AddComponent<NetworkObject>();
            _gameManager = _gameManagerGo.AddComponent<GameManager>();
            _gameManager.ignoreGameLoop = true;

            // Order: [Dummy0, Dummy1, VictoryConditionCheckState, GameEndingState]. isInGameLoop=false so a
            // NextGameState advance (portal smoke) is a clean 0->1 with no day/game-started events firing.
            AddSeededState(ScriptableObject.CreateInstance<SeamDummyState>());
            AddSeededState(ScriptableObject.CreateInstance<SeamDummyState>());
            AddSeededState(ScriptableObject.CreateInstance<VictoryConditionCheckState>());
            AddSeededState(ScriptableObject.CreateInstance<GameEndingState>());

            _characterManagerGo = new GameObject("CharacterManager");
            _characterManagerGo.AddComponent<NetworkObject>();
            _characterManager = _characterManagerGo.AddComponent<CharacterManager>();

            ReflectionHelper.SetPrivateField(_gameManager, "characterManager", _characterManager);

            _gameManager.GetComponent<NetworkObject>().Spawn();
            _characterManager.GetComponent<NetworkObject>().Spawn();

            _charactersParentGo = new GameObject("CharactersParent");
            _charactersParentGo.AddComponent<NetworkObject>().Spawn();
            ReflectionHelper.SetPrivateField(_characterManager, "_charactersParent", _charactersParentGo.transform);
            ReflectionHelper.SetPrivateField(_characterManager, "_characterPrefab", _dummyCharPrefab.GetComponent<NetworkObject>());

            yield return NetworkTestHelper.WaitUntilAllSpawnedOrTimeout(_gameManager, _characterManager);
        }

        private void AddSeededState(GameState _state)
        {
            _seededStates.Add(_state);
            _gameManager.gameStates.Add(_state, new GameStateSettings { isInGameLoop = false });
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (_networkManager != null && _networkManager.IsListening) _networkManager.Shutdown();
            yield return NetworkTestHelper.WaitUntilOrTimeout(() => _networkManager == null || !_networkManager.IsListening, 5f, "NGO did not stop listening within 5s after Shutdown().");

            ReflectionHelper.SetPrivateField(typeof(GameManager), "instance", null);
            ReflectionHelper.SetPrivateField(typeof(CharacterManager), "instance", null);

            if (_gameManagerGo != null) Object.Destroy(_gameManagerGo);
            if (_characterManagerGo != null) Object.Destroy(_characterManagerGo);
            if (_charactersParentGo != null) Object.Destroy(_charactersParentGo);
            if (_networkManagerGo != null) Object.Destroy(_networkManagerGo);
            if (_dummyCharPrefab != null) Object.Destroy(_dummyCharPrefab);

            foreach (var _state in _seededStates)
            {
                if (_state != null) Object.Destroy(_state);
            }
            _seededStates.Clear();
            foreach (var _state in _standaloneStates)
            {
                if (_state != null) Object.Destroy(_state);
            }
            _standaloneStates.Clear();
            yield return null;
        }

        private IEnumerator SpawnCharacter(ulong _ownerId, Role _role, System.Action<Character> _onSpawned)
        {
            Character _character = _characterManager.AddNewCharacter(_ownerId);
            Assert.IsNotNull(_character, $"AddNewCharacter returned null for id {_ownerId}.");
            _character.role = _role;
            yield return NetworkTestHelper.WaitUntilSpawnedOrTimeout(_character, 5f);
            yield return NetworkTestHelper.WaitUntilOrTimeout(
                () => _characterManager.GetCharacter(_ownerId, false) != null, 5f,
                $"CharacterManager never projected the spawned Character for id {_ownerId}.");
            _onSpawned(_character);
        }

        private T NewStandalone<T>() where T : ScriptableObject
        {
            var _state = ScriptableObject.CreateInstance<T>();
            _standaloneStates.Add(_state);
            return _state;
        }

        // ─────────────────────────── AC5 (victory half) ───────────────────────────

        [UnityTest]
        public IEnumerator TryResolveVictoryNow_NoWinningCondition_ReturnsFalse_AndDoesNotTransition()
        {
            // One un-chained anomaly, no winning conditions → the evaluator finds no winner.
            Character _c = null;
            yield return SpawnCharacter(1, new Role { factionType = FactionType.anomaly }, _spawned => _c = _spawned);
            Assert.IsNotNull(_c);

            int _indexBefore = _gameManager.currentGameStateIndex.Value;
            var _victory = (VictoryConditionCheckState)_gameManager.GetGameStates(typeof(VictoryConditionCheckState))[0];

            bool _ended = _victory.TryResolveVictoryNow();

            Assert.IsFalse(_ended, "With no satisfied win-condition the resolver must return false (no winner).");
            Assert.AreEqual(_indexBefore, _gameManager.currentGameStateIndex.Value,
                "A no-winner resolve must NOT transition the game state.");
        }

        // ─────────────────────────── AC2 (awakening drain) ───────────────────────────

        [UnityTest]
        public IEnumerator AwakeningState_ChainedAwakenedLeaver_IsDrainedFromLayer()
        {
            Character _leaver = null;
            Character _other = null;
            yield return SpawnCharacter(1, new Role { factionType = FactionType.anomaly }, _s => _leaver = _s);
            yield return SpawnCharacter(2, new Role { factionType = FactionType.anomaly }, _s => _other = _s);

            var _awakening = NewStandalone<AwakeningState>();
            _awakening.gameManager = _gameManager;
            _awakening.characterManager = _characterManager;

            // Both awakened this layer; two entries so draining ONE leaves the layer non-empty (no advance,
            // so this smoke needs no awakeningOrder). Proves the specific-leaver removal, the load-bearing part.
            _leaver.isAwakened.Value = true;
            _other.isAwakened.Value = true;
            _awakening.currentlyAwakenedCharacters.Add(_leaver);
            _awakening.currentlyAwakenedCharacters.Add(_other);

            // The pipeline chains the leaver BEFORE unblocking.
            _leaver.ChainCharacterServer();

            _awakening.OnPlayerLeftServer(1);

            Assert.IsFalse(_awakening.currentlyAwakenedCharacters.Contains(_leaver),
                "The chained leaver must be drained from currentlyAwakenedCharacters so the layer can advance.");
            Assert.IsTrue(_awakening.currentlyAwakenedCharacters.Contains(_other),
                "A still-present awakened character must remain — the drain is leaver-specific.");
            Assert.IsFalse(_leaver.isAwakened.Value,
                "Draining an awakened leaver must also put it to sleep (isAwakened false).");
        }

        // ─────────────────────────── AC3 (mage/portal) ───────────────────────────

        [UnityTest]
        public IEnumerator TakeDownThePortal_MageLeaves_AdvancesLoop()
        {
            var _portal = NewStandalone<TakeDownThePortalState>();
            _portal.gameManager = _gameManager;
            _portal.mageCharacterOwnerId = 1;

            Assert.AreEqual(0, _gameManager.currentGameStateIndex.Value, "Precondition: start at state index 0.");

            _portal.OnPlayerLeftServer(1); // the awaited Mage leaves

            Assert.AreEqual(1, _gameManager.currentGameStateIndex.Value,
                "The awaited Mage leaving must advance the loop so the portal step cannot hang.");
            yield return null;
        }

        [UnityTest]
        public IEnumerator TakeDownThePortal_NonMageLeaves_IsNoOp()
        {
            var _portal = NewStandalone<TakeDownThePortalState>();
            _portal.gameManager = _gameManager;
            _portal.mageCharacterOwnerId = 1;

            int _indexBefore = _gameManager.currentGameStateIndex.Value;

            _portal.OnPlayerLeftServer(2); // NOT the Mage — nobody else is subscribed, nothing to unblock

            Assert.AreEqual(_indexBefore, _gameManager.currentGameStateIndex.Value,
                "A non-Mage leaver must not advance the portal step.");
            yield return null;
        }

        // ─────────────────────────── AC4 (vote denominator) ───────────────────────────

        [UnityTest]
        public IEnumerator VoteState_DepartedNonVoterLeaves_ReducesDenominator_CollapsesTimer()
        {
            // 1 = present voter, 2 = the REAL departed leaver, 3 = chained-but-PRESENT (owner ruling: still eligible).
            Character _voter = null;
            Character _leaver = null;
            Character _chainedPresent = null;
            yield return SpawnCharacter(1, new Role { factionType = FactionType.chosen }, _s => _voter = _s);
            yield return SpawnCharacter(2, new Role { factionType = FactionType.chosen }, _s => _leaver = _s);
            yield return SpawnCharacter(3, new Role { factionType = FactionType.chosen }, _s => _chainedPresent = _s);
            Assert.IsNotNull(_voter);
            Assert.IsNotNull(_leaver);
            Assert.IsNotNull(_chainedPresent);

            var _vote = NewStandalone<VoteState>();
            _vote.gameManager = _gameManager;
            _vote.characterManager = _characterManager;

            // Players 1 and 3 have voted (2 cast votes). While all three are eligible the denominator is 3, so the
            // vote is NOT yet closeable (2 < 3).
            _vote.votesForPlayer[2] = new List<ulong> { 1, 3 };
            _vote.voteTimer = 100f;

            // Player 3 is chained but STILL PRESENT — the pipeline chains a vote outcome too. It must remain eligible.
            _chainedPresent.ChainCharacterServer();
            Assert.IsTrue(_vote.CanVote(3, true),
                "Owner ruling: a chained-but-PRESENT player is still eligible to vote (must NOT be excluded).");

            // Player 2 leaves mid-game: the pipeline chains it AND records the real departure (departed-set).
            _leaver.ChainCharacterServer();
            MarkClientDeparted(2);
            Assert.IsFalse(_vote.CanVote(2, true),
                "A DEPARTED real client must be excluded from voting (the discriminator is departure, not chaining).");

            _vote.OnPlayerLeftServer(2);

            // Denominator is now 2 (players 1 and 3, both present); 2 cast votes meet it → auto-close.
            Assert.LessOrEqual(_vote.voteTimer, 5f,
                "After the departed non-voter is excluded, the reduced denominator (present voters) is met — the vote must auto-close (timer collapsed).");
        }

        // Simulates the REAL departure the pipeline records: GameManager.HandlePlayerLeft adds the leaver's clientId
        // to a private departed-set that VoteState.CanVote reads via HasClientLeft. We populate it directly (no
        // second client needed) — the load-bearing discriminator is departure, never isChained.
        private void MarkClientDeparted(ulong _clientId)
        {
            FieldInfo _field = typeof(GameManager).GetField("_departedClientIds",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(_field, "GameManager._departedClientIds field not found (rename?).");
            var _set = (HashSet<ulong>)_field.GetValue(_gameManager);
            _set.Add(_clientId);
            Assert.IsTrue(_gameManager.HasClientLeft(_clientId), "HasClientLeft must report the departed client.");
        }
    }

    /// <summary>
    /// Top-level inert GameState seeded by <see cref="LeaveUnblockSeamTests"/>. Kept top-level (not nested) so its
    /// FullName ("Tests.PlayMode.Desingleton.SeamDummyState") fits the FixedString64Bytes the loop's DoStateMethodRpc
    /// serializes the state type name into during a NextGameState transition.
    /// </summary>
    public class SeamDummyState : GameState
    {
        public override void StateUpdateClient() { }
        public override void StateUpdateServer() { }
    }
}
