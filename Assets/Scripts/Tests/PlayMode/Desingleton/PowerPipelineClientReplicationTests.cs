using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using Characters;
using Characters.Powers;
using ChatSystem;
using GameLogic;
using NUnit.Framework;
using RoleTarget;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.TestTools;

namespace Tests.PlayMode.Desingleton
{
    /// <summary>
    /// 2-NM E2E — REAL power pipelines (server RPC → pure decision → dispatcher → executors) whose
    /// terminal effects are observed on the REAL remote client: elimination (Bounty), chat broadcast
    /// (Bounty), bless+heal (Blessing), and the chaining NetworkList (ChainedByShadows). Extends the
    /// Proto B pattern (PowerObjectReplicationProtoTests) to three more powers and three more effect
    /// carriers (chat RPC fan-out, NetworkList replication, Public reveal fan-out).
    ///
    /// SINGLETON DANCE: ChatManager and ChainingManager destroy duplicate instances in Awake. In this
    /// one-process 2-NM world the host clone claims the static first, and the client replica's Awake
    /// would then DESTROY the replica (an NGO error). So: null the static right after the host spawn
    /// (the replica, instantiated on a later tick, claims it harmlessly), then restore the HOST
    /// instance once the replica exists — the executors resolve the static server-side and must get
    /// the host object. RoleTargetSystem claims in OnNetworkSpawn claim-if-free (no dance needed).
    ///
    /// REVEALER: reveal-emitting executors resolve CompositionRoot.For(serverNm).GameInfoRevealer,
    /// which answers only from a registered root — so a bound root (reflection into the per-NM
    /// registry, the OwnerLocalEffectBoundaryTests pattern) is registered for the HOST NM.
    /// </summary>
    public class PowerPipelineClientReplicationTests : MultiClientGameFixture
    {
        private const uint BountyPrefabHash = 0xC0DE0301u;
        private const uint BlessingPrefabHash = 0xC0DE0302u;
        private const uint ChainedPrefabHash = 0xC0DE0303u;
        private const uint RtsPrefabHash = 0xC0DE0304u;
        private const uint RevealerPrefabHash = 0xC0DE0305u;
        private const uint ChatPrefabHash = 0xC0DE0306u;
        private const uint ChainingPrefabHash = 0xC0DE0307u;

        private GameObject _bountyPrefabGo, _blessingPrefabGo, _chainedPrefabGo, _rtsPrefabGo,
            _revealerPrefabGo, _chatPrefabGo, _chainingPrefabGo, _placeholderGo, _rootGo;
        private NetworkObject _bountyPrefabNo, _blessingPrefabNo, _chainedPrefabNo, _rtsPrefabNo,
            _revealerPrefabNo, _chatPrefabNo, _chainingPrefabNo;

        private RoleTargetSystem _hostRts;
        private GameInfoRevealer _hostRevealer, _clientRevealer;
        private ChatManager _hostChat, _clientChat;
        private ChainingManager _hostChaining, _clientChaining;
        private readonly List<NetworkBehaviour> _spawnedHostObjects = new();

