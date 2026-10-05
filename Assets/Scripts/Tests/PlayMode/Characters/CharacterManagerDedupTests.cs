using System.Collections;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using Characters;
using NUnit.Framework;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.TestTools;

namespace Tests.PlayMode
{
    /// <summary>
    /// Regression net for the technomancer-duplicate-card incident (2026-07-03,
    /// investigations/technomancer-duplicate-card-investigation.md): a client's
    /// replicated <c>networkedCharacters</c> NetworkList held the same entry twice
    /// (NGO initial-sync + pending-delta double delivery), so every UI surface
    /// projecting <c>GetCharacters()</c> showed a phantom duplicate player.
    ///
    /// The fix dedups by resolved Character reference inside
    /// <c>RebuildCharactersCache</c> and fires a loud [CHARLIST] tripwire log.
    /// These tests inject the replica-level duplicate directly into the private
    /// NetworkList (reflection — no public writer can produce it by design) and
    /// pin both the self-healing projection and the untouched healthy path.
    /// </summary>
    [Category("CharacterManagerDedup")]
    public class CharacterManagerDedupTests
    {
        private GameObject _networkManagerGo;
        private NetworkManager _networkManager;
        private GameObject _characterManagerGo;
        private CharacterManager _characterManager;
        private GameObject _charactersParentGo;
        private GameObject _dummyCharPrefab;

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

            _dummyCharPrefab = new GameObject("CharacterPrefab_Dedup");
            _dummyCharPrefab.AddComponent<NetworkObject>();
            _dummyCharPrefab.AddComponent<Character>();
            _networkManager.NetworkConfig.Prefabs.Add(new NetworkPrefab { Prefab = _dummyCharPrefab });

            Assert.IsTrue(_networkManager.StartHost(), "NGO StartHost() failed — server did not start.");

            _characterManagerGo = new GameObject("CharacterManager");
            _characterManagerGo.AddComponent<NetworkObject>();
            _characterManager = _characterManagerGo.AddComponent<CharacterManager>();

            _charactersParentGo = new GameObject("CharactersParent");
            _charactersParentGo.AddComponent<NetworkObject>().Spawn();
            ReflectionHelper.SetPrivateField(_characterManager, "_charactersParent", _charactersParentGo.transform);
            ReflectionHelper.SetPrivateField(_characterManager, "_characterPrefab", _dummyCharPrefab.GetComponent<NetworkObject>());
            _characterManager.GetComponent<NetworkObject>().Spawn();

            yield return NetworkTestHelper.WaitUntilAllSpawnedOrTimeout(_characterManager);
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (_networkManager != null && _networkManager.IsListening) _networkManager.Shutdown();
            yield return NetworkTestHelper.WaitUntilOrTimeout(() => _networkManager == null || !_networkManager.IsListening, 5f, "NGO did not stop listening within 5s after Shutdown().");

            CharacterManager.instance = null;

            // Null-guarded: a SetUp assert can fail before the later objects exist.
            if (_characterManagerGo != null) Object.Destroy(_characterManagerGo);
            if (_charactersParentGo != null) Object.Destroy(_charactersParentGo);
            if (_networkManagerGo != null) Object.Destroy(_networkManagerGo);
            if (_dummyCharPrefab != null) Object.Destroy(_dummyCharPrefab);
            yield return null;
        }

        // The replica duplicate cannot be produced through AddNewCharacter (clientId
        // guard) — it only ever appears when NGO delivers the same list entry twice
        // to a client. Reproduce that exact end state on the private list.
        // NET-04: the list is now a full-value NetworkVariable<NetworkObjectIdList> (unique ids by construction), so
        // the corrupt source is reproduced by assigning a raw id list that holds the same id twice.
        private NetworkVariable<Network.NetworkObjectIdList> GetNetworkedCharacters()
        {
            var _field = typeof(CharacterManager).GetField("networkedCharacters", BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(_field, "CharacterManager.networkedCharacters field not found — fix the test if it was renamed.");
            return (NetworkVariable<Network.NetworkObjectIdList>)_field.GetValue(_characterManager);
        }

        [UnityTest]
        public IEnumerator GetCharacters_DropsDuplicateReplicaEntry_AndFiresTripwire()
        {
            Character _character = _characterManager.AddNewCharacter(_networkManager.LocalClientId);
            yield return NetworkTestHelper.WaitUntilSpawnedOrTimeout(_character);

            LogAssert.Expect(LogType.Error, new Regex(@"\[CHARLIST\]"));

            // Simulate the NGO replica divergence: the same entry present twice.
            GetNetworkedCharacters().Value =
                Network.NetworkObjectIdList.FromRawForTests(_character.NetworkObjectId, _character.NetworkObjectId);
            ReflectionHelper.SetPrivateField(_characterManager, "_cacheDirty", true);

            var _characters = _characterManager.GetCharacters(false);

            Assert.AreEqual(1, _characters.Count, "A duplicated NetworkList entry must be projected exactly once.");
            Assert.AreSame(_character, _characters[0]);

            // The duplicate persists in the list for the session: a second dirty read
            // must still project once and must NOT log again (an unexpected second
            // LogError fails the test by default — pins the once-per-duplicate tripwire).
            ReflectionHelper.SetPrivateField(_characterManager, "_cacheDirty", true);
            var _secondRead = _characterManager.GetCharacters(false);
            Assert.AreEqual(1, _secondRead.Count);
        }

        [UnityTest]
        public IEnumerator GetCharacters_HealthyList_KeepsCountAndOrder_NoLog()
        {
            Character _first = _characterManager.AddNewCharacter(_networkManager.LocalClientId);
            Character _second = _characterManager.AddNewCharacter(111);
            Character _third = _characterManager.AddNewCharacter(222);
            yield return NetworkTestHelper.WaitUntilAllSpawnedOrTimeout(_first, _second, _third);

            ReflectionHelper.SetPrivateField(_characterManager, "_cacheDirty", true);
            var _characters = _characterManager.GetCharacters(false);

            // Byte-identical healthy path: same count, insertion order preserved,
            // and no [CHARLIST] log (an unexpected LogError fails the test by default).
            Assert.AreEqual(3, _characters.Count);
            Assert.AreSame(_first, _characters[0]);
            Assert.AreSame(_second, _characters[1]);
            Assert.AreSame(_third, _characters[2]);
            Assert.AreEqual(3, _characters.Distinct().Count());
        }
    }
}
