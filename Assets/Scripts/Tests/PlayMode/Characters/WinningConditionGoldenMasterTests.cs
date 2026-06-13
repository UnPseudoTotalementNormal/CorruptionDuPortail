using System.Collections;
using Characters;
using Characters.Powers;
using Characters.WinningConditions;
using GameLogic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Unity.Netcode;

namespace Tests.PlayMode
{
    /// <summary>
    /// GOLDEN-MASTER CORPUS (Story 1.2 — third blocking gate of Epic 1).
    ///
    /// Pins the CURRENT verdict of all four <see cref="WinningCondition"/>s on the
    /// faithful (Story 1.0) and deterministic (Story 1.1) <c>StartHost()</c> PlayMode
    /// harness. These goldens are the standing oracle Epic 2's strangler migration
    /// (2.3–2.6) and the post-cut oracle swap (2.7b) compare against. Test-only —
    /// no production file under <c>Assets/Scripts/Characters/WinningConditions/</c>
    /// (or anywhere outside <c>Tests/</c>) is touched.
    ///
    /// Each case encodes the current verdict as an <c>Assert</c> (it is both golden
    /// and regression), tagged <c>[Category("GoldenMaster")]</c>. Two distinct vacuous
    /// flavors additionally carry <c>[Category("VacuousTruth")]</c>.
    ///
    /// CAPTURE AS-IS, DO NOT "FIX": two behaviours look like bugs but are golden:
    ///   - <see cref="WOmniscienceHackedCharacter"/> THROWS NullReferenceException when
    ///     the owner is not found (no null guard at WOmniscienceHackedCharacter.cs:17-18).
    ///     Captured here as <c>Assert.Throws&lt;NullReferenceException&gt;</c> (case O1) so
    ///     an Epic-2 change to a silent <c>false</c> goes red and forces an explicit decision.
    ///   - vacuously-true empty-list / empty-relevant-subset verdicts (cases A1 / C1 / C2).
    ///
    /// ─────────────────────────────────────────────────────────────────────────────
    /// BOUNDARY MATRIX (the gate artifact — every row maps to ≥1 [UnityTest] below).
    /// Population for the two iterating conditions is GetCharacters(false).Where(!isFake).
    /// ─────────────────────────────────────────────────────────────────────────────
    ///
    /// WAnomalyCorruption  — team anomaly; reads isFake (filter), isCorrupted.Value.
    ///                       (WAnomalyCorruption.cs:19-29)
    ///  A1 empty population (loop never runs)            0 non-fake                       → true   [Golden,Vacuous]
    ///  A2 n=1 corrupted                                 1 corrupted                      → true   [Golden]
    ///  A3 n=1 not corrupted (early return)              1 not-corrupted                  → false  [Golden]
    ///  A4 n all corrupted                               n corrupted                      → true   [Golden]
    ///  A5 n, FIRST not corrupted (short-circuit pos.)   [false,true,true]                → false  [Golden]
    ///  A6 n, LAST not corrupted                         [true,true,false]                → false  [Golden]
    ///  A7 !isFake filter                                fake !corrupted + real corrupted → true   [Golden]
    ///
    /// WChosenChainedAllAnomaly — team chosen; reads isFake (filter), role.factionType,
    ///                            isChained.Value; continue on non-anomaly.
    ///                            (WChosenChainedAllAnomaly.cs:19-36)
    ///  C1 empty population                              0 non-fake                       → true   [Golden,Vacuous]
    ///  C2 empty relevant subset (all continue'd)        only non-anomaly factions        → true   [Golden,Vacuous]
    ///  C3 n=1 anomaly chained                           1 anomaly chained                → true   [Golden]
    ///  C4 n=1 anomaly not chained (early return)        1 anomaly free                   → false  [Golden]
    ///  C5 mix: chosen continue'd + all anomalies chained chosen + 2 chained anomalies    → true   [Golden]
    ///  C6 FIRST anomaly not chained                     anomalies [false,true]           → false  [Golden]
    ///  C7 LAST anomaly not chained                      anomalies [true,false]           → false  [Golden]
    ///  C8 !isFake filter                                fake free anomaly + real chained → true   [Golden]
    ///
    /// WMarginalIsChainedWin — team marginal; reads GetCharacter(ownerClientId), isFake,
    ///                         isChained.Value. (WMarginalIsChainedWin.cs:14-23) — zero prior coverage.
    ///  M1 owner not found (null) → early return         ownerClientId matches nobody     → false  [Golden]
    ///  M2 owner found but isFake → early return         owner is fake-id char            → false  [Golden]
    ///  M3 owner real, chained                           owner isChained=true             → true   [Golden]
    ///  M4 owner real, not chained                       owner isChained=false            → false  [Golden]
    ///
    /// WOmniscienceHackedCharacter — team marginal; reads GetCharacter(ownerClientId)
    ///                               (NO null guard), role.powers.Find(POmniscience),
    ///                               hackedCharacterClientId vs HACKED_CHARACTER_DEFAULT,
    ///                               GetCharacter(hackedId), hacked.isChained.Value,
    ///                               hacked.role.factionType==chosen.
    ///                               (WOmniscienceHackedCharacter.cs:15-36)
    ///  O1 owner not found (no null guard :17-18)        ownerClientId matches nobody     → THROWS NullReferenceException [Golden]
    ///  O2 owner found, no POmniscience in powers        owner role, empty powers         → false  [Golden]
    ///  O3 omni present, hackedId == DEFAULT             omni never targeted              → false  [Golden]
    ///  O4 omni present, hacked id set, hacked NOT found omni points nowhere              → false  [Golden]
    ///  O5 hacked found, NOT chained (isChained term)    hacked free, faction chosen      → false  [Golden]
    ///  O6 hacked found, chained, faction != chosen      hacked chained, faction anomaly  → false  [Golden]
    ///  O7 hacked found, chained, faction == chosen      hacked chained, faction chosen   → true   [Golden]
    ///
    /// ─────────────────────────────────────────────────────────────────────────────
    /// BRANCH-COVERAGE MAP (Task 6 — the real gate; every branch hit by ≥1 golden):
    ///   WAnomalyCorruption:
    ///     loop-body-never-runs           → A1
    ///     !isCorrupted == true  (return false) → A3, A5, A6
    ///     !isCorrupted == false (continue loop)→ A2, A4 (and the corrupted reals in A5/A6/A7)
    ///     !isFake filters out a fake     → A7
    ///   WChosenChainedAllAnomaly:
    ///     loop-body-never-runs           → C1
    ///     factionType != anomaly → continue→ C2, C5 (chosen member)
    ///     factionType == anomaly → eval    → C3,C4,C5,C6,C7,C8
    ///     !isChained == true  (return false)→ C4, C6, C7
    ///     !isChained == false (continue)   → C3, C5, C8 (and the chained anomalies in C6/C7)
    ///     !isFake filters out a fake       → C8
    ///   WMarginalIsChainedWin:
    ///     owner == null (return false)     → M1
    ///     owner.isFake (return false)      → M2
    ///     isChained.Value == true          → M3
    ///     isChained.Value == false         → M4
    ///   WOmniscienceHackedCharacter:
    ///     owner not found → deref null     → O1 (NRE)
    ///     omni == null (return false)      → O2
    ///     hackedId == DEFAULT (return false)→ O3
    ///     hacked == null (return false)    → O4 (double-lookup not-found)
    ///     isChained == false (term)        → O5
    ///     factionType != chosen (term)     → O6
    ///     isChained && factionType==chosen → O7 (the only true verdict)
    ///
    /// The binding target is BRANCH COVERAGE above, not the case count (~26). Adjust
    /// counts freely as long as every branch stays covered.
    /// </summary>
    [Category("GoldenMaster")]
    public class WinningConditionGoldenMasterTests
    {
        private GameObject _networkManagerGo;
        private NetworkManager _networkManager;
        private GameObject _gameManagerGo;
        private GameManager _gameManager;
        private GameObject _characterManagerGo;
        private CharacterManager _characterManager;