        protected override void BuildExtraNetworkPrefabs(List<GameObject> _templates)
        {
            _bountyPrefabGo = MakePrefab("PipelineBountyPrefab", BountyPrefabHash, out _bountyPrefabNo);
            _bountyPrefabGo.AddComponent<PHighPriorityBounty>();
            _templates.Add(_bountyPrefabGo);

            _blessingPrefabGo = MakePrefab("PipelineBlessingPrefab", BlessingPrefabHash, out _blessingPrefabNo);
            _blessingPrefabGo.AddComponent<PBlessing>();
            _templates.Add(_blessingPrefabGo);

            _chainedPrefabGo = MakePrefab("PipelineChainedPrefab", ChainedPrefabHash, out _chainedPrefabNo);
            _chainedPrefabGo.AddComponent<PChainedByTheShadows>();
            _templates.Add(_chainedPrefabGo);

            _rtsPrefabGo = MakePrefab("PipelineRtsPrefab", RtsPrefabHash, out _rtsPrefabNo);
            _rtsPrefabGo.AddComponent<RoleTargetSystem>();
            _templates.Add(_rtsPrefabGo);

            // Inactive placeholders so every revealer instance's Start() asserts pass (Awake never
            // runs on an inactive GO — no singleton claims).
            _placeholderGo = new GameObject("PipelinePlaceholders");
            _placeholderGo.SetActive(false);
            var _placeholderCm = _placeholderGo.AddComponent<CharacterManager>();
            var _placeholderGm = _placeholderGo.AddComponent<GameManager>();

            _revealerPrefabGo = MakePrefab("PipelineRevealerPrefab", RevealerPrefabHash, out _revealerPrefabNo);
            var _revealerTemplate = _revealerPrefabGo.AddComponent<GameInfoRevealer>();
            ReflectionHelper.SetPrivateField(_revealerTemplate, "characterManager", _placeholderCm);
            ReflectionHelper.SetPrivateField(_revealerTemplate, "gameManager", _placeholderGm);
            _templates.Add(_revealerPrefabGo);

            _chatPrefabGo = MakePrefab("PipelineChatPrefab", ChatPrefabHash, out _chatPrefabNo);
            _chatPrefabGo.AddComponent<ChatManager>(); // Awake claims the static on the TEMPLATE...
            _templates.Add(_chatPrefabGo);

            _chainingPrefabGo = MakePrefab("PipelineChainingPrefab", ChainingPrefabHash, out _chainingPrefabNo);
            _chainingPrefabGo.AddComponent<ChainingManager>(); // ...same here...
            _templates.Add(_chainingPrefabGo);

            // ...so free both statics NOW, or the future host clones' Awake would DESTROY the clones
            // as "duplicates" of the templates.
            ChatManager.instance = null;
            ChainingManager.instance = null;
        }

        private static GameObject MakePrefab(string _name, uint _hash, out NetworkObject _no)
        {
            var _go = new GameObject(_name);
            _no = _go.AddComponent<NetworkObject>();
            SetGlobalObjectIdHash(_no, _hash);
            MarkAsNonSceneObject(_no);
            return _go;
        }

        [UnityTearDown]
        public IEnumerator PipelineTearDown()
        {
            if (HostNm != null && HostNm.IsListening)
            {
                foreach (var _nb in _spawnedHostObjects)
                {
                    if (_nb != null && _nb.IsSpawned)
                    {
                        _nb.NetworkObject.Despawn(true);
                    }
                }
            }
            _spawnedHostObjects.Clear();
            yield return null;

            UnregisterBoundRoot(HostNm);
            if (_rootGo != null) Object.Destroy(_rootGo);
            if (_placeholderGo != null) Object.Destroy(_placeholderGo);

            // Domain reload is disabled — statics must not leak across tests.
            RoleTargetSystem.instance = null;
            ChatManager.instance = null;
            ChainingManager.instance = null;

            _hostRts = null;
            _hostRevealer = null; _clientRevealer = null;
            _hostChat = null; _clientChat = null;
            _hostChaining = null; _clientChaining = null;
        }

        // --- world-building helpers -------------------------------------------------------------

        private T SpawnOnHost<T>(NetworkObject _prefabNo) where T : NetworkBehaviour
        {
            T _spawned = HostNm.SpawnManager.InstantiateAndSpawn(_prefabNo, destroyWithScene: true)
                .GetComponent<T>();
            _spawnedHostObjects.Add(_spawned);
            return _spawned;
        }

        private IEnumerator ResolveClientReplica<T>(T _hostObject, System.Action<T> _assign) where T : NetworkBehaviour
        {
            ulong _netId = _hostObject.NetworkObjectId;
            yield return NetworkTestHelper.WaitUntilOrTimeout(
                () => ClientNm.SpawnManager.SpawnedObjects.ContainsKey(_netId),
                5f,
                $"Client NM never spawned its replica of {typeof(T).Name} (networkObjectId {_netId}).");
            T _replica = ClientNm.SpawnManager.SpawnedObjects[_netId].GetComponent<T>();
            Assert.IsNotNull(_replica, $"Client {typeof(T).Name} replica missing after wait.");
            Assert.AreNotSame(_hostObject, _replica, $"{typeof(T).Name} replicas must be distinct objects.");
            _assign(_replica);
        }

