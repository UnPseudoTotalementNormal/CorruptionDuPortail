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

        private IEnumerator SpawnUgesWithMarque(List<PMarqueHurluberluges> _out) =>
            SpawnUgesWithMarque(_networkManager.LocalClientId, _out);

        private IEnumerator SpawnUgesWithMarque(ulong _slot, List<PMarqueHurluberluges> _out)
        {
            Character _uges = _characterManager.AddNewCharacter(_slot);
            yield return NetworkTestHelper.WaitUntilSpawnedOrTimeout(_uges);
            _uges.role = new Role { roleID = RoleID.Uges, factionType = FactionType.chosen };

            // Built inactive so isPassive is set before Awake snapshots it (BaseIsPassive), like the real prefab
            // (isPassive: 1): otherwise the Marque counts as an active, stealable power.
            GameObject _go = new GameObject("MarqueHurluberluges");
            _go.SetActive(false);
            _go.AddComponent<NetworkObject>();
            var _marque = _go.AddComponent<PMarqueHurluberluges>();
            _marque.powerName = "Marque";
            _marque.isPassive = true;
            _go.SetActive(true);
            Assert.IsTrue(_marque.BaseIsPassive, "The Marque is passive, as authored on its prefab.");
            _go.GetComponent<NetworkObject>().Spawn();
            _marque.ownerClientId.Value = _slot;
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
        public IEnumerator StolenReincarnation_GrantsAreUsableTheSameNight()
        {
            // Board T26 (Wouh, 2026-10-27): only Ugës's own stolen powers are one per night (green); what he gets through
            // a copied Incomplet / Luma is usable the same night (red).
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
            // Same frame, before the use is consumed: the awakening flow checks Ugës's usable powers right after the
            // use, so the grant must already be in his kit or he is put back to sleep before he can use it.
            Assert.IsTrue(OwnedBy(_networkManager.LocalClientId).Any(_p => _p.powerName == "Granted"),
                "The grant is in Ugës's kit in the same frame as the Réincarnation.");
            _reincarnation.OnUsed(false);

            Power _granted = OwnedBy(_networkManager.LocalClientId).First(_p => _p.powerName == "Granted");
            Assert.IsTrue(_marque.CopiesLocked, "Using the stolen Réincarnation spent tonight's Marque budget.");
            Assert.IsTrue(_granted.isStolenCopy.Value, "A stolen Réincarnation grants one-shot copies.");
            Assert.AreEqual(0UL, _granted.marqueSourceId.Value, "The grant is tied to no Marque budget.");
            Assert.IsFalse(_granted.IsLockedByMarque(), "The grant is usable the same night.");
        }

        [UnityTest]
        public IEnumerator CopiedReincarnation_OnUges_GivesOneRandomCopyUsableAtOnce()
        {
            var _sources = new List<Power>();
            yield return AddChosenWithActivePower<Power>(731, RoleID.Dryade, "Source A", _sources);
            yield return AddChosenWithActivePower<Power>(732, RoleID.Omniscient, "Source B", _sources);
            var _marques = new List<PMarqueHurluberluges>();
            yield return SpawnUgesWithMarque(733, _marques);
            Power _reincarnation = null;
            yield return ThiefWithCopiedReincarnation(RoleID.Croupiere, _p => _reincarnation = _p);

            ReflectionHelper.InvokePrivateMethod(_reincarnation, "ReincarnatePlayerRpc", (ulong)733);
            yield return WaitFrames(10);

            List<Power> _granted = OwnedBy(_networkManager.LocalClientId).Where(_p => _p != _reincarnation).ToList();
            Assert.AreEqual(1, _granted.Count, "A copied Réincarnation on Ugës gives ONE power, not his three. Got: " +
                                               string.Join(", ", _granted.Select(_p => _p.powerName.ToString())));
            Assert.IsTrue(_granted[0].powerName == "Source A" || _granted[0].powerName == "Source B",
                "The power is drawn among the élus' active powers. Got: " + _granted[0].powerName + " [T26-TEST] characters=" +
                string.Join(",", _characterManager.GetCharacters(false).Select(_c => $"{_c.ownerClientId.Value}:{_c.role?.roleID}:{_c.role?.factionType}:" +
                    string.Join("+", _c.role?.powers.Select(_p => _p.powerName.ToString()) ?? new string[0]))));
            Assert.IsFalse(_granted[0] is PMarqueHurluberluges, "The Marque itself is not copied.");
            Assert.IsTrue(_granted[0].isStolenCopy.Value, "One-shot copy.");
            Assert.AreEqual(0UL, _granted[0].marqueSourceId.Value, "Usable at once: tied to no Marque budget.");
        }

        [UnityTest]
        public IEnumerator IncompletOwnReincarnation_OnUges_CopiesTheWholeMarque()
        {
            var _marques = new List<PMarqueHurluberluges>();
            yield return SpawnUgesWithMarque(743, _marques);
            var _own = new List<Power>();
            yield return AddChosenWithActivePower<PReincarnation>(_networkManager.LocalClientId, RoleID.Incomplet, "Reincarnation", _own);

            ReflectionHelper.InvokePrivateMethod(_own[0], "ReincarnatePlayerRpc", (ulong)743);
            yield return NetworkTestHelper.WaitUntilOrTimeout(
                () => OwnedBy(_networkManager.LocalClientId).OfType<PMarqueHurluberluges>().Any(), 3f,
                "L'Incomplet's own Réincarnation on Ugës gives him the Marque (its three powers).");

            PMarqueHurluberluges _copied = OwnedBy(_networkManager.LocalClientId).OfType<PMarqueHurluberluges>().Single();
            yield return NetworkTestHelper.WaitUntilOrTimeout(() => _copied.isCopiedPower.Value, 3f, "The Marque is a copy.");
            Assert.IsFalse(_copied.isStolenCopy.Value, "A permanent copy, not a one-shot.");
        }

        [UnityTest]
        public IEnumerator Reincarnation_NeverCopiesAnIncomplet()
        {
            var _incomplet = new List<Power>();
            yield return AddChosenWithActivePower<PReincarnation>(751, RoleID.Incomplet, "Reincarnation of 751", _incomplet);
            Power _reincarnation = null;
            yield return ThiefWithCopiedReincarnation(RoleID.Uges, _p => _reincarnation = _p);

            ReflectionHelper.InvokePrivateMethod(_reincarnation, "ReincarnatePlayerRpc", (ulong)751);
            yield return WaitFrames(10);

            Assert.AreEqual(0, OwnedBy(_networkManager.LocalClientId).Count(_p => _p != _reincarnation),
                "A copy of l'Incomplet never copies l'Incomplet.");
        }

        [UnityTest]
        public IEnumerator Melange_OnFacticeUges_GivesOneRandomCopyUsableAtOnce()
        {
            var _sources = new List<Power>();
            yield return AddChosenWithActivePower<Power>(761, RoleID.Dryade, "Source A", _sources);
            yield return AddChosenWithActivePower<Power>(762, RoleID.Omniscient, "Source B", _sources);
            var _marques = new List<PMarqueHurluberluges>();
            yield return SpawnUgesWithMarque(763, _marques);
            var _luma = new List<Power>();
            yield return AddChosenWithActivePower<PCardsShuffling>(_networkManager.LocalClientId, RoleID.Croupiere, "Melange", _luma);

            ReflectionHelper.InvokePrivateMethod(_luma[0], "GrantCopyFromFakeRole", _characterManager.GetCharacter(763));
            yield return WaitFrames(10);

            List<Power> _granted = OwnedBy(_networkManager.LocalClientId).Where(_p => _p != _luma[0]).ToList();
            Assert.AreEqual(1, _granted.Count, "Luma on a factice Ugës gets ONE power. Got: " +
                                               string.Join(", ", _granted.Select(_p => _p.powerName.ToString())));
            Assert.IsTrue(_granted[0].powerName == "Source A" || _granted[0].powerName == "Source B",
                "The power is drawn among the élus' active powers. Got: " + _granted[0].powerName + " [T26-TEST] characters=" +
                string.Join(",", _characterManager.GetCharacters(false).Select(_c => $"{_c.ownerClientId.Value}:{_c.role?.roleID}:{_c.role?.factionType}:" +
                    string.Join("+", _c.role?.powers.Select(_p => _p.powerName.ToString()) ?? new string[0]))));
            Assert.IsTrue(_granted[0].isStolenCopy.Value, "One-shot copy.");
            Assert.AreEqual(0UL, _granted[0].marqueSourceId.Value, "Usable at once: tied to no Marque budget.");
        }

        // The local player holds a one-shot copy of a Réincarnation (as Ugës or Luma would after a theft).
        private IEnumerator ThiefWithCopiedReincarnation(RoleID _thiefRole, System.Action<Power> _out)
        {
            var _list = new List<Power>();
            yield return AddChosenWithActivePower<PReincarnation>(_networkManager.LocalClientId, _thiefRole, "Reincarnation copy", _list);
            Power.ConfigureAsOneShotStolenCopy(_list[0]);
            _out(_list[0]);
        }

        private List<Power> OwnedBy(ulong _slot) =>
            Object.FindObjectsByType<Power>()
                .Where(_p => _p != null && _p.IsSpawned && _p.ownerClientId.Value == _slot)
                .ToList();

        private static IEnumerator WaitFrames(int _frames)
        {
            for (int _i = 0; _i < _frames; _i++)
            {
                yield return null;
            }
        }
    }
}
