using System.Collections;
using System.Collections.Generic;
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

            // Authored fallback total = 1 role (LobbyState reads it because gameSettingsManager stays null).
            _roleAttribution = ScriptableObject.CreateInstance<RoleAttributionState>();
            AddRole(_roleAttribution, RoleID.Robot, count: 1);
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

        private static void AddRole(RoleAttributionState _state, RoleID _id, int count)
        {
            RoleDataObject _roleData = ScriptableObject.CreateInstance<RoleDataObject>();
            _roleData.role = new Role { roleID = _id, roleName = _id.ToString() };
            _roleData.powers = new List<Power>();
            _state.roleAttributionDictionary.Add(_roleData, new RoleAttributionSetting { roleToAttribute = count, canBeFake = false });
        }

        [UnityTest]
        public IEnumerator OnStartGameButtonPressed_MorePlayersThanRoles_WarnsAndDoesNotAdvance()
        {
            // 2 players, only 1 role to attribute → the guard (LobbyState.cs:42-46) must fire.
            Character c1 = _characterManager.AddNewCharacter(1);
            Character c2 = _characterManager.AddNewCharacter(2);
            yield return NetworkTestHelper.WaitUntilAllSpawnedOrTimeout(c1, c2);

            LogAssert.Expect(LogType.Warning, "Not enough roles to attribute to all players!");
            _lobbyState.OnStartGameButtonPressed();

            // The warning expectation alone proves the early-return branch ran; an unmet expectation fails the test.
            yield return null;
        }
    }
}