        private GameObject _dummyCharPrefab;

        // A fake client id (ulong.MaxValue - k, k in 0..MAX_PLAYERS) → IsFakeClientId() is true.
        private const ulong FakeClientId = GameValues.FAKE_CLIENT_ID - 1;

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

            _dummyCharPrefab = new GameObject("CharacterPrefab_Golden");
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

            // Load-bearing: domain reload may be disabled, statics survive across
            // PlayMode sessions — reset or a later test inherits a stale singleton.
            ReflectionHelper.SetPrivateField(typeof(GameManager), "instance", null);
            ReflectionHelper.SetPrivateField(typeof(CharacterManager), "instance", null);

            Object.Destroy(_gameManagerGo);
            Object.Destroy(_characterManagerGo);
            Object.Destroy(_networkManagerGo);
            Object.Destroy(_dummyCharPrefab);
            yield return null;
        }

        // Spawns a live POmniscience as a NetworkBehaviour (Power : NetworkBehaviour —
        // it cannot be new-ed). Mirrors the bare-NetworkObject spawn used for
        // CharactersParent in SetUp. The golden only needs CheckCondition() to traverse
        // the real role.powers.Find(...) + hackedCharacterClientId branches.
        private POmniscience SpawnOmniscience()
        {
            var go = new GameObject("POmniscience_Golden");
            go.AddComponent<NetworkObject>();
            var omni = go.AddComponent<POmniscience>();
            go.GetComponent<NetworkObject>().Spawn();
            return omni;
        }

