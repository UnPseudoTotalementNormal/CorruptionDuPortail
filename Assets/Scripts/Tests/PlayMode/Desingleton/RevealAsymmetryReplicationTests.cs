using System.Collections;
using System.Collections.Generic;
using Characters;
using GameLogic;
using NUnit.Framework;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.TestTools;

namespace Tests.PlayMode.Desingleton
{
    /// <summary>
    /// 2-NM E2E — GameInfoRevealer's reveal ASYMMETRY over the real wire. The reveal store is a plain
    /// per-process dictionary fed by TARGETED RPCs (SendRevealLevelRpc → RpcTarget.Single(observer)):
    /// a Personal reveal must land in the intended observer's store and in NO other client's. Under
    /// StartHost there is exactly ONE revealer, so a "reveal leaked to everyone" bug (the
    /// CursedVision/Embrace class of regression) is structurally invisible — only a real second client
    /// with its own replica can witness both sides of the asymmetry.
    ///
    /// Infrastructure: a REAL replicated GameInfoRevealer (the RPCs need a spawned NetworkObject).
    /// Its Start() asserts scene-wired characterManager/gameManager, so the template carries INACTIVE
    /// placeholder components (Awake never runs — no singleton claims) that every instance copies;
    /// after spawn each side's characterManager is rewired to its OWN NM's CharacterManager so
    /// GetLocalClientId answers per-NM (the observer identity the store keys UI decisions on).
    /// </summary>
    public class RevealAsymmetryReplicationTests : MultiClientGameFixture
    {
        private const uint RevealerPrefabHash = 0xC0DE0401u;
        private const ulong SubjectSeat = 999UL;

        private GameObject _revealerPrefabGo;
        private NetworkObject _revealerPrefabNo;
        private GameObject _placeholderGo;

        private GameInfoRevealer _hostRevealer;
        private GameInfoRevealer _clientRevealer;

        protected override void BuildExtraNetworkPrefabs(List<GameObject> _templates)
        {
            // Inactive placeholder GO: components exist (non-null for Start()'s asserts on every
            // instance) but Awake/Start never run, so no static instance is ever claimed by them.
            _placeholderGo = new GameObject("RevealPlaceholders");
            _placeholderGo.SetActive(false);
            var _placeholderCm = _placeholderGo.AddComponent<CharacterManager>();
            var _placeholderGm = _placeholderGo.AddComponent<GameManager>();

            _revealerPrefabGo = new GameObject("RevealerPrefab");
            _revealerPrefabNo = _revealerPrefabGo.AddComponent<NetworkObject>();
            SetGlobalObjectIdHash(_revealerPrefabNo, RevealerPrefabHash);
            MarkAsNonSceneObject(_revealerPrefabNo);
            var _revealerTemplate = _revealerPrefabGo.AddComponent<GameInfoRevealer>();
            ReflectionHelper.SetPrivateField(_revealerTemplate, "characterManager", _placeholderCm);
            ReflectionHelper.SetPrivateField(_revealerTemplate, "gameManager", _placeholderGm);
            _templates.Add(_revealerPrefabGo);
        }

        [UnityTearDown]
        public IEnumerator RevealTearDown()
        {
            // Despawn while the NMs still listen, then base teardown proceeds untouched.
            if (HostNm != null && HostNm.IsListening && _hostRevealer != null && _hostRevealer.IsSpawned)
            {
                _hostRevealer.NetworkObject.Despawn(true);
            }
            yield return null;
            if (_placeholderGo != null) Object.Destroy(_placeholderGo);
            _hostRevealer = null;
            _clientRevealer = null;
        }

        /// <summary>Spawn the subject Character + the replicated revealer, resolve the client replica,
        /// and rewire each side's characterManager to its OWN NM's manager.</summary>
        private IEnumerator SpawnRevealerWorld()
        {
            yield return SpawnRealCharacterForClient(SubjectSeat);
            yield return NetworkTestHelper.WaitUntilOrTimeout(
                () => ClientCm.GetCharacter(SubjectSeat, false) != null,
                5f,
                "The client CM never projected the subject seat.");

            _hostRevealer = HostNm.SpawnManager.InstantiateAndSpawn(_revealerPrefabNo, destroyWithScene: true)
                .GetComponent<GameInfoRevealer>();
            yield return NetworkTestHelper.WaitUntilSpawnedOrTimeout(_hostRevealer, 5f);
            ulong _netId = _hostRevealer.NetworkObjectId;
            yield return NetworkTestHelper.WaitUntilOrTimeout(
                () => ClientNm.SpawnManager.SpawnedObjects.ContainsKey(_netId),
                5f,
                "Client NM never spawned its GameInfoRevealer replica.");
            _clientRevealer = ClientNm.SpawnManager.SpawnedObjects[_netId].GetComponent<GameInfoRevealer>();
            Assert.AreNotSame(_hostRevealer, _clientRevealer, "Revealer replicas must be distinct objects.");

            ReflectionHelper.SetPrivateField(_hostRevealer, "characterManager", HostCm);
            ReflectionHelper.SetPrivateField(_clientRevealer, "characterManager", ClientCm);
        }

