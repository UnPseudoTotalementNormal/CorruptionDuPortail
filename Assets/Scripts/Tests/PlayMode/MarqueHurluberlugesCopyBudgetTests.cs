using System.Collections;
using System.Collections.Generic;
using System.Linq;
using AudioSystem;
using Board;
using Characters;
using Characters.Powers;
using ChatSystem;
using CorruptionDuPortail.Domain;
using CorruptionDuPortail.Domain.Powers.State;
using GameLogic;
using Network;
using NUnit.Framework;
using RoleTarget;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.TestTools;

namespace Tests.PlayMode
{
    /// <summary>
    /// Ugës (Marque d'Hurluberluges): the stolen copies are not usable the night they are stolen, then one per night
    /// ("à partir du second tour, il peut utiliser une fois par nuit l'un de ces pouvoirs stockés"). The budget is
    /// held by the Marque (replicated <c>copiesLocked</c>) and each copy points back to it (<c>marqueSourceId</c>).
    /// Same harness as <see cref="EntrapmentPowerTests"/>: one host NetworkManager, real power spawn/reparent path.
    /// </summary>
    [Category("Powers")]
    public class MarqueHurluberlugesCopyBudgetTests
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

            _dummyCharPrefab = new GameObject("CharacterPrefab_Marque");
            _dummyCharPrefab.AddComponent<NetworkObject>();
            _dummyCharPrefab.AddComponent<Character>();
            _networkManager.NetworkConfig.Prefabs.Add(new NetworkPrefab { Prefab = _dummyCharPrefab });

            Assert.IsTrue(_networkManager.StartHost(), "NGO StartHost() failed — server did not start.");

            _gameManagerGo = new GameObject("GameManager");
            _gameManagerGo.AddComponent<NetworkObject>();
            _gameManager = _gameManagerGo.AddComponent<GameManager>();
            _gameManager.ignoreGameLoop = true;
            _gameManager.gameStates.Add(ScriptableObject.CreateInstance<DummyGameState>(), new GameStateSettings());
            _gameManager.GetComponent<NetworkObject>().Spawn();

            _characterManagerGo = new GameObject("CharacterManager");
            _characterManagerGo.AddComponent<NetworkObject>();
            _characterManager = _characterManagerGo.AddComponent<CharacterManager>();
            ReflectionHelper.SetPrivateField(_gameManager, "characterManager", _characterManager);
            _characterManager.GetComponent<NetworkObject>().Spawn();

            GameObject _charactersParent = new GameObject("CharactersParent");
            _charactersParent.AddComponent<NetworkObject>().Spawn();
            ReflectionHelper.SetPrivateField(_characterManager, "_charactersParent", _charactersParent.transform);
            ReflectionHelper.SetPrivateField(_characterManager, "_characterPrefab", _dummyCharPrefab.GetComponent<NetworkObject>());

            new GameObject("RoleTargetSystem").AddComponent<RoleTargetSystem>().gameObject.AddComponent<NetworkObject>().Spawn();
            ReflectionHelper.SetPrivateField(new GameObject("BoardManager").AddComponent<BoardManager>(), "characterManager", _characterManager);
            new GameObject("AudioManager").AddComponent<GameAudioManager>();
            new GameObject("ChatManager").AddComponent<ChatManager>().gameObject.AddComponent<NetworkObject>().Spawn();
            new GameObject("LobbyPlayerInfoHolder").AddComponent<LobbyPlayerInfoHolder>().gameObject.AddComponent<NetworkObject>().Spawn();
            new GameObject("ChainingManager").AddComponent<ChainingManager>().gameObject.AddComponent<NetworkObject>().Spawn();
            var _powerManager = new GameObject("PowerManager").AddComponent<PowerManager>();
            ReflectionHelper.SetPrivateField(_powerManager, "characterManager", _characterManager);
            ReflectionHelper.SetPrivateField(_powerManager, "gameManager", _gameManager);
            _powerManager.gameObject.AddComponent<NetworkObject>().Spawn();

            yield return NetworkTestHelper.WaitUntilAllSpawnedOrTimeout(_gameManager, _characterManager);
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (_networkManager != null && _networkManager.IsListening) _networkManager.Shutdown();
            yield return NetworkTestHelper.WaitUntilOrTimeout(() => _networkManager == null || !_networkManager.IsListening, 5f,
                "NGO did not stop listening within 5s after Shutdown().");

            ReflectionHelper.SetPrivateField(typeof(GameManager), "instance", null);
            ReflectionHelper.SetPrivateField(typeof(CharacterManager), "instance", null);
            ReflectionHelper.SetPrivateField(typeof(RoleTargetSystem), "instance", null);
            ReflectionHelper.SetPrivateField(typeof(BoardManager), "instance", null);
            ReflectionHelper.SetPrivateField(typeof(GameAudioManager), "instance", null);
            ReflectionHelper.SetPrivateField(typeof(ChatManager), "instance", null);
            ReflectionHelper.SetPrivateField(typeof(LobbyPlayerInfoHolder), "instance", null);
            ReflectionHelper.SetPrivateField(typeof(ChainingManager), "instance", null);
            ReflectionHelper.SetPrivateField(typeof(PowerManager), "instance", null);