        // ───────────────────────────── WAnomalyCorruption ─────────────────────────────

        [UnityTest]
        [Category("VacuousTruth")]
        public IEnumerator WAnomalyCorruption_ReturnsTrue_WhenPopulationIsEmpty() // A1
        {
            // No non-fake characters: the loop body never runs → vacuously true.
            yield return null;
            Assert.IsTrue(new WAnomalyCorruption().CheckCondition(),
                "WAnomalyCorruption is vacuously true over an empty non-fake population.");
        }

        [UnityTest]
        public IEnumerator WAnomalyCorruption_ReturnsTrue_WhenSingleCharacterCorrupted() // A2
        {
            Character c = _characterManager.AddNewCharacter(1);
            yield return NetworkTestHelper.WaitUntilAllSpawnedOrTimeout(c);

            c.isCorrupted.Value = true;

            Assert.IsTrue(new WAnomalyCorruption().CheckCondition(),
                "WAnomalyCorruption: single corrupted character → true.");
        }

        [UnityTest]
        public IEnumerator WAnomalyCorruption_ReturnsFalse_WhenSingleCharacterNotCorrupted() // A3
        {
            Character c = _characterManager.AddNewCharacter(1);
            yield return NetworkTestHelper.WaitUntilAllSpawnedOrTimeout(c);

            c.isCorrupted.Value = false;

            Assert.IsFalse(new WAnomalyCorruption().CheckCondition(),
                "WAnomalyCorruption: single not-corrupted character → false (early return).");
        }

        [UnityTest]
        public IEnumerator WAnomalyCorruption_ReturnsTrue_WhenAllOfManyCorrupted() // A4
        {
            Character c1 = _characterManager.AddNewCharacter(1);
            Character c2 = _characterManager.AddNewCharacter(2);
            Character c3 = _characterManager.AddNewCharacter(3);
            yield return NetworkTestHelper.WaitUntilAllSpawnedOrTimeout(c1, c2, c3);

            c1.isCorrupted.Value = true;
            c2.isCorrupted.Value = true;
            c3.isCorrupted.Value = true;

            Assert.IsTrue(new WAnomalyCorruption().CheckCondition(),
                "WAnomalyCorruption: all of n corrupted → true.");
        }

        [UnityTest]
        public IEnumerator WAnomalyCorruption_ReturnsFalse_WhenFirstOfManyNotCorrupted() // A5
        {
            Character c1 = _characterManager.AddNewCharacter(1);
            Character c2 = _characterManager.AddNewCharacter(2);
            Character c3 = _characterManager.AddNewCharacter(3);
            yield return NetworkTestHelper.WaitUntilAllSpawnedOrTimeout(c1, c2, c3);

            c1.isCorrupted.Value = false; // first
            c2.isCorrupted.Value = true;
            c3.isCorrupted.Value = true;

            Assert.IsFalse(new WAnomalyCorruption().CheckCondition(),
                "WAnomalyCorruption: first of n not corrupted → false (short-circuit position).");
        }