        // --- 1. Personal reveal targeted at the REMOTE observer: lands in the CLIENT's store, and the
        // HOST's own store stays False (the reveal was routed, not broadcast). ---
        [UnityTest]
        public IEnumerator PersonalReveal_ToRemoteObserver_LandsOnClientRevealerOnly()
        {
            yield return SpawnRevealerWorld();
            ulong _observer = ClientNm.LocalClientId;

            Assert.AreEqual(RevealLevel.False,
                _clientRevealer.GetCharacterInfo(SubjectSeat, _observer).isRoleRevealed,
                "Precondition: client store starts with no role reveal.");

            _hostRevealer.SendRevealLevelRpc(SubjectSeat,
                nameof(CharacterInfoReveal.isRoleRevealed), RevealLevel.Personal, _observer, false);

            yield return NetworkTestHelper.WaitUntilOrTimeout(
                () => _clientRevealer.GetCharacterInfo(SubjectSeat, _observer).isRoleRevealed == RevealLevel.Personal,
                5f,
                "The Personal reveal never reached the remote observer's revealer replica.");

            Assert.AreEqual(RevealLevel.False,
                _hostRevealer.GetCharacterInfo(SubjectSeat, HostNm.LocalClientId).isRoleRevealed,
                "A reveal targeted at the remote observer must NOT land in the host's own store.");
        }

        // --- 2. The mirror: a Personal reveal targeted at the HOST must NOT leak to the remote client.
        // A later marker reveal TO the client proves the wire delivered everything sent its way before
        // the negative assertion is read. ---
        [UnityTest]
        public IEnumerator PersonalReveal_ToHostObserver_DoesNotLeakToRemoteClientRevealer()
        {
            yield return SpawnRevealerWorld();
            ulong _hostObserver = HostNm.LocalClientId;
            ulong _clientObserver = ClientNm.LocalClientId;

            _hostRevealer.SendRevealLevelRpc(SubjectSeat,
                nameof(CharacterInfoReveal.isRoleRevealed), RevealLevel.Personal, _hostObserver, false);
            yield return NetworkTestHelper.WaitUntilOrTimeout(
                () => _hostRevealer.GetCharacterInfo(SubjectSeat, _hostObserver).isRoleRevealed == RevealLevel.Personal,
                5f,
                "The host-targeted reveal never landed in the host's store.");

            // Marker on a DIFFERENT field so it cannot mask the negative below.
            _hostRevealer.SendRevealLevelRpc(SubjectSeat,
                nameof(CharacterInfoReveal.isCorruptRevealed), RevealLevel.Personal, _clientObserver, false);
            yield return NetworkTestHelper.WaitUntilOrTimeout(
                () => _clientRevealer.GetCharacterInfo(SubjectSeat, _clientObserver).isCorruptRevealed == RevealLevel.Personal,
                5f,
                "The marker reveal never reached the client — cannot conclude on the negative.");

            Assert.AreEqual(RevealLevel.False,
                _clientRevealer.GetCharacterInfo(SubjectSeat, _clientObserver).isRoleRevealed,
                "A host-targeted Personal reveal LEAKED to the remote client's revealer.");
        }

        // --- 3. Public reveal fans out SendTo.Everyone: BOTH stores converge to Public. ---
        [UnityTest]
        public IEnumerator PublicReveal_FansOutToBothRevealerStores()
        {
            yield return SpawnRevealerWorld();

            _hostRevealer.SetRevealLevelRpc(SubjectSeat,
                nameof(CharacterInfoReveal.isRoleRevealed), RevealLevel.Public, false);

            yield return NetworkTestHelper.WaitUntilOrTimeout(
                () => _clientRevealer.GetCharacterInfo(SubjectSeat, ClientNm.LocalClientId).isRoleRevealed == RevealLevel.Public,
                5f,
                "The Public reveal never fanned out to the remote client's revealer replica.");
            Assert.AreEqual(RevealLevel.Public,
                _hostRevealer.GetCharacterInfo(SubjectSeat, HostNm.LocalClientId).isRoleRevealed,
                "The Public reveal must also land in the host's store.");
        }

        // --- 4. The hack marker (POmniscience) is Personal-scoped AND clearable: the targeted clear RPC
        // must flip the remote observer's store back to False — a downgrade the monotonic reveal path
        // cannot do, via its own dedicated route, across the wire. ---
        [UnityTest]
        public IEnumerator HackReveal_ThenTargetedClear_RoundTripsOnRemoteObserver()
        {
            yield return SpawnRevealerWorld();
            ulong _observer = ClientNm.LocalClientId;

            _hostRevealer.SendRevealLevelRpc(SubjectSeat,
                nameof(CharacterInfoReveal.isHacked), RevealLevel.Personal, _observer, false);
            yield return NetworkTestHelper.WaitUntilOrTimeout(
                () => _clientRevealer.GetCharacterInfo(SubjectSeat, _observer).isHacked == RevealLevel.Personal,
                5f,
                "The hack marker never reached the remote observer's revealer replica.");
            Assert.AreEqual(RevealLevel.False,
                _hostRevealer.GetCharacterInfo(SubjectSeat, HostNm.LocalClientId).isHacked,
                "The hack marker must not land in the host's store.");

            _hostRevealer.SendClearHackedRpc(SubjectSeat, _observer);
            yield return NetworkTestHelper.WaitUntilOrTimeout(
                () => _clientRevealer.GetCharacterInfo(SubjectSeat, _observer).isHacked == RevealLevel.False,
                5f,
                "The targeted hack-clear never round-tripped to the remote observer's replica.");
        }
    }
}
