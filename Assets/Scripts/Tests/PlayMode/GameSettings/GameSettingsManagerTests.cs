using System.Collections;
using System.Collections.Generic;
using Characters;
using Characters.Powers;
using GameLogic.GameSettings;
using GameLogic.GameStates;
using NUnit.Framework;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.TestTools;

namespace Tests.PlayMode
{
    /// <summary>
    /// Quick-dev gamesettings-refonte (2026-06-20). PlayMode coverage of the server-authoritative
    /// <see cref="GameSettingsManager"/>: it seeds the replicated role settings from the authored
    /// RoleAttributionState SO, exposes them to readers (counts / canBeFake / total), and applies host edits
    /// (clamped to the authored range, idempotent, raising OnSettingsChanged).
    ///
    /// Single-host (host == server): proves the server-authoritative seed / read / write / event path. The
    /// cross-wire replication to a real second client, the non-host-ignored gate, and the late-join snapshot
    /// are covered by NGO's NetworkList replication semantics + the spec's manual smoke; a dedicated two-NM
    /// settings-replication test is recorded in deferred-work (same spec-permitted reduction as 13.3/13.4).
    /// </summary>
    public class GameSettingsManagerTests
    {
        private GameObject _networkManagerGo;
        private NetworkManager _networkManager;
        private GameObject _managerGo;
        private GameSettingsManager _manager;
        private RoleAttributionState _authoredSource;

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

            Assert.IsTrue(_networkManager.StartHost(), "NGO StartHost() failed — server did not start.");

            // Authored defaults the manager seeds from (distinct RoleIDs so the per-role lookup is unambiguous).
            _authoredSource = ScriptableObject.CreateInstance<RoleAttributionState>();
            AddRole(_authoredSource, RoleID.Robot, "Robot", count: 2, canBeFake: true);
            AddRole(_authoredSource, RoleID.Abyss, "Abyss", count: 1, canBeFake: false);
            AddRole(_authoredSource, RoleID.Oracle, "Oracle", count: 3, canBeFake: true);

            _managerGo = new GameObject("GameSettingsManager");
            _managerGo.AddComponent<NetworkObject>();
            _manager = _managerGo.AddComponent<GameSettingsManager>();
            // Wire the authored source before Spawn() so OnNetworkSpawn (server) seeds from it.
            ReflectionHelper.SetPrivateField(_manager, "_authoredSettingsSource", _authoredSource);
            _managerGo.GetComponent<NetworkObject>().Spawn();

            yield return NetworkTestHelper.WaitUntilSpawnedOrTimeout(_manager);
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (_manager != null && _manager.IsSpawned) _manager.NetworkObject.Despawn(true);
            if (_networkManager != null && _networkManager.IsListening) _networkManager.Shutdown();
            yield return NetworkTestHelper.WaitUntilOrTimeout(
                () => _networkManager == null || !_networkManager.IsListening, 5f,
                "NGO did not stop listening within 5s after Shutdown().");

            if (_managerGo != null) Object.Destroy(_managerGo);
            if (_networkManagerGo != null) Object.Destroy(_networkManagerGo);

            if (_authoredSource != null)
            {
                foreach (RoleDataObject _key in new List<RoleDataObject>(_authoredSource.roleAttributionDictionary.Keys))
                {
                    if (_key != null) Object.Destroy(_key);
                }
                Object.Destroy(_authoredSource);
            }
            yield return null;
        }

        private static void AddRole(RoleAttributionState _state, RoleID _id, string _name, int count, bool canBeFake)
        {
            RoleDataObject _roleData = ScriptableObject.CreateInstance<RoleDataObject>();
            _roleData.role = new Role { roleID = _id, roleName = _name };
            _roleData.powers = new List<Power>();
            _state.roleAttributionDictionary.Add(_roleData, new RoleAttributionSetting { roleToAttribute = count, canBeFake = canBeFake });
        }