        [UnityTest]
        public IEnumerator WAnomalyCorruption_ReturnsFalse_WhenLastOfManyNotCorrupted() // A6
        {
            Character c1 = _characterManager.AddNewCharacter(1);
            Character c2 = _characterManager.AddNewCharacter(2);
            Character c3 = _characterManager.AddNewCharacter(3);
            yield return NetworkTestHelper.WaitUntilAllSpawnedOrTimeout(c1, c2, c3);

            c1.isCorrupted.Value = true;
            c2.isCorrupted.Value = true;
            c3.isCorrupted.Value = false; // last

            Assert.IsFalse(new WAnomalyCorruption().CheckCondition(),
                "WAnomalyCorruption: last of n not corrupted → false.");
        }

        [UnityTest]
        public IEnumerator WAnomalyCorruption_ReturnsTrue_WhenOnlyNonCorruptedIsFake() // A7
        {
            Character real = _characterManager.AddNewCharacter(1);
            Character fake = _characterManager.AddNewCharacter(FakeClientId);
            yield return NetworkTestHelper.WaitUntilAllSpawnedOrTimeout(real, fake);

            real.isCorrupted.Value = true;
            fake.isCorrupted.Value = false; // filtered out by !isFake

            Assert.IsTrue(new WAnomalyCorruption().CheckCondition(),
                "WAnomalyCorruption: a non-corrupted FAKE is filtered out → true (proves !isFake filter).");
        }

        // ────────────────────────── WChosenChainedAllAnomaly ──────────────────────────

        [UnityTest]
        [Category("VacuousTruth")]
        public IEnumerator WChosenChainedAllAnomaly_ReturnsTrue_WhenPopulationIsEmpty() // C1
        {
            // Empty non-fake population — loop never runs → vacuously true.
            yield return null;
            Assert.IsTrue(new WChosenChainedAllAnomaly().CheckCondition(),
                "WChosenChainedAllAnomaly is vacuously true over an empty non-fake population.");
        }

        [UnityTest]
        [Category("VacuousTruth")]
        public IEnumerator WChosenChainedAllAnomaly_ReturnsTrue_WhenNoAnomalyInPopulation() // C2
        {
            // Empty relevant subset: every member is continue'd (no anomaly faction) →
            // "all anomalies chained" holds vacuously with zero anomalies. Distinct from C1.
            Character chosen = _characterManager.AddNewCharacter(1);
            Character marginal = _characterManager.AddNewCharacter(2);
            yield return NetworkTestHelper.WaitUntilAllSpawnedOrTimeout(chosen, marginal);

            chosen.role = new Role { factionType = FactionType.chosen };
            marginal.role = new Role { factionType = FactionType.marginal };

            Assert.IsTrue(new WChosenChainedAllAnomaly().CheckCondition(),
                "WChosenChainedAllAnomaly is vacuously true when no member is an anomaly (all continue'd).");
        }

        [UnityTest]
        public IEnumerator WChosenChainedAllAnomaly_ReturnsTrue_WhenSingleAnomalyChained() // C3
        {
            Character anomaly = _characterManager.AddNewCharacter(1);
            yield return NetworkTestHelper.WaitUntilAllSpawnedOrTimeout(anomaly);

            anomaly.role = new Role { factionType = FactionType.anomaly };
            anomaly.isChained.Value = true;

            Assert.IsTrue(new WChosenChainedAllAnomaly().CheckCondition(),
                "WChosenChainedAllAnomaly: single chained anomaly → true.");
        }

