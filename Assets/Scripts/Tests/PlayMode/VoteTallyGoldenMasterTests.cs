using System.Collections;
using System.Reflection;
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
    /// Characterization golden masters for the CURRENT "VoteTally" behavior (Story 1.5, PR B).
    /// The tally-as-it-exists-today is the vote-count → outcome block in <see cref="VoteState.OnEndStateServer"/>
    /// (VoteState.cs:186–198). These goldens drive the REAL vote path on the host (seed buckets via
    /// OnStartStateServer, cast votes via the real server vote handler, resolve via OnEndStateServer) and pin the
    /// two observable outputs: <see cref="VoteState.mostVotedPlayer"/> and whether the winner was added to
    /// <see cref="ChainingManager.chainingPlayers"/>.
    ///
    /// ORDERING PROPERTY NOTE (AC line 184): the tally orders by descending count and inspects ordered.First(),
    /// which is enumeration-order-dependent — BUT only in the tie case, which always routes to SKIP_VOTE_ID
    /// regardless of which tied entry is first. So the OUTCOME is order-independent despite the order-dependent
    /// intermediate. The single-winner case has a unique max, so First() is unambiguous.
    ///
    /// Reuses the Story 1.0/1.2 VictoryConditionTests host harness (NGO host + GameManager + CharacterManager),
    /// extended with a spawned ChainingManager and a registered VoteState instance.
    /// </summary>
    public class VoteTallyGoldenMasterTests
    {
        private GameObject _networkManagerGo;
        private NetworkManager _networkManager;
        private GameObject _gameManagerGo;
        private GameManager _gameManager;
        private GameObject _characterManagerGo;
        private CharacterManager _characterManager;
        private GameObject _chainingManagerGo;
        private ChainingManager _chainingManager;
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

            _dummyCharPrefab = new GameObject("CharacterPrefab_Vote");
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

            // Register a VoteState so GameManager.DoStateMethodRpc can resolve it by type name during the real path.
            _voteState = ScriptableObject.CreateInstance<VoteState>();
            _voteState.gameManager = _gameManager;
            _voteState.voteDuration = 999f; // keep the timer from auto-advancing during the test
            _gameManager.gameStates.Add(_voteState, new GameStateSettings());

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

            _chainingManagerGo = new GameObject("ChainingManager");
            _chainingManagerGo.AddComponent<NetworkObject>();
            _chainingManager = _chainingManagerGo.AddComponent<ChainingManager>();
            _chainingManager.GetComponent<NetworkObject>().Spawn();

            yield return NetworkTestHelper.WaitUntilAllSpawnedOrTimeout(_gameManager, _characterManager, _chainingManager);

            VoteState.mostVotedPlayer = ulong.MaxValue - 1; // sentinel: a test must overwrite this via OnEndStateServer
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (_networkManager != null && _networkManager.IsListening) _networkManager.Shutdown();
            yield return NetworkTestHelper.WaitUntilOrTimeout(() => _networkManager == null || !_networkManager.IsListening, 5f, "NGO did not stop listening within 5s after Shutdown().");

            ReflectionHelper.SetPrivateField(typeof(GameManager), "instance", null);
            ReflectionHelper.SetPrivateField(typeof(CharacterManager), "instance", null);

            Object.Destroy(_gameManagerGo);
            Object.Destroy(_characterManagerGo);
            Object.Destroy(_chainingManagerGo);
            Object.Destroy(_networkManagerGo);
            Object.Destroy(_dummyCharPrefab);
            yield return null;
        }

        // --- helpers ---

        private IEnumerator SpawnVoters(params ulong[] ids)
        {
            var spawned = new Character[ids.Length];
            for (int i = 0; i < ids.Length; i++)
            {
                spawned[i] = _characterManager.AddNewCharacter(ids[i]);
            }
            yield return NetworkTestHelper.WaitUntilAllSpawnedOrTimeout(spawned);
        }

        /// <summary>Casts a vote through the REAL server handler (with CanVote gating), by reflection (it is private).</summary>
        private void CastVote(ulong senderId, ulong votedId)
        {
            var method = typeof(VoteState).GetMethod("OnPlayerVotedRpc", BindingFlags.NonPublic | BindingFlags.Instance);
            method.Invoke(_voteState, new object[] { senderId, votedId });
        }

        private bool ChainingContains(ulong id)
        {
            foreach (var c in _chainingManager.chainingPlayers)
            {
                if (c == id)
                {
                    return true;
                }
            }
            return false;
        }

        // --- goldens ---

        [UnityTest]
        [Category("GoldenMaster")]
        [Category("VoteTally")]
        public IEnumerator ClearSingleWinner_MostVotedIsWinner_AndChained()
        {
            yield return SpawnVoters(1, 2, 3);
            _voteState.OnStartStateServer();

            CastVote(1, 1);
            CastVote(2, 1);
            CastVote(3, 1); // unanimous for char 1

            _voteState.OnEndStateServer();

            Assert.AreEqual(1UL, VoteState.mostVotedPlayer, "Unanimous winner must be the most-voted player.");
            Assert.IsTrue(ChainingContains(1), "The clear winner must be added to the chaining list.");
        }

        [UnityTest]
        [Category("GoldenMaster")]
        [Category("VoteTally")]
        public IEnumerator TieForTop_ResolvesToSkip_NobodyChained()
        {
            yield return SpawnVoters(1, 2, 3);
            _voteState.OnStartStateServer();

            CastVote(1, 2); // char 2: 1 vote
            CastVote(2, 3); // char 3: 1 vote  -> two-way tie at the top

            _voteState.OnEndStateServer();

            Assert.AreEqual(VoteState.SKIP_VOTE_ID, VoteState.mostVotedPlayer, "A tie for the top must resolve to SKIP.");
            Assert.IsFalse(ChainingContains(2), "Nobody is chained on a tie.");
            Assert.IsFalse(ChainingContains(3), "Nobody is chained on a tie.");
        }

        [UnityTest]
        [Category("GoldenMaster")]
        [Category("VoteTally")]
        public IEnumerator AbstentionWins_ResolvesToSkip_NobodyChained()
        {
            yield return SpawnVoters(1, 2, 3);
            _voteState.OnStartStateServer();

            CastVote(1, VoteState.SKIP_VOTE_ID);
            CastVote(2, VoteState.SKIP_VOTE_ID);
            CastVote(3, VoteState.SKIP_VOTE_ID); // SKIP bucket is the strict max

            _voteState.OnEndStateServer();

            Assert.AreEqual(VoteState.SKIP_VOTE_ID, VoteState.mostVotedPlayer, "SKIP winning the count still resolves to SKIP (the != SKIP guard).");
            Assert.IsFalse(ChainingContains(1));
            Assert.IsFalse(ChainingContains(2));
            Assert.IsFalse(ChainingContains(3));
        }

        [UnityTest]
        [Category("GoldenMaster")]
        [Category("VoteTally")]
        public IEnumerator SingleVoter_ThatPlayerWins_AndChained()
        {
            yield return SpawnVoters(1, 2, 3);
            _voteState.OnStartStateServer();

            CastVote(1, 2); // only one vote, for char 2

            _voteState.OnEndStateServer();

            Assert.AreEqual(2UL, VoteState.mostVotedPlayer, "A single vote gives a unique max → that player wins.");
            Assert.IsTrue(ChainingContains(2));
        }

        [UnityTest]
        [Category("GoldenMaster")]
        [Category("VoteTally")]
        [Category("VacuousTruth")]
        public IEnumerator ZeroVoters_ResolvesToSkip_NobodyChained()
        {
            yield return SpawnVoters(1, 2, 3);
            _voteState.OnStartStateServer();

            // No votes cast — every bucket ties at 0.
            _voteState.OnEndStateServer();

            Assert.AreEqual(VoteState.SKIP_VOTE_ID, VoteState.mostVotedPlayer, "All-zero counts tie → SKIP.");
            Assert.IsFalse(ChainingContains(1));
            Assert.IsFalse(ChainingContains(2));
            Assert.IsFalse(ChainingContains(3));
        }
    }
}