            Object.Destroy(_gameManagerGo);
            Object.Destroy(_characterManagerGo);
            Object.Destroy(GameObject.Find("CharactersParent"));
            Object.Destroy(GameObject.Find("RoleTargetSystem"));
            Object.Destroy(GameObject.Find("BoardManager"));
            Object.Destroy(GameObject.Find("ChatManager"));
            Object.Destroy(GameObject.Find("LobbyPlayerInfoHolder"));
            Object.Destroy(GameObject.Find("ChainingManager"));
            Object.Destroy(GameObject.Find("PowerManager"));
            Object.Destroy(_networkManagerGo);
            Object.Destroy(_dummyCharPrefab);
            Object.Destroy(GameObject.Find("AudioManager"));
            yield return null;
        }

        // A chosen character owning one active power of type T (the power Ugës can steal).
        private IEnumerator AddChosenWithActivePower<T>(ulong _slot, RoleID _roleId, string _powerName, List<Power> _out) where T : Power
        {
            Character _character = _characterManager.AddNewCharacter(_slot);
            yield return NetworkTestHelper.WaitUntilSpawnedOrTimeout(_character);
            _character.role = new Role { roleID = _roleId, factionType = FactionType.chosen };

            GameObject _go = new GameObject(_powerName);
            _go.AddComponent<NetworkObject>();
            T _power = _go.AddComponent<T>();
            _power.powerName = _powerName;
            _power.isPassive = false;
            _go.GetComponent<NetworkObject>().Spawn();
            _power.ownerClientId.Value = _slot;
            yield return NetworkTestHelper.WaitUntilSpawnedOrTimeout(_power);
            // NET-08: role.powers is a projection of the power registry by ownerClientId: never add to it by hand
            // (a hand-added power is listed twice and stolen twice).
            yield return NetworkTestHelper.WaitUntilOrTimeout(() => _character.role.powers.Contains(_power), 3f,
                "The source power is listed in its owner's kit.");
            _out.Add(_power);
        }

        private IEnumerator SpawnUgesWithMarque(List<PMarqueHurluberluges> _out)
        {
            Character _uges = _characterManager.AddNewCharacter(_networkManager.LocalClientId);
            yield return NetworkTestHelper.WaitUntilSpawnedOrTimeout(_uges);
            _uges.role = new Role { roleID = RoleID.Uges, factionType = FactionType.chosen };

            GameObject _go = new GameObject("MarqueHurluberluges");
            _go.AddComponent<NetworkObject>();
            var _marque = _go.AddComponent<PMarqueHurluberluges>();
            _marque.powerName = "Marque";
            _marque.isPassive = true;
            _go.GetComponent<NetworkObject>().Spawn();
            _marque.ownerClientId.Value = _networkManager.LocalClientId;
            yield return NetworkTestHelper.WaitUntilSpawnedOrTimeout(_marque);
            _out.Add(_marque);
        }

        private List<Power> CopiesOf(PMarqueHurluberluges _marque) =>
            Object.FindObjectsByType<Power>()
                .Where(_p => _p != null && _p.IsSpawned && _p.marqueSourceId.Value == _marque.NetworkObjectId)
                .ToList();

        private IEnumerator StealAndWait(PMarqueHurluberluges _marque, int _expected)
        {
            ((IStolenPowerGrant)_marque).GrantStolen((int)_networkManager.LocalClientId);
            yield return NetworkTestHelper.WaitUntilOrTimeout(() => CopiesOf(_marque).Count == _expected, 3f,
                () => $"Ugës should hold {_expected} copies tied to his Marque. [MARQUE-TEST] characters=" +
                      string.Join(",", _characterManager.GetCharacters(false).Select(_c => $"{_c.ownerClientId.Value}:{_c.role?.factionType}:{_c.role?.powers.Count}")) +
                      " powers=" + string.Join(" | ", Object.FindObjectsByType<Power>().Select(_p =>
                          $"{_p.powerName} owner={_p.ownerClientId.Value} spawned={_p.IsSpawned} stolen={_p.isStolenCopy.Value} src={_p.marqueSourceId.Value} basePassive={_p.BaseIsPassive}")) +
                      $" marqueId={_marque.NetworkObjectId}");
        }

        private IEnumerator NewDay(PMarqueHurluberluges _marque)
        {
            _gameManager.onNewDayPassed.Invoke();
            yield return NetworkTestHelper.WaitUntilOrTimeout(() => !_marque.CopiesLocked, 3f,
                "A new day should unlock the Marque's copies.");
        }

        [UnityTest]
        public IEnumerator Copies_AreLockedTheNightTheyAreStolen_UnlockedTheNextDay()
        {
            var _sources = new List<Power>();
            yield return AddChosenWithActivePower<Power>(701, RoleID.Dryade, "Source A", _sources);
            yield return AddChosenWithActivePower<Power>(702, RoleID.Omniscient, "Source B", _sources);
            var _marques = new List<PMarqueHurluberluges>();
            yield return SpawnUgesWithMarque(_marques);
            PMarqueHurluberluges _marque = _marques[0];

            yield return StealAndWait(_marque, 2);
            List<Power> _copies = CopiesOf(_marque);

            Assert.IsTrue(_marque.CopiesLocked, "The night of the theft, the copies are locked.");
            Assert.IsTrue(_copies.All(_c => _c.isStolenCopy.Value), "Every copy is a one-shot stolen copy.");
            Assert.IsTrue(_copies.All(_c => _c.IsLockedByMarque()), "No copy is usable the night of the theft.");
            Assert.IsTrue(_copies.All(_c => !_c.CanUse()), "CanUse refuses a locked copy.");

            yield return NewDay(_marque);
            Assert.IsTrue(_copies.All(_c => !_c.IsLockedByMarque()), "From the next night, the copies are usable.");
            Assert.IsTrue(_sources.All(_s => !_s.IsLockedByMarque()), "The original owners' powers are never locked.");
        }

        [UnityTest]
        public IEnumerator UsingOneCopy_LocksTheOthersUntilTheNextNight_AndTheSpentCopyDespawns()
        {
            var _sources = new List<Power>();
            yield return AddChosenWithActivePower<Power>(711, RoleID.Dryade, "Source A", _sources);
            yield return AddChosenWithActivePower<Power>(712, RoleID.Omniscient, "Source B", _sources);
            var _marques = new List<PMarqueHurluberluges>();
            yield return SpawnUgesWithMarque(_marques);
            PMarqueHurluberluges _marque = _marques[0];
            yield return StealAndWait(_marque, 2);
            yield return NewDay(_marque);

            List<Power> _copies = CopiesOf(_marque);
            Power _used = _copies[0];
            Power _kept = _copies[1];

            _used.OnUsed(false); // the server-side use path (the host's own consume)

            Assert.IsTrue(_marque.CopiesLocked, "Using one copy locks the Marque for the rest of the night.");
            Assert.IsTrue(_kept.IsLockedByMarque(), "The other copy waits for the next night.");
            yield return NetworkTestHelper.WaitUntilOrTimeout(() => _used == null || !_used.IsSpawned, 3f,
                "The spent copy is despawned.");
            Assert.AreEqual(1, CopiesOf(_marque).Count, "One copy left.");

            yield return NewDay(_marque);
            Assert.IsFalse(_kept.IsLockedByMarque(), "The next night, the remaining copy is usable.");
            Assert.AreEqual(1, _kept.powerUseLeft.Value, "The remaining copy still has its single use.");
        }

        [UnityTest]
        public IEnumerator StolenReincarnation_GrantsJoinTheSameBudget()
        {
            var _sources = new List<Power>();
            yield return AddChosenWithActivePower<PReincarnation>(721, RoleID.Incomplet, "Reincarnation", _sources);
            var _marques = new List<PMarqueHurluberluges>();
            yield return SpawnUgesWithMarque(_marques);
            PMarqueHurluberluges _marque = _marques[0];
            yield return StealAndWait(_marque, 1);
            yield return NewDay(_marque);

            // A role whose active power the stolen Réincarnation will grant. Not stealable (Ugës already stole).
            var _targetPowers = new List<Power>();
            yield return AddChosenWithActivePower<Power>(722, RoleID.Dryade, "Granted", _targetPowers);

            Power _reincarnation = CopiesOf(_marque).Single();
            ReflectionHelper.InvokePrivateMethod(_reincarnation, "ReincarnatePlayerRpc", (ulong)722);
            _reincarnation.OnUsed(false);

            yield return NetworkTestHelper.WaitUntilOrTimeout(() => Object.FindObjectsByType<Power>()
                    .Any(_p => _p != null && _p.IsSpawned && _p.ownerClientId.Value == _networkManager.LocalClientId &&
                               _p.powerName == "Granted"), 3f,
                "The stolen Réincarnation grants the target role's active power.");
            Power _granted = Object.FindObjectsByType<Power>()
                .First(_p => _p != null && _p.IsSpawned && _p.ownerClientId.Value == _networkManager.LocalClientId && _p.powerName == "Granted");
            yield return NetworkTestHelper.WaitUntilOrTimeout(() => _granted.marqueSourceId.Value == _marque.NetworkObjectId, 3f,
                "The grant is tied to the same Marque.");

            Assert.IsTrue(_granted.isStolenCopy.Value, "A stolen Réincarnation grants one-shot copies.");
            Assert.IsTrue(_granted.IsLockedByMarque(), "Tonight's stored power was the Réincarnation: its grant waits for the next night.");

            yield return NewDay(_marque);
            Assert.IsFalse(_granted.IsLockedByMarque(), "The next night, the grant is usable.");
        }
    }
}