        private IEnumerator SpawnRts()
        {
            _hostRts = SpawnOnHost<RoleTargetSystem>(_rtsPrefabNo);
            yield return NetworkTestHelper.WaitUntilSpawnedOrTimeout(_hostRts, 5f);
            Assert.AreSame(_hostRts, RoleTargetSystem.instance,
                "The host RTS must claim the singleton (the executors resolve it server-side).");
            yield return ResolveClientReplica<RoleTargetSystem>(_hostRts, _ => { });
        }

        private IEnumerator SpawnRevealerWithHostRoot()
        {
            _hostRevealer = SpawnOnHost<GameInfoRevealer>(_revealerPrefabNo);
            yield return NetworkTestHelper.WaitUntilSpawnedOrTimeout(_hostRevealer, 5f);
            yield return ResolveClientReplica<GameInfoRevealer>(_hostRevealer, _r => _clientRevealer = _r);
            ReflectionHelper.SetPrivateField(_hostRevealer, "characterManager", HostCm);
            ReflectionHelper.SetPrivateField(_clientRevealer, "characterManager", ClientCm);
            _rootGo = MakeBoundRoot(HostNm, _hostRevealer, HostCm);
        }

        private IEnumerator SpawnChatWithSingletonDance()
        {
            ChatManager.instance = null;
            _hostChat = SpawnOnHost<ChatManager>(_chatPrefabNo);
            // The host clone's Awake just claimed the static; free it IMMEDIATELY so the client
            // replica (instantiated on a later tick) survives its own destroy-duplicate Awake.
            ChatManager.instance = null;
            yield return NetworkTestHelper.WaitUntilSpawnedOrTimeout(_hostChat, 5f);
            yield return ResolveClientReplica<ChatManager>(_hostChat, _c => _clientChat = _c);
            // Restore the SERVER-side singleton the executors resolve.
            ChatManager.instance = _hostChat;
        }

        private IEnumerator SpawnChainingWithSingletonDance()
        {
            ChainingManager.instance = null;
            _hostChaining = SpawnOnHost<ChainingManager>(_chainingPrefabNo);
            ChainingManager.instance = null;
            yield return NetworkTestHelper.WaitUntilSpawnedOrTimeout(_hostChaining, 5f);
            yield return ResolveClientReplica<ChainingManager>(_hostChaining, _c => _clientChaining = _c);
            ChainingManager.instance = _hostChaining;
        }

        /// <summary>Owner seat on the host, target seat on the REAL remote client; returns the host
        /// target and its resolved CLIENT replica.</summary>
        private IEnumerator SpawnOwnerAndTarget(Role _ownerRole, Role _targetRole,
            System.Action<Character, Character> _assign)
        {
            ulong _ownerId = HostNm.LocalClientId;
            ulong _targetId = ClientNm.LocalClientId;
            yield return SpawnRealCharacterForClient(_ownerId, _ownerRole);
            yield return SpawnRealCharacterForClient(_targetId, _targetRole);
            Character _hostTarget = HostCm.GetCharacter(_targetId, false);
            Assert.IsNotNull(_hostTarget, "Host has no Character for the target seat.");
            Character _clientTarget = null;
            yield return ResolveClientReplica<Character>(_hostTarget, _c => _clientTarget = _c);
            _assign(_hostTarget, _clientTarget);
        }

        private IEnumerator SpawnPower<T>(NetworkObject _prefabNo, System.Action<T> _assign) where T : Power
        {
            T _hostPower = SpawnOnHost<T>(_prefabNo);
            yield return NetworkTestHelper.WaitUntilSpawnedOrTimeout(_hostPower, 5f);
            // Wait for the client replica too: its OnNetworkSpawn resolving the CLIENT NM's managers
            // (the Proto B crux) is part of the pipeline being real on both sides.
            yield return ResolveClientReplica<T>(_hostPower, _ => { });
            _assign(_hostPower);
        }

        // --- tests -------------------------------------------------------------------------------

