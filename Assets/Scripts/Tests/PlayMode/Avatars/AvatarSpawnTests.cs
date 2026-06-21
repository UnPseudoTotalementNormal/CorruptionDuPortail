using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using Avatars;
using NUnit.Framework;
using Unity.Netcode;
using Unity.Netcode.Components;
using Unity.Netcode.Transports.UTP;
using UnityEngine;
using UnityEngine.TestTools;

namespace Tests.PlayMode.Avatars
{
    /// <summary>
    /// Story 13.1 (Epic 13 — Player Embodiment) — multi-client proof of the avatar foundation. Built on
    /// the MultiClientGameFixture substrate (MultiClientGameFixture.cs:115–166, 352–387): a host plus ONE
    /// real in-process client over a UnityTransport loopback (two NetworkManagers in one process). This is
    /// a trimmed, self-contained variant — it registers the AVATAR prefab + spawns the AvatarManager on the
    /// host the same way the fixture registers/spawns GM/CM, and needs no GameManager/CharacterManager
    /// (the avatar layer depends on neither for spawning).
    ///
    /// Proves (AC #7): an avatar spawns for each REAL client, is visible on the remote replica, despawns
    /// cleanly on demand, and NO avatar is spawned for a simulated bot (clientId &gt;= 100, AC #6).
    ///
    /// DOCUMENTED EXCEPTION (same as MultiClientGameFixture) to the "route through NetworkTestHelper" rule:
    /// this drives the dual-NetworkManager substrate by hand on purpose. UnityTransport loopback only —
    /// production transport is Facepunch (Steam), deliberately not used here.
    /// </summary>
    public class AvatarSpawnTests
    {
        private const uint AvatarPrefabHash = 0xC0DE0201u;
        private const uint AvatarManagerPrefabHash = 0xC0DE0202u;
        private const ushort LoopbackPort = 7799;
        private const ulong SimulatedBotClientId = 100;

        private GameObject _avatarPrefabGo;
        private GameObject _managerPrefabGo;
        private NetworkObject _avatarPrefabNo;
        private NetworkObject _managerPrefabNo;