        [UnityTest]
        public IEnumerator WChosenChainedAllAnomaly_ReturnsFalse_WhenSingleAnomalyNotChained() // C4
        {
            Character anomaly = _characterManager.AddNewCharacter(1);
            yield return NetworkTestHelper.WaitUntilAllSpawnedOrTimeout(anomaly);

            anomaly.role = new Role { factionType = FactionType.anomaly };
            anomaly.isChained.Value = false;

            Assert.IsFalse(new WChosenChainedAllAnomaly().CheckCondition(),
                "WChosenChainedAllAnomaly: single free anomaly → false (early return).");
        }

        [UnityTest]
        public IEnumerator WChosenChainedAllAnomaly_ReturnsTrue_WhenChosenContinuedAndAllAnomaliesChained() // C5
        {
            Character chosen = _characterManager.AddNewCharacter(1);
            Character anomaly1 = _characterManager.AddNewCharacter(2);
            Character anomaly2 = _characterManager.AddNewCharacter(3);
            yield return NetworkTestHelper.WaitUntilAllSpawnedOrTimeout(chosen, anomaly1, anomaly2);

            chosen.role = new Role { factionType = FactionType.chosen };
            anomaly1.role = new Role { factionType = FactionType.anomaly };
            anomaly2.role = new Role { factionType = FactionType.anomaly };

            anomaly1.isChained.Value = true;
            anomaly2.isChained.Value = true;

            Assert.IsTrue(new WChosenChainedAllAnomaly().CheckCondition(),
                "WChosenChainedAllAnomaly: chosen continue'd + all anomalies chained → true.");
        }

        [UnityTest]
        public IEnumerator WChosenChainedAllAnomaly_ReturnsFalse_WhenFirstAnomalyNotChained() // C6
        {
            Character anomaly1 = _characterManager.AddNewCharacter(1);
            Character anomaly2 = _characterManager.AddNewCharacter(2);
            yield return NetworkTestHelper.WaitUntilAllSpawnedOrTimeout(anomaly1, anomaly2);

            anomaly1.role = new Role { factionType = FactionType.anomaly };
            anomaly2.role = new Role { factionType = FactionType.anomaly };

            anomaly1.isChained.Value = false; // first
            anomaly2.isChained.Value = true;

            Assert.IsFalse(new WChosenChainedAllAnomaly().CheckCondition(),
                "WChosenChainedAllAnomaly: first anomaly not chained → false.");
        }

        [UnityTest]
        public IEnumerator WChosenChainedAllAnomaly_ReturnsFalse_WhenLastAnomalyNotChained() // C7
        {
            Character anomaly1 = _characterManager.AddNewCharacter(1);
            Character anomaly2 = _characterManager.AddNewCharacter(2);
            yield return NetworkTestHelper.WaitUntilAllSpawnedOrTimeout(anomaly1, anomaly2);

            anomaly1.role = new Role { factionType = FactionType.anomaly };
            anomaly2.role = new Role { factionType = FactionType.anomaly };

            anomaly1.isChained.Value = true;
            anomaly2.isChained.Value = false; // last

            Assert.IsFalse(new WChosenChainedAllAnomaly().CheckCondition(),
                "WChosenChainedAllAnomaly: last anomaly not chained → false.");
        }

        [UnityTest]
        public IEnumerator WChosenChainedAllAnomaly_ReturnsTrue_WhenOnlyUnchainedAnomalyIsFake() // C8
        {
            Character realAnomaly = _characterManager.AddNewCharacter(1);
            Character fakeAnomaly = _characterManager.AddNewCharacter(FakeClientId);
            yield return NetworkTestHelper.WaitUntilAllSpawnedOrTimeout(realAnomaly, fakeAnomaly);

            realAnomaly.role = new Role { factionType = FactionType.anomaly };
            // fakeAnomaly is filtered by !isFake before role/isChained are read; no role needed.
            realAnomaly.isChained.Value = true;
            fakeAnomaly.isChained.Value = false;

            Assert.IsTrue(new WChosenChainedAllAnomaly().CheckCondition(),
                "WChosenChainedAllAnomaly: an unchained FAKE anomaly is filtered out → true (proves !isFake filter).");
        }

        // ─────────────────────────── WMarginalIsChainedWin ────────────────────────────

