using System.Collections;
using System.Collections.Generic;
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
    /// Server-authoritative vote-eligibility guards in <see cref="VoteState.CanVote"/> (added coverage). Drives the
    /// REAL method on a single host with spawned characters (same harness as VoteInsertionOrderTests), pinning the
    /// CURRENT behavior of each guard: the valid-live-voter accept, the unknown voter (no character) reject, the
    /// eliminated-voter reject, and the already-voted reject plus its _ignoreAlreadyVoted bypass. The vote-count
    /// OUTCOME (tally / tie-break) is separately pinned by the VoteTally EditMode tests.
    /// </summary>
    [Category("VoteState")]
    public class VoteStateCanVoteTests
    {
        private GameObject _networkManagerGo;
        private NetworkManager _networkManager;
        private GameObject _gameManagerGo;
        private GameManager _gameManager;
        private GameObject _characterManagerGo;
        private CharacterManager _characterManager;
        private GameObject _dummyCharPrefab;
        private VoteState _voteState;

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

            _dummyCharPrefab = new GameObject("CharacterPrefab_CanVote");
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

            _voteState = ScriptableObject.CreateInstance<VoteState>();
            _voteState.gameManager = _gameManager;
            _voteState.characterManager = _characterManager;
            _gameManager.gameStates.Add(_voteState, new GameStateSettings());

            yield return NetworkTestHelper.WaitUntilAllSpawnedOrTimeout(_gameManager, _characterManager);
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (_networkManager != null && _networkManager.IsListening) _networkManager.Shutdown();
            yield return NetworkTestHelper.WaitUntilOrTimeout(
                () => _networkManager == null || !_networkManager.IsListening, 5f,
                "NGO did not stop listening within 5s after Shutdown().");

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
        public IEnumerator CanVote_ValidLivePlayerNotVoted_ReturnsTrue()
        {
            Character c1 = _characterManager.AddNewCharacter(1);
            yield return NetworkTestHelper.WaitUntilAllSpawnedOrTimeout(c1);

            Assert.IsTrue(_voteState.CanVote(1),
                "A live, non-eliminated, non-fake voter who has not voted yet can vote.");
        }

        [UnityTest]
        public IEnumerator CanVote_UnknownPlayer_ReturnsFalse()
        {
            // No character with id 999 exists → CharacterQuery.GetCharacter returns null → cannot vote (line 97-99).
            Assert.IsFalse(_voteState.CanVote(999));
            yield return null;
        }

        [UnityTest]
        public IEnumerator CanVote_EliminatedPlayer_ReturnsFalse()
        {
            Character c1 = _characterManager.AddNewCharacter(1);
            yield return NetworkTestHelper.WaitUntilAllSpawnedOrTimeout(c1);

            c1.isEliminated.Value = true; // server-authoritative write

            Assert.IsFalse(_voteState.CanVote(1), "An eliminated voter cannot vote (line 97).");
        }

        [UnityTest]
        public IEnumerator CanVote_AlreadyVoted_ReturnsFalse_AndIgnoreFlagBypasses()
        {
            Character c1 = _characterManager.AddNewCharacter(1);
            Character c2 = _characterManager.AddNewCharacter(2);
            yield return NetworkTestHelper.WaitUntilAllSpawnedOrTimeout(c1, c2);

            // Record that voter 1 already cast a vote (for player 2).
            _voteState.votesForPlayer[2] = new List<ulong> { 1 };

            Assert.IsFalse(_voteState.CanVote(1),
                "A voter already present in a vote list cannot vote again (line 91-93).");
            Assert.IsTrue(_voteState.CanVote(1, true),
                "The _ignoreAlreadyVoted flag bypasses the already-voted guard for an otherwise-valid voter.");
        }
    }
}
