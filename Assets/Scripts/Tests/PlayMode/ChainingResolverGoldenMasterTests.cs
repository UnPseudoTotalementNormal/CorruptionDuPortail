using System.Collections;
using System.Collections.Generic;
using GameLogic;
using NUnit.Framework;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.TestTools;

namespace Tests.PlayMode
{
    /// <summary>
    /// Characterization golden masters for the CURRENT "ChainingResolver" behavior (Story 1.5, PR A).
    /// The resolver-as-it-exists-today is <see cref="ChainingManager.AddCharacterToChainingList"/>: server-only
    /// membership management with dedup-by-Contains. These goldens pin the current verdict as-is so the Epic 2
    /// extraction of `ChainingResolver` is behavior-preserving by proof.
    ///
    /// PROPERTY NOTE (ordering contract for Epic 2 — AC line 184):
    ///   - Membership is ORDER-INDIFFERENT: adding {A,B,C} in any permutation yields the same membership set.
    ///     This MUST hold — the chained set drives Character.isChained, read by WMarginal/WChosen/WOmniscience.
    ///   - The list also currently preserves INSERTION ORDER (first-occurrence sequence). This sequence is
    ///     observable but NO winning condition reads list position — they read isChained per character. So Epic 2
    ///     MAY treat membership as a set; if it changes list ordering it must prove no consumer reads the position.
    ///   - Dedup: a clientId already present is never added twice.
    ///
    /// Reuses the ChainingManagerTests host harness verbatim (Story 1.0 proved it drives production logic).
    /// </summary>
    public class ChainingResolverGoldenMasterTests
    {
        private GameObject _networkManagerGo;
        private NetworkManager _networkManager;
        private GameObject _chainingManagerGo;
        private ChainingManager _chainingManager;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            _networkManagerGo = new GameObject("NetworkManager");
            _networkManager = _networkManagerGo.AddComponent<NetworkManager>();
            _networkManager.NetworkConfig = new NetworkConfig
            {
                NetworkTransport = _networkManagerGo.AddComponent<Unity.Netcode.Transports.UTP.UnityTransport>()
            };
            Assert.IsTrue(_networkManager.StartHost(), "NGO StartHost() failed — server did not start.");

            _chainingManagerGo = new GameObject("ChainingManager");
            var netObj = _chainingManagerGo.AddComponent<NetworkObject>();
            _chainingManager = _chainingManagerGo.AddComponent<ChainingManager>();
            netObj.Spawn();

            yield return null;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (_networkManager != null && _networkManager.IsListening)
            {
                _networkManager.Shutdown();
            }
            yield return NetworkTestHelper.WaitUntilOrTimeout(() => _networkManager == null || !_networkManager.IsListening, 5f, "NGO did not stop listening within 5s after Shutdown().");

            Object.Destroy(_chainingManagerGo);
            Object.Destroy(_networkManagerGo);
            yield return null;
        }

        private List<ulong> Membership()
        {
            var list = new List<ulong>();
            foreach (var id in _chainingManager.chainingPlayers)
            {
                list.Add(id);
            }
            return list;
        }

        private void AddAll(params ulong[] ids)
        {
            foreach (var id in ids)
            {
                _chainingManager.AddCharacterToChainingList(id);
            }
        }

        // --- Order-indifference of MEMBERSHIP (the must-hold property) ---

        [UnityTest]
        [Category("GoldenMaster")]
        [Category("ChainingResolver")]
        public IEnumerator AddInForwardOrder_MembershipIsABC()
        {
            AddAll(10, 20, 30);
            CollectionAssert.AreEquivalent(new ulong[] { 10, 20, 30 }, Membership());
            yield return null;
        }

        [UnityTest]
        [Category("GoldenMaster")]
        [Category("ChainingResolver")]
        public IEnumerator AddInReverseOrder_SameMembershipSet()
        {
            AddAll(30, 20, 10);
            CollectionAssert.AreEquivalent(new ulong[] { 10, 20, 30 }, Membership());
            yield return null;
        }

        [UnityTest]
        [Category("GoldenMaster")]
        [Category("ChainingResolver")]
        public IEnumerator AddInShuffledOrder_SameMembershipSet()
        {
            AddAll(20, 10, 30);
            CollectionAssert.AreEquivalent(new ulong[] { 10, 20, 30 }, Membership());
            yield return null;
        }

        // --- Insertion-order sequence (currently observable, pinned as-is) ---

        [UnityTest]
        [Category("GoldenMaster")]
        [Category("ChainingResolver")]
        public IEnumerator List_PreservesFirstOccurrenceInsertionOrder()
        {
            AddAll(10, 20, 30);
            // Sequence-sensitive assert: pins the CURRENT insertion-order behavior.
            Assert.AreEqual(new List<ulong> { 10, 20, 30 }, Membership());
            yield return null;
        }

        // --- Dedup ---

        [UnityTest]
        [Category("GoldenMaster")]
        [Category("ChainingResolver")]
        public IEnumerator Dedup_InterleavedDuplicates_MembershipIsDistinct()
        {
            AddAll(10, 20, 10, 30, 20);
            CollectionAssert.AreEquivalent(new ulong[] { 10, 20, 30 }, Membership());
            Assert.AreEqual(3, _chainingManager.chainingPlayers.Count);
            yield return null;
        }

        // --- Vacuous: empty resolution ---

        [UnityTest]
        [Category("GoldenMaster")]
        [Category("VacuousTruth")]
        [Category("ChainingResolver")]
        public IEnumerator NoAdds_MembershipIsEmpty()
        {
            Assert.AreEqual(0, _chainingManager.chainingPlayers.Count);
            yield return null;
        }
    }
}
