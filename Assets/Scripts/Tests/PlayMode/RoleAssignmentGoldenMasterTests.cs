using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Characters;
using Characters.Powers;
using GameLogic;
using GameLogic.GameStates;
using NUnit.Framework;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.TestTools;
using Random = UnityEngine.Random;

namespace Tests.PlayMode
{
    /// <summary>
    /// GOLDEN MASTER (Story 3.2) — pins the CURRENT role-assignment output of the live
    /// <see cref="RoleAttributionState"/> selection under a fixed <c>UnityEngine.Random.InitState</c>
    /// seed, before the 3.3 <c>RoleDistributor</c> extraction. The selection itself is unchanged;
    /// only the RNG is determinized (3.1 added the port; the consumer wiring lands in 3.3).
    ///
    /// Faithful: it runs the REAL <c>OnStartStateServer</c> (not a re-implementation) with the state
    /// constructed standalone (NOT registered in gameStates → no SetupGameStates clone / spawn-time
    /// auto-run) and <c>gameManager</c> wired manually. Roles are read back from each spawned
    /// character's <c>role.roleName</c> (set synchronously in GiveRandomRole, RoleAttributionState.cs:110).
    /// RoleDataObjects carry empty powers + canBeFake=false and counts sum to N so the fake path is skipped.
    /// </summary>
    [Category("GoldenMaster")]
    [Category("RoleAssignment")]
    public class RoleAssignmentGoldenMasterTests
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

            _dummyCharPrefab = new GameObject("CharacterPrefab_RoleAssign");
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

            ReflectionHelper.SetPrivateField(typeof(GameManager), "instance", null);
            ReflectionHelper.SetPrivateField(typeof(CharacterManager), "instance", null);