        [UnityTest]
        public IEnumerator WMarginalIsChainedWin_ReturnsFalse_WhenOwnerNotFound() // M1
        {
            // ownerClientId 999 matches no character → GetCharacter returns null → false (null guard).
            yield return null;
            var condition = new WMarginalIsChainedWin { ownerClientId = 999 };
            Assert.IsFalse(condition.CheckCondition(),
                "WMarginalIsChainedWin: owner not found → false (null-guarded).");
        }

        [UnityTest]
        public IEnumerator WMarginalIsChainedWin_ReturnsFalse_WhenOwnerIsFake() // M2
        {
            Character fakeOwner = _characterManager.AddNewCharacter(FakeClientId);
            yield return NetworkTestHelper.WaitUntilAllSpawnedOrTimeout(fakeOwner);

            fakeOwner.isChained.Value = true; // even chained, a fake owner cannot win

            var condition = new WMarginalIsChainedWin { ownerClientId = FakeClientId };
            Assert.IsFalse(condition.CheckCondition(),
                "WMarginalIsChainedWin: fake owner → false (isFake early return).");
        }

        [UnityTest]
        public IEnumerator WMarginalIsChainedWin_ReturnsTrue_WhenOwnerRealAndChained() // M3
        {
            Character owner = _characterManager.AddNewCharacter(1);
            yield return NetworkTestHelper.WaitUntilAllSpawnedOrTimeout(owner);

            owner.isChained.Value = true;

            var condition = new WMarginalIsChainedWin { ownerClientId = 1 };
            Assert.IsTrue(condition.CheckCondition(),
                "WMarginalIsChainedWin: real chained owner → true.");
        }

        [UnityTest]
        public IEnumerator WMarginalIsChainedWin_ReturnsFalse_WhenOwnerRealAndNotChained() // M4
        {
            Character owner = _characterManager.AddNewCharacter(1);
            yield return NetworkTestHelper.WaitUntilAllSpawnedOrTimeout(owner);

            owner.isChained.Value = false;

            var condition = new WMarginalIsChainedWin { ownerClientId = 1 };
            Assert.IsFalse(condition.CheckCondition(),
                "WMarginalIsChainedWin: real owner not chained → false.");
        }

        // ──────────────────────── WOmniscienceHackedCharacter ─────────────────────────

        [UnityTest]
        public IEnumerator WOmniscienceHackedCharacter_Throws_WhenOwnerNotFound() // O1
        {
            // GOLDEN AS-IS: no owner null guard at :17-18 → _ownerCharacter.role dereferences null.
            // Captured as a throw so an Epic-2 change to a silent false goes red (reading note 3).
            yield return null;
            var condition = new WOmniscienceHackedCharacter { ownerClientId = 999 };
            Assert.Throws<System.NullReferenceException>(() => condition.CheckCondition(),
                "WOmniscienceHackedCharacter: owner not found throws NullReferenceException (no null guard — golden as-is).");
        }

        [UnityTest]
        public IEnumerator WOmniscienceHackedCharacter_ReturnsFalse_WhenOwnerHasNoOmniscience() // O2
        {
            Character owner = _characterManager.AddNewCharacter(1);
            yield return NetworkTestHelper.WaitUntilAllSpawnedOrTimeout(owner);

            owner.role = new Role(); // empty powers → Find(POmniscience) is null

            var condition = new WOmniscienceHackedCharacter { ownerClientId = 1 };
            Assert.IsFalse(condition.CheckCondition(),
                "WOmniscienceHackedCharacter: owner without POmniscience → false.");
        }

        [UnityTest]
        public IEnumerator WOmniscienceHackedCharacter_ReturnsFalse_WhenHackedIdIsDefault() // O3
        {
            Character owner = _characterManager.AddNewCharacter(1);
            yield return NetworkTestHelper.WaitUntilAllSpawnedOrTimeout(owner);

            POmniscience omni = SpawnOmniscience();
            yield return NetworkTestHelper.WaitUntilAllSpawnedOrTimeout(omni);

            owner.role = new Role();
            owner.role.powers.Add(omni);
            // hackedCharacterClientId left at HACKED_CHARACTER_DEFAULT (never targeted).

            var condition = new WOmniscienceHackedCharacter { ownerClientId = 1 };
            Assert.IsFalse(condition.CheckCondition(),
                "WOmniscienceHackedCharacter: omniscience present but never targeted (DEFAULT) → false.");
        }