        private GameObject _hostNmGo;
        private GameObject _clientNmGo;
        private NetworkManager _hostNm;
        private NetworkManager _clientNm;
        private AvatarManager _hostManager;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            // --- Avatar prefab template (NetworkObject + NetworkTransform + PlayerAvatar). Unique non-zero
            // hash so replication can key on it and it does not collide with hash 0.
            _avatarPrefabGo = new GameObject("FixtureAvatarPrefab");
            _avatarPrefabNo = _avatarPrefabGo.AddComponent<NetworkObject>();
            SetGlobalObjectIdHash(_avatarPrefabNo, AvatarPrefabHash);
            MarkAsNonSceneObject(_avatarPrefabNo);
            // Owner authority (Story 13.2): a runtime-added NetworkTransform defaults to Server — force
            // Owner so the test exercises the same authority the production prefab ships (AuthorityMode = 1).
            NetworkTransform _networkTransform = _avatarPrefabGo.AddComponent<NetworkTransform>();
            FieldInfo _authorityField = typeof(NetworkTransform).GetField("AuthorityMode",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (_authorityField != null)
            {
                _authorityField.SetValue(_networkTransform, System.Enum.ToObject(_authorityField.FieldType, 1));
            }
            _avatarPrefabGo.AddComponent<PlayerAvatar>();

            // --- AvatarManager prefab template. The [SerializeField] _avatarPrefab is set on the TEMPLATE
            // via reflection so the InstantiateAndSpawn clone inherits it (Object.Instantiate copies the
            // live serialized field — same pattern the fixture uses for GameManager.gameStates). The seat /
            // spawn lists stay empty and _avatarsParent stays null (null-tolerant) — neither is exercised by
            // these spawn assertions.
            _managerPrefabGo = new GameObject("FixtureAvatarManagerPrefab");
            _managerPrefabNo = _managerPrefabGo.AddComponent<NetworkObject>();
            SetGlobalObjectIdHash(_managerPrefabNo, AvatarManagerPrefabHash);
            MarkAsNonSceneObject(_managerPrefabNo);
            var _managerComp = _managerPrefabGo.AddComponent<AvatarManager>();
            ReflectionHelper.SetPrivateField(_managerComp, "_avatarPrefab", _avatarPrefabNo);

            // --- Host NM FIRST (its OnEnable claims NetworkManager.Singleton), client NM SECOND.
            _hostNmGo = new GameObject("FixtureAvatarHostNM");
            _hostNm = _hostNmGo.AddComponent<NetworkManager>();
            ConfigureNetworkManager(_hostNm, _hostNmGo);

            _clientNmGo = new GameObject("FixtureAvatarClientNM");
            _clientNm = _clientNmGo.AddComponent<NetworkManager>();
            ConfigureNetworkManager(_clientNm, _clientNmGo);

            Assert.IsTrue(NetworkManager.Singleton == _hostNm,
                "Host NM (created first) must own NetworkManager.Singleton.");

            // Drop any registry entry the template's lifecycle may have left (domain reload disabled).
            ResetAvatarManagerStatics();

            Assert.IsTrue(_hostNm.StartHost(), "NGO StartHost() failed — host did not start.");

            // Spawn the manager on the host. Its OnNetworkSpawn (server) loops already-connected clients
            // (the host itself, clientId 0) and spawns their avatars, then subscribes for future connects.
            _hostManager = _hostNm.SpawnManager.InstantiateAndSpawn(_managerPrefabNo, destroyWithScene: true)
                .GetComponent<AvatarManager>();
            yield return NetworkTestHelper.WaitUntilAllSpawnedOrTimeout(5f, _hostManager);

            Assert.IsTrue(_clientNm.StartClient(), "NGO StartClient() failed — client did not start.");

            // Wait until the client is FULLY ready before any test body runs: connected, its AvatarManager
            // replica registered, AND both real-client avatars resolved on the client replica. The weaker
            // "manager replica != null" alone can return while the connection is still settling (notably after
            // the prior test's despawn churn), so a body could start before replication stabilised and NRE on a
            // still-null replica — the root of this class' in-group order-dependent flake.
            yield return NetworkTestHelper.WaitUntilOrTimeout(
                () => _clientNm.IsConnectedClient
                      && AvatarManager.For(_clientNm) != null
                      && AvatarManager.For(_clientNm).GetAvatar(0) != null
                      && AvatarManager.For(_clientNm).GetAvatar(_clientNm.LocalClientId) != null,
                10f,
                "Client never fully connected + replicated both avatars before the test body.");
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            // UTP logs benign socket-teardown errors when one loopback socket closes with a pending
            // receive on the other — ignore failing logs for the teardown window only (mirror the fixture).
            LogAssert.ignoreFailingMessages = true;

            // Despawn the host manager FIRST so it unsubscribes the connect/disconnect callbacks before the
            // shutdown disconnect sequence runs.
            if (_hostNm != null && _hostNm.IsListening && _hostManager != null && _hostManager.IsSpawned)
            {
                _hostManager.NetworkObject.Despawn(true);
            }
            yield return null;

            if (_clientNm != null && _clientNm.IsListening) _clientNm.Shutdown();
            if (_hostNm != null && _hostNm.IsListening) _hostNm.Shutdown();

            yield return NetworkTestHelper.WaitUntilOrTimeout(
                () => (_clientNm == null || !_clientNm.IsListening) && (_hostNm == null || !_hostNm.IsListening),
                5f,
                "NGO did not stop listening within 5s after Shutdown().");

            LogAssert.ignoreFailingMessages = false;

            if (_clientNmGo != null) Object.Destroy(_clientNmGo);
            if (_hostNmGo != null) Object.Destroy(_hostNmGo);
            if (_managerPrefabGo != null) Object.Destroy(_managerPrefabGo);
            if (_avatarPrefabGo != null) Object.Destroy(_avatarPrefabGo);

            // Wait for NGO to null the static Singleton on the destroyed host NM (its OnDestroy clears it). A
            // single yield is NOT enough: a LIVE stale Singleton would make the NEXT test's host NM self-destruct
            // on OnEnable (it can't claim an already-set Singleton), leaving its client replica unregistered so
            // AvatarManager.For(_clientNm) returns null in the body -> NRE. This is the root cause of this class'
            // in-group order-dependent flake; mirror AvatarEmbodiedModeTests / MultiClientGameFixture teardown.
            yield return NetworkTestHelper.WaitUntilOrTimeout(
                () => NetworkManager.Singleton == null, 5f,
                "NetworkManager.Singleton was not cleared after teardown.");

            // Statics must be clean for the next test (domain reload is disabled).
            ResetAvatarManagerStatics();
            yield return null;
        }