            Object.Destroy(_gameManagerGo);
            Object.Destroy(_characterManagerGo);
            Object.Destroy(_networkManagerGo);
            Object.Destroy(_dummyCharPrefab);
            yield return null;
        }

        // Builds a standalone RoleAttributionState (NOT in gameStates) with the given pool, wired to gameManager.
        private RoleAttributionState BuildState(params (string name, int count)[] pool)
        {
            var state = ScriptableObject.CreateInstance<RoleAttributionState>();
            state.gameManager = _gameManager;
            state.characterManager = _characterManager;
            foreach (var (name, count) in pool)
            {
                var roleData = ScriptableObject.CreateInstance<RoleDataObject>();
                roleData.role = new Role { roleName = name };
                roleData.powers = new List<Power>();
                // canBeFake=false (mandatory) migrates to forced == max: 0 fakeable copies. Σmax==N ⇒ no surplus ⇒ fake path skipped.
                state.roleAttributionDictionary.Add(roleData, new RoleAttributionSetting { max = count, forced = count });
            }
            return state;
        }

        // Seeds, runs the REAL selection once, returns the assigned roleName per character in add-order.
        private List<string> RunAssignment(RoleAttributionState state, List<Character> characters, int seed)
        {
            Random.InitState(seed);
            state.OnStartStateServer();
            return characters.Select(c => c.role.roleName.ToString()).ToList();
        }

        private IEnumerator AddCharacters(int n, List<Character> outChars)
        {
            for (ulong id = 1; id <= (ulong)n; id++)
            {
                outChars.Add(_characterManager.AddNewCharacter(id));
            }
            yield return NetworkTestHelper.WaitUntilAllSpawnedOrTimeout(outChars.ToArray());
        }

        // Fakeable pool (canBeFake = true) for the fake-path golden (sum > N → CreateNewFakeCharacter runs).
        private RoleAttributionState BuildFakeableState(params (string name, int count)[] pool)
        {
            var state = ScriptableObject.CreateInstance<RoleAttributionState>();
            state.gameManager = _gameManager;
            state.characterManager = _characterManager;
            foreach (var (name, count) in pool)
            {
                var roleData = ScriptableObject.CreateInstance<RoleDataObject>();
                roleData.role = new Role { roleName = name };
                roleData.powers = new List<Power>();
                // canBeFake=true migrates to forced == 0: the whole pool is fakeable (old behaviour).
                state.roleAttributionDictionary.Add(roleData, new RoleAttributionSetting { max = count, forced = 0 });
            }
            return state;
        }

        // Runs the live two-loop selection (fakes created during the run) and returns
        // (fake roles in creation order, real roles in add order).
        private (List<string> fakes, List<string> reals) RunWithFakes(RoleAttributionState state, List<Character> reals, int seed)
        {
            Random.InitState(seed);
            state.OnStartStateServer();
            var fakeRoles = _characterManager.GetCharacters(false).Where(c => c.isFake).Select(c => c.role.roleName.ToString()).ToList();
            var realRoles = reals.Select(c => c.role.roleName.ToString()).ToList();
            return (fakeRoles, realRoles);
        }

        [UnityTest]
        public IEnumerator RoleAssignment_FakePath_Exhaustion() // fake-path discovery
        {
            // N=2 reals, pool [A:2, B:2] canBeFake → sum=4, fakeCount=|2-4|=2. Two fake characters are
            // created and drawn from {A,B}, then the two reals draw from the depleted remainder.
            var chars = new List<Character>();
            yield return AddCharacters(2, chars);

            var (fakes, reals) = RunWithFakes(BuildFakeableState(("A", 2), ("B", 2)), chars, seed: 5);

            // GOLDEN — the current fake-then-real draw order under InitState(5): the two fakes drain B,
            // then the two reals take the remaining A. Pins the shared-count depletion across both loops.
            CollectionAssert.AreEqual(new[] { "B", "B" }, fakes, "Fake characters draw first, exhausting B.");
            CollectionAssert.AreEqual(new[] { "A", "A" }, reals, "Reals draw from the depleted remainder (A).");
        }

        [UnityTest]
        public IEnumerator RoleAssignment_MinPlayers_SingleRole() // M-single
        {
            var chars = new List<Character>();
            yield return AddCharacters(1, chars);

            // OnStartStateServer mutates the (shared) RoleAttributionSetting counts as roles exhaust —
            // in prod each game runs on a fresh SetupGameStates clone, so the determinism re-run uses a
            // fresh state too (a single state cannot be re-run; its counts would already be depleted).
            var first = RunAssignment(BuildState(("Solo", 1)), chars, seed: 1);
            var again = RunAssignment(BuildState(("Solo", 1)), chars, seed: 1);

            Debug.Log($"[ROLEGOLD] MinPlayers_SingleRole seed=1: {string.Join(",", first)}");
            CollectionAssert.AreEqual(first, again, "Same seed + fresh state must reproduce the same assignment (determinism).");
            CollectionAssert.AreEqual(new[] { "Solo" }, first);
        }

        [UnityTest]
        public IEnumerator RoleAssignment_SingleRolePool_AllSame() // single-role-pool
        {
            var chars = new List<Character>();
            yield return AddCharacters(3, chars);

            var first = RunAssignment(BuildState(("Only", 3)), chars, seed: 7);
            var again = RunAssignment(BuildState(("Only", 3)), chars, seed: 7);

            Debug.Log($"[ROLEGOLD] SingleRolePool_AllSame seed=7: {string.Join(",", first)}");
            CollectionAssert.AreEqual(first, again, "Same seed + fresh state must reproduce the same assignment.");
            CollectionAssert.AreEqual(new[] { "Only", "Only", "Only" }, first);
        }

        [UnityTest]
        public IEnumerator RoleAssignment_MultiRole_Exhaustion_SeedA() // multi-exhaustion seed A
        {
            var chars = new List<Character>();
            yield return AddCharacters(5, chars);

            var first = RunAssignment(BuildState(("A", 2), ("B", 2), ("C", 1)), chars, seed: 42);
            var again = RunAssignment(BuildState(("A", 2), ("B", 2), ("C", 1)), chars, seed: 42);

            CollectionAssert.AreEqual(first, again, "Same seed + fresh state must reproduce the same assignment.");
            // GOLDEN — the current ordered assignment for [A:2, B:2, C:1] under InitState(42).
            CollectionAssert.AreEqual(new[] { "B", "A", "C", "B", "A" }, first);
        }

        [UnityTest]
        public IEnumerator RoleAssignment_MultiRole_Exhaustion_SeedB() // multi-exhaustion seed B
        {
            var chars = new List<Character>();
            yield return AddCharacters(5, chars);

            var first = RunAssignment(BuildState(("A", 2), ("B", 2), ("C", 1)), chars, seed: 99);
            var again = RunAssignment(BuildState(("A", 2), ("B", 2), ("C", 1)), chars, seed: 99);

            CollectionAssert.AreEqual(first, again, "Same seed + fresh state must reproduce the same assignment.");
            // GOLDEN — the current ordered assignment for [A:2, B:2, C:1] under InitState(99).
            CollectionAssert.AreEqual(new[] { "B", "B", "C", "A", "A" }, first);
        }
    }
}
