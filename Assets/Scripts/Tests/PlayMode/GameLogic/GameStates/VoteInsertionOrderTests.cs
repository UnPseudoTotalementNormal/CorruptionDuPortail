using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Characters;
using GameLogic;
using GameLogic.GameStates;
using NUnit.Framework;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.TestTools;

namespace Tests.PlayMode
{
    /// <summary>
    /// DETERMINISM PIN (Story 1.1 §3b C — vote-insertion order).
    ///
    /// Pins the *current* server-side ordering that <see cref="VoteState"/> relies on:
    /// the order of <c>votesForPlayer</c> keys is exactly the non-fake
    /// <see cref="CharacterManager.GetCharacters"/> order (which is server-side
    /// spawn/add order), with <see cref="VoteState.SKIP_VOTE_ID"/> appended last.
    ///
    /// This is the ordering contract <c>VoteTally</c> (Story 2.9) must respect.
    /// Test-only: no production change. If this goes RED, the vote-tally substrate
    /// has drifted and any golden captured on it is no longer reproducible.
    /// </summary>
    [Category("Determinism")]
    public class VoteInsertionOrderTests
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

            _dummyCharPrefab = new GameObject("CharacterPrefab_VoteOrder");
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

            yield return NetworkTestHelper.WaitUntilAllSpawnedOrTimeout(_gameManager, _characterManager);
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (_networkManager != null && _networkManager.IsListening) _networkManager.Shutdown();
            yield return NetworkTestHelper.WaitUntilOrTimeout(() => _networkManager == null || !_networkManager.IsListening, 5f, "NGO did not stop listening within 5s after Shutdown().");

            // Domain reload may be disabled — statics survive across PlayMode sessions.
            ReflectionHelper.SetPrivateField(typeof(GameManager), "instance", null);
            ReflectionHelper.SetPrivateField(typeof(CharacterManager), "instance", null);

            Object.Destroy(_gameManagerGo);
            Object.Destroy(_characterManagerGo);
            Object.Destroy(_networkManagerGo);
            Object.Destroy(_dummyCharPrefab);
            yield return null;
        }

        [UnityTest]
        public IEnumerator GetCharacters_PreservesAddOrder_ForNonFakeCharacters()
        {
            // Deterministic add order: 1, 2, 3.
            Character c1 = _characterManager.AddNewCharacter(1);
            Character c2 = _characterManager.AddNewCharacter(2);
            Character c3 = _characterManager.AddNewCharacter(3);
            yield return NetworkTestHelper.WaitUntilAllSpawnedOrTimeout(c1, c2, c3);

            List<ulong> _order = _characterManager.GetCharacters()
                .Where(_c => !_c.isFake)
                .Select(_c => _c.ownerClientId.Value)
                .ToList();

            // GetCharacters() iterates networkedCharacters (NetworkList) in list order,
            // which is server-side add/spawn order. Pin it.
            CollectionAssert.AreEqual(new ulong[] { 1, 2, 3 }, _order,
                "GetCharacters() (non-fake) must preserve server-side add order — VoteTally (2.9) depends on it.");
        }

        [UnityTest]
        public IEnumerator VoteState_BuildsVoteKeys_InCharacterOrder_WithSkipLast()
        {
            Character c1 = _characterManager.AddNewCharacter(1);
            Character c2 = _characterManager.AddNewCharacter(2);
            Character c3 = _characterManager.AddNewCharacter(3);
            yield return NetworkTestHelper.WaitUntilAllSpawnedOrTimeout(c1, c2, c3);

            // Drive the REAL VoteState.OnStartStateServer() (VoteState.cs:158-179),
            // not a re-implementation. It must be registered in gameStates so the
            // internal DoStateMethodRpc dispatch resolves the state.
            var voteState = ScriptableObject.CreateInstance<VoteState>();
            voteState.gameManager = _gameManager;
            voteState.characterManager = _characterManager;
            _gameManager.gameStates.Add(voteState, new GameStateSettings());

            voteState.OnStartStateServer();

            // votesForPlayer keys are inserted in non-fake GetCharacters() order
            // (VoteState.cs:162-165), then SKIP_VOTE_ID is appended last (line 166).
            List<ulong> _voteKeys = voteState.votesForPlayer.Keys.ToList();
            CollectionAssert.AreEqual(new ulong[] { 1, 2, 3, VoteState.SKIP_VOTE_ID }, _voteKeys,
                "votesForPlayer key order must follow character add order with SKIP_VOTE_ID last.");
            Assert.AreEqual(VoteState.SKIP_VOTE_ID, _voteKeys.Last(),
                "SKIP_VOTE_ID must always be the last vote key.");
        }
    }
}