        [UnityTest]
        public IEnumerator WOmniscienceHackedCharacter_ReturnsFalse_WhenHackedCharacterNotFound() // O4
        {
            Character owner = _characterManager.AddNewCharacter(1);
            yield return NetworkTestHelper.WaitUntilAllSpawnedOrTimeout(owner);

            POmniscience omni = SpawnOmniscience();
            yield return NetworkTestHelper.WaitUntilAllSpawnedOrTimeout(omni);

            owner.role = new Role();
            owner.role.powers.Add(omni);
            omni.hackedCharacterClientId = 777; // no character with this id (double-lookup not-found)

            var condition = new WOmniscienceHackedCharacter { ownerClientId = 1 };
            Assert.IsFalse(condition.CheckCondition(),
                "WOmniscienceHackedCharacter: hacked target not found → false (double-lookup not-found branch).");
        }

        [UnityTest]
        public IEnumerator WOmniscienceHackedCharacter_ReturnsFalse_WhenHackedNotChained() // O5
        {
            Character owner = _characterManager.AddNewCharacter(1);
            Character hacked = _characterManager.AddNewCharacter(2);
            yield return NetworkTestHelper.WaitUntilAllSpawnedOrTimeout(owner, hacked);

            POmniscience omni = SpawnOmniscience();
            yield return NetworkTestHelper.WaitUntilAllSpawnedOrTimeout(omni);

            owner.role = new Role();
            owner.role.powers.Add(omni);
            omni.hackedCharacterClientId = 2;

            hacked.role = new Role { factionType = FactionType.chosen };
            hacked.isChained.Value = false; // isChained term fails

            var condition = new WOmniscienceHackedCharacter { ownerClientId = 1 };
            Assert.IsFalse(condition.CheckCondition(),
                "WOmniscienceHackedCharacter: hacked chosen but NOT chained → false (isChained term).");
        }

        [UnityTest]
        public IEnumerator WOmniscienceHackedCharacter_ReturnsFalse_WhenHackedFactionNotChosen() // O6
        {
            Character owner = _characterManager.AddNewCharacter(1);
            Character hacked = _characterManager.AddNewCharacter(2);
            yield return NetworkTestHelper.WaitUntilAllSpawnedOrTimeout(owner, hacked);

            POmniscience omni = SpawnOmniscience();
            yield return NetworkTestHelper.WaitUntilAllSpawnedOrTimeout(omni);

            owner.role = new Role();
            owner.role.powers.Add(omni);
            omni.hackedCharacterClientId = 2;

            hacked.role = new Role { factionType = FactionType.anomaly }; // factionType term fails
            hacked.isChained.Value = true;

            var condition = new WOmniscienceHackedCharacter { ownerClientId = 1 };
            Assert.IsFalse(condition.CheckCondition(),
                "WOmniscienceHackedCharacter: hacked chained but faction != chosen → false (factionType term).");
        }

        [UnityTest]
        public IEnumerator WOmniscienceHackedCharacter_ReturnsTrue_WhenHackedChainedAndChosen() // O7
        {
            Character owner = _characterManager.AddNewCharacter(1);
            Character hacked = _characterManager.AddNewCharacter(2);
            yield return NetworkTestHelper.WaitUntilAllSpawnedOrTimeout(owner, hacked);

            POmniscience omni = SpawnOmniscience();
            yield return NetworkTestHelper.WaitUntilAllSpawnedOrTimeout(omni);

            owner.role = new Role();
            owner.role.powers.Add(omni);
            omni.hackedCharacterClientId = 2;

            hacked.role = new Role { factionType = FactionType.chosen };
            hacked.isChained.Value = true;

            var condition = new WOmniscienceHackedCharacter { ownerClientId = 1 };
            Assert.IsTrue(condition.CheckCondition(),
                "WOmniscienceHackedCharacter: hacked chained AND chosen → true (the only win verdict).");
        }
    }
}