        [UnityTest]
        public IEnumerator Avatar_SpawnsForEachRealClient_AndIsVisibleOnTheRemote()
        {
            ulong _clientId = _clientNm.LocalClientId; // host = 0, this client = 1
            AvatarManager _clientManager = AvatarManager.For(_clientNm);
            Assert.IsNotNull(_clientManager, "Client AvatarManager replica must resolve.");

            // The remote replica must see an avatar for BOTH real clients (host 0 + the real client),
            // delivered via the replicated authoritative list.
            yield return NetworkTestHelper.WaitUntilOrTimeout(
                () => _clientManager.AvatarCount >= 2
                      && _clientManager.GetAvatar(0) != null
                      && _clientManager.GetAvatar(_clientId) != null,
                10f,
                "Client replica never saw replicated avatars for both real clients.");

            Assert.AreEqual(2, _hostManager.AvatarCount,
                "Host should own exactly two avatars (the host + one real client).");
            Assert.IsNotNull(_clientManager.GetAvatar(0), "Host avatar (clientId 0) must be visible on the remote.");
            Assert.IsNotNull(_clientManager.GetAvatar(_clientId), $"Avatar for real client {_clientId} must be visible on the remote.");
        }

        [UnityTest]
        public IEnumerator Avatar_SimulatedBot_GetsNoAvatar()
        {
            // Wait for the two real-client avatars from setup to settle.
            yield return NetworkTestHelper.WaitUntilOrTimeout(
                () => _hostManager.AvatarCount >= 2,
                10f,
                "Setup avatars never spawned for the two real clients.");

            int _before = _hostManager.AvatarCount;

            // AC #6 / DO2: a simulated bot (clientId >= 100) gets NO avatar.
            PlayerAvatar _botAvatar = _hostManager.SpawnAvatar(SimulatedBotClientId);
            Assert.IsNull(_botAvatar, "SpawnAvatar(>=100) must return null — simulated bots get no avatar.");

            yield return null;
            yield return null;

            Assert.AreEqual(_before, _hostManager.AvatarCount,
                "A simulated bot must not add an avatar to the authoritative set.");
            Assert.IsNull(_hostManager.GetAvatar(SimulatedBotClientId),
                "No avatar must exist for the simulated bot clientId.");
        }

        [UnityTest]
        public IEnumerator Avatar_Despawn_RemovesTheReplicaOnTheRemote()
        {
            ulong _clientId = _clientNm.LocalClientId;
            AvatarManager _clientManager = AvatarManager.For(_clientNm);

            // Ensure the real client's avatar is present on the remote first.
            yield return NetworkTestHelper.WaitUntilOrTimeout(
                () => _clientManager.GetAvatar(_clientId) != null,
                10f,
                "Real client's avatar never became visible on the remote.");

            _hostManager.DespawnAvatar(_clientId);

            // The despawn must propagate: the remote replica no longer sees that avatar.
            yield return NetworkTestHelper.WaitUntilOrTimeout(
                () => _clientManager.GetAvatar(_clientId) == null,
                10f,
                "Despawned avatar is still visible on the remote replica.");

            Assert.IsNull(_hostManager.GetAvatar(_clientId),
                "Despawned avatar must be gone from the host's authoritative set too.");
        }