        // 1. HighPriorityBounty on the Robot: the SetEliminated executor's server write must reach the
        // remote target's replica, and the RevealPublic executor's SendTo.Everyone reveal must land in
        // the REMOTE observer's reveal store.
        [UnityTest]
        public IEnumerator HighPriorityBounty_EliminatesRobot_AndPublicReveal_ObservedOnRemoteClient()
        {
            Character _hostTarget = null, _clientTarget = null;
            yield return SpawnOwnerAndTarget(
                new Role { roleName = "Hunter" },
                new Role { roleID = RoleID.Robot, roleName = "Robot" },
                (_h, _c) => { _hostTarget = _h; _clientTarget = _c; });
            yield return SpawnRts();
            yield return SpawnRevealerWithHostRoot();
            yield return SpawnChatWithSingletonDance();

            PHighPriorityBounty _hostPower = null;
            yield return SpawnPower<PHighPriorityBounty>(_bountyPrefabNo, _p => _hostPower = _p);
            Assert.IsFalse(_clientTarget.isEliminated.Value, "Baseline: client target replica not eliminated.");

            ReflectionHelper.InvokePrivateMethod(_hostPower, "OnCardClickedRpc",
                _hostTarget.ownerClientId.Value);

            yield return NetworkTestHelper.WaitUntilOrTimeout(
                () => _clientTarget.isEliminated.Value,
                5f,
                "The bounty elimination never crossed the wire to the remote target's replica.");
            Assert.IsTrue(_hostTarget.isEliminated.Value, "Sanity: host target must be eliminated too.");

            yield return NetworkTestHelper.WaitUntilOrTimeout(
                () => _clientRevealer.GetCharacterInfo(_hostTarget.ownerClientId.Value, ClientNm.LocalClientId)
                          .isRoleRevealed == RevealLevel.Public,
                5f,
                "The Public role reveal never reached the REMOTE observer's reveal store.");
        }

        // 2. Same pipeline, chat leg: the ChatBroadcast(All) executor's RPC must be RECEIVED by the
        // remote client's ChatManager replica (message stored in its own Server window).
        [UnityTest]
        public IEnumerator HighPriorityBounty_BroadcastChat_IsReceivedByRemoteChatReplica()
        {
            Character _hostTarget = null, _clientTarget = null;
            yield return SpawnOwnerAndTarget(
                new Role { roleName = "Hunter" },
                new Role { roleID = RoleID.Robot, roleName = "Robot" },
                (_h, _c) => { _hostTarget = _h; _clientTarget = _c; });
            yield return SpawnRts();
            yield return SpawnRevealerWithHostRoot();
            yield return SpawnChatWithSingletonDance();

            PHighPriorityBounty _hostPower = null;
            yield return SpawnPower<PHighPriorityBounty>(_bountyPrefabNo, _p => _hostPower = _p);

            ChatMessage _received = default;
            bool _clientGotMessage = false;
            _clientChat.onChatMessageReceived += _m => { _received = _m; _clientGotMessage = true; };

            ReflectionHelper.InvokePrivateMethod(_hostPower, "OnCardClickedRpc",
                _hostTarget.ownerClientId.Value);

            yield return NetworkTestHelper.WaitUntilOrTimeout(
                () => _clientGotMessage,
                5f,
                "The broadcast chat message never reached the REMOTE client's ChatManager replica.");
            Assert.AreEqual((int)ChatWindowIDs.Server, _received.chatId,
                "The remote client must receive the broadcast in the Server window.");
        }