        [UnityTest]
        public IEnumerator Seed_FromAuthoredDefaults_ExposesCountsCanBeFakeAndTotal()
        {
            Assert.AreEqual(2, _manager.GetRoleCount(RoleID.Robot), "Robot count seeded from the authored SO.");
            Assert.AreEqual(1, _manager.GetRoleCount(RoleID.Abyss), "Abyss count seeded from the authored SO.");
            Assert.AreEqual(3, _manager.GetRoleCount(RoleID.Oracle), "Oracle count seeded from the authored SO.");
            Assert.AreEqual(6, _manager.GetTotalRolesToAttribute(), "Total = sum of seeded counts.");
            Assert.IsTrue(_manager.GetCanBeFake(RoleID.Robot), "Robot canBeFake seeded true.");
            Assert.IsFalse(_manager.GetCanBeFake(RoleID.Abyss), "Abyss canBeFake seeded false.");
            yield return null;
        }

        [UnityTest]
        public IEnumerator HostEdit_UpdatesCount_RaisesEvent_AndAdjustsTotal()
        {
            int _changedCount = 0;
            void Handler() => _changedCount++;
            _manager.OnSettingsChanged += Handler;

            _manager.RequestSetRoleCount(RoleID.Robot, 5);

            yield return NetworkTestHelper.WaitUntilOrTimeout(
                () => _changedCount >= 1, 3f, "OnSettingsChanged did not fire on a host edit.");

            Assert.AreEqual(5, _manager.GetRoleCount(RoleID.Robot), "Host edit updates the replicated count.");
            Assert.AreEqual(9, _manager.GetTotalRolesToAttribute(), "Total reflects the edit (5 + 1 + 3).");

            _manager.OnSettingsChanged -= Handler;
        }

        [UnityTest]
        public IEnumerator HostEdit_ClampsToAuthoredRange()
        {
            _manager.RequestSetRoleCount(RoleID.Abyss, 999);
            yield return null;
            Assert.AreEqual(15, _manager.GetRoleCount(RoleID.Abyss), "Count clamps to the server max (15, mirrors the slider max).");

            _manager.RequestSetRoleCount(RoleID.Abyss, -4);
            yield return null;
            Assert.AreEqual(0, _manager.GetRoleCount(RoleID.Abyss), "Count clamps to 0 (no negatives).");
        }

        [UnityTest]
        public IEnumerator HostEdit_NoOpValue_DoesNotRaiseEventAgain()
        {
            _manager.RequestSetRoleCount(RoleID.Oracle, 4);
            // Let the first change's OnListChanged fully process before subscribing.
            yield return null;
            yield return null;

            int _changedCount = 0;
            void Handler() => _changedCount++;
            _manager.OnSettingsChanged += Handler;

            _manager.RequestSetRoleCount(RoleID.Oracle, 4); // same value — must be a no-op
            yield return null;
            yield return null;

            Assert.AreEqual(0, _changedCount, "A no-op set must not replicate / raise OnSettingsChanged.");
            Assert.AreEqual(4, _manager.GetRoleCount(RoleID.Oracle), "Value is unchanged after the no-op set.");

            _manager.OnSettingsChanged -= Handler;
        }

        [UnityTest]
        public IEnumerator HostEdit_RoleIdNotInSettings_NoOps()
        {
            // RoleID.Gardien is NOT among the seeded roles (Robot/Abyss/Oracle). ApplyRoleCountServer scans the
            // list, finds no match, and returns without mutating any entry or raising the change event — pins the
            // role-not-found no-op (existing tests cover the clamp and the same-VALUE no-op, not the missing ROLE).
            int _changedCount = 0;
            void Handler() => _changedCount++;
            _manager.OnSettingsChanged += Handler;

            _manager.RequestSetRoleCount(RoleID.Gardien, 5);
            yield return null;
            yield return null;

            Assert.AreEqual(0, _changedCount, "A set for an unknown RoleID must not replicate / raise OnSettingsChanged.");
            Assert.AreEqual(0, _manager.GetRoleCount(RoleID.Gardien), "The unknown role stays absent (count 0).");
            Assert.AreEqual(6, _manager.GetTotalRolesToAttribute(), "Total is unchanged (2 + 1 + 3).");

            _manager.OnSettingsChanged -= Handler;
        }
    }
}