        [UnityTest]
        public IEnumerator Avatar_IsOwnedByItsClient_AndOwnerDrivenPositionReplicates()
        {
            ulong _clientId = _clientNm.LocalClientId;
            AvatarManager _clientManager = AvatarManager.For(_clientNm);

            yield return NetworkTestHelper.WaitUntilOrTimeout(
                () => _hostManager.GetAvatar(0) != null
                      && _hostManager.GetAvatar(_clientId) != null
                      && _clientManager.GetAvatar(0) != null,
                10f,
                "Avatars for both real clients never settled on host + client.");

            // Story 13.2 ownership: each avatar is owned by its own client (owner-authoritative movement).
            Assert.AreEqual(0UL, _hostManager.GetAvatar(0).OwnerClientId,
                "The host's avatar must be owned by the host (clientId 0).");
            Assert.AreEqual(_clientId, _hostManager.GetAvatar(_clientId).OwnerClientId,
                $"The real client's avatar must be owned by its client ({_clientId}).");

            // Owner-authoritative NetworkTransform: the CLIENT drives its OWN avatar's position on the
            // client side, and the HOST replica receives it. Under server authority a non-server write
            // would be ignored — so this distinguishes (and proves) OWNER authority.
            Vector3 _target = new Vector3(2f, 0f, 3f);
            _clientManager.GetAvatar(_clientId).transform.position = _target;

            yield return NetworkTestHelper.WaitUntilOrTimeout(
                () => Vector3.Distance(_hostManager.GetAvatar(_clientId).transform.position, _target) < 0.5f,
                10f,
                "Owner (client)-driven avatar position did not replicate to the host via NetworkTransform.");
        }

        // Story 13.4 — the seated embodied window's NetworkTransform suppression/restore (the riskiest new
        // lifecycle path: the class doc warns that if RestoreAll never runs every avatar's NetworkTransform
        // stays disabled forever). Exercised on the multi-NM substrate where an AvatarManager + real avatars
        // exist (the camera-fixture tests deliberately no-op the presenter with no manager).
        [UnityTest]
        public IEnumerator SeatingPresenter_SuppressesNetworkTransformOnEnter_AndRestoresOnExitAndOnDisable()
        {
            // The host's own avatar (clientId 0) is the LOCAL avatar for the host NM — the presenter only
            // places the ring once the local avatar resolves.
            yield return NetworkTestHelper.WaitUntilOrTimeout(
                () => _hostManager.GetAvatar(0) != null,
                10f,
                "Host avatar never settled for the seating-presenter test.");

            PlayerAvatar _avatar = _hostManager.GetAvatar(0);
            NetworkTransform _networkTransform = _avatar.GetComponent<NetworkTransform>();
            Assert.IsNotNull(_networkTransform, "Fixture avatar must carry a NetworkTransform.");
            Assert.IsTrue(_networkTransform.enabled, "NetworkTransform must start enabled (before the embodied window).");

            var _presenterGo = new GameObject("FixtureSeatingPresenter");
            var _presenter = _presenterGo.AddComponent<AvatarSeatingPresenter>();

            // Enter the embodied Vote window → the presenter suppresses (disables) each avatar's NetworkTransform
            // in its LateUpdate so it can drive the per-client ring pose locally.
            _presenter.SetActive(true);
            yield return null; // LateUpdate runs Suppress
            yield return null;
            Assert.IsFalse(_networkTransform.enabled,
                "Entering the embodied window must suppress (disable) the avatar's NetworkTransform.");

            // Leave the window → RestoreAll restores the pose and re-enables the NetworkTransform synchronously.
            _presenter.SetActive(false);
            Assert.IsTrue(_networkTransform.enabled,
                "Leaving the embodied window must restore (re-enable) the avatar's NetworkTransform.");

            // OnDisable safety net: destroying the presenter while still active must also restore — suppression
            // can never outlive the presenter (otherwise the NetworkTransform would stay disabled forever).
            _presenter.SetActive(true);
            yield return null;
            Assert.IsFalse(_networkTransform.enabled, "Re-entering must suppress the NetworkTransform again.");
            Object.Destroy(_presenterGo); // OnDisable fires while active → RestoreAll
            yield return null;
            Assert.IsTrue(_networkTransform.enabled,
                "OnDisable must restore the NetworkTransform when the presenter is torn down mid-window.");
        }