        // 3. Blessing on a matching-role target: the SetBlessed + HealPlayer executors' server writes
        // must reach the remote target's replica.
        [UnityTest]
        public IEnumerator Blessing_BlessesAndHealsMatchingTarget_ObservedOnRemoteClientReplica()
        {
            Character _hostTarget = null, _clientTarget = null;
            yield return SpawnOwnerAndTarget(
                new Role { roleName = "Blesser" },
                new Role { roleName = "Bless-Test" },
                (_h, _c) => { _hostTarget = _h; _clientTarget = _c; });
            yield return SpawnRts();
            yield return SpawnRevealerWithHostRoot();
            yield return SpawnChatWithSingletonDance();

            PBlessing _hostPower = null;
            yield return SpawnPower<PBlessing>(_blessingPrefabNo, _p => _hostPower = _p);
            Assert.IsFalse(_clientTarget.isBlessed.Value, "Baseline: client target replica not blessed.");

            var _compareRole = new Role { roleName = "Bless-Test", ownerClientId = _hostTarget.ownerClientId.Value };
            ReflectionHelper.InvokePrivateMethod(_hostPower, "TryBlessCharacterServerRpc",
                _hostTarget.ownerClientId.Value, _compareRole);

            yield return NetworkTestHelper.WaitUntilOrTimeout(
                () => _clientTarget.isBlessed.Value && _clientTarget.isHealed.Value,
                5f,
                "The bless+heal never crossed the wire to the remote target's replica.");
            Assert.IsTrue(_hostTarget.isBlessed.Value, "Sanity: host target must be blessed too.");
        }

        // 4. ChainedByShadows on a matching chosen target: the AddToChain executor appends to the
        // ChainingManager's replicated NetworkList — the membership must be observed on the REMOTE
        // client's ChainingManager replica (NetworkList replication, a different carrier from
        // NetworkVariables and RPCs).
        [UnityTest]
        public IEnumerator ChainedByShadows_ChainsChosenTarget_NetworkListObservedOnRemoteReplica()
        {
            Character _hostTarget = null, _clientTarget = null;
            yield return SpawnOwnerAndTarget(
                new Role { roleName = "Shadow" },
                new Role { factionType = FactionType.chosen, roleName = "Chosen-Test" },
                (_h, _c) => { _hostTarget = _h; _clientTarget = _c; });
            yield return SpawnRts();
            yield return SpawnRevealerWithHostRoot();
            yield return SpawnChainingWithSingletonDance();

            PChainedByTheShadows _hostPower = null;
            yield return SpawnPower<PChainedByTheShadows>(_chainedPrefabNo, _p => _hostPower = _p);
            Assert.AreEqual(0, _clientChaining.chainingPlayers.Count,
                "Baseline: remote chaining list starts empty.");

            ulong _targetId = _hostTarget.ownerClientId.Value;
            var _compareRole = new Role { roleName = "Chosen-Test", ownerClientId = _targetId };
            ReflectionHelper.InvokePrivateMethod(_hostPower, "TryCorruptCharacterServerRpc",
                _targetId, _compareRole);

            yield return NetworkTestHelper.WaitUntilOrTimeout(
                () => _clientChaining.chainingPlayers.Contains(_targetId),
                5f,
                "The chaining membership never replicated to the REMOTE client's ChainingManager NetworkList.");
            Assert.IsTrue(_hostChaining.chainingPlayers.Contains(_targetId),
                "Sanity: host chaining list must contain the target too.");
        }

        // --- bound-root plumbing (OwnerLocalEffectBoundaryTests pattern) -------------------------

        private static GameObject MakeBoundRoot(NetworkManager _nm, GameInfoRevealer _revealer, CharacterManager _cm)
        {
            var _go = new GameObject("PipelineBoundRoot");
            _go.SetActive(false); // Awake must not run (it binds to NetworkManager.Singleton).
            var _root = _go.AddComponent<CompositionRoot>();
            ReflectionHelper.SetPrivateField(_root, "gameInfoRevealer", _revealer);
            ReflectionHelper.SetPrivateField(_root, "characterManager", _cm);
            ReflectionHelper.SetPrivateField(_root, "_networkManager", _nm);
            Registry()[_nm] = _root;
            return _go;
        }

        private static void UnregisterBoundRoot(NetworkManager _nm)
        {
            if (_nm == null) return;
            var _registry = Registry();
            if (_registry.Contains(_nm)) _registry.Remove(_nm);
        }

        private static System.Collections.IDictionary Registry() =>
            (System.Collections.IDictionary)typeof(CompositionRoot)
                .GetField("s_byNetworkManager", BindingFlags.Static | BindingFlags.NonPublic)
                .GetValue(null);
    }
}