        // Story 13.4 — the ratified owner-write seated gaze (SeatedYaw/SeatedPitch) IsOwner gate + replication.
        // Driven from the HOST side: IsOwner is reliable there (the host IS NetworkManager.Singleton, so a
        // NetworkBehaviour's IsOwner resolves correctly; a client replica's IsOwner is NOT reliable on this
        // 2-NetworkManager-in-one-process substrate because IsOwner keys off the Singleton's LocalClientId).
        // The host owns its own avatar → publish writes + replicates to the client; the host's view of the
        // client's avatar is non-owned → publish is a no-op (so a non-owner can never poke another's gaze).
        [UnityTest]
        public IEnumerator Avatar_PublishSeatedLook_WritesAndReplicatesWhenOwner_AndIsNoOpWhenNotOwner()
        {
            ulong _clientId = _clientNm.LocalClientId;
            AvatarManager _clientManager = AvatarManager.For(_clientNm);

            yield return NetworkTestHelper.WaitUntilOrTimeout(
                () => _hostManager.GetAvatar(0) != null
                      && _hostManager.GetAvatar(_clientId) != null
                      && _clientManager.GetAvatar(0) != null,
                10f,
                "Avatars never settled on host + client for the seated-look test.");

            // Owner path: the host owns its own avatar (clientId 0) — the IsOwner gate lets the publish through
            // and writes the ratified owner-write SeatedYaw/SeatedPitch.
            PlayerAvatar _hostOwn = _hostManager.GetAvatar(0);
            Assert.IsTrue(_hostOwn.IsOwner, "The host must own its own avatar.");
            _hostOwn.PublishSeatedLook(30f, -10f);
            Assert.AreEqual(30f, _hostOwn.SeatedYaw.Value, 0.01f, "Owner PublishSeatedLook must write SeatedYaw.");
            Assert.AreEqual(-10f, _hostOwn.SeatedPitch.Value, 0.01f, "Owner PublishSeatedLook must write SeatedPitch.");

            // The owner-written values replicate to the client replica (NetworkVariableReadPermission.Everyone).
            yield return NetworkTestHelper.WaitUntilOrTimeout(
                () => Mathf.Abs(_clientManager.GetAvatar(0).SeatedYaw.Value - 30f) < 0.01f
                      && Mathf.Abs(_clientManager.GetAvatar(0).SeatedPitch.Value - (-10f)) < 0.01f,
                10f,
                "Owner-published SeatedYaw/Pitch never replicated to the client replica.");

            // Non-owner path: the host's replica of the CLIENT's avatar is not owned by the host —
            // PublishSeatedLook is gated by IsOwner and must be a no-op (the value stays at its 0 default).
            PlayerAvatar _hostViewOfClient = _hostManager.GetAvatar(_clientId);
            Assert.IsFalse(_hostViewOfClient.IsOwner, "The host must NOT own the client's avatar.");
            _hostViewOfClient.PublishSeatedLook(99f, 99f);
            yield return null;
            yield return null;
            Assert.AreEqual(0f, _hostViewOfClient.SeatedYaw.Value, 0.01f,
                "PublishSeatedLook on a non-owned avatar must be a no-op (IsOwner-gated).");
        }

        // --- substrate helpers (lifted from MultiClientGameFixture) ---

        private void ConfigureNetworkManager(NetworkManager _nm, GameObject _go)
        {
            var _transport = _go.AddComponent<UnityTransport>();
            _transport.SetConnectionData("127.0.0.1", LoopbackPort);
            _nm.NetworkConfig = new NetworkConfig
            {
                NetworkTransport = _transport,
                EnableSceneManagement = false,
            };
            _nm.NetworkConfig.Prefabs.Add(new NetworkPrefab { Prefab = _avatarPrefabGo });
            _nm.NetworkConfig.Prefabs.Add(new NetworkPrefab { Prefab = _managerPrefabGo });
        }

        private static void SetGlobalObjectIdHash(NetworkObject _networkObject, uint _hash)
        {
            FieldInfo _field = typeof(NetworkObject).GetField("GlobalObjectIdHash",
                BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            _field.SetValue(_networkObject, _hash);
        }

        private static void MarkAsNonSceneObject(NetworkObject _networkObject)
        {
            PropertyInfo _prop = typeof(NetworkObject).GetProperty("IsSceneObject",
                BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            _prop.SetValue(_networkObject, (bool?)false);
        }

        private static void ResetAvatarManagerStatics()
        {
            // s_byNetworkManager is a private static readonly Dictionary; readonly forbids reassignment but
            // Clear() is fine. ReflectionHelper cannot reach statics, so read the field value directly.
            FieldInfo _field = typeof(AvatarManager).GetField("s_byNetworkManager",
                BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
            if (_field?.GetValue(null) is System.Collections.IDictionary _registry)
            {
                _registry.Clear();
            }
        }
    }
}
