using System.Collections;
using System.Reflection;
using System.Text.RegularExpressions;
using Board;
using Characters;
using Characters.Powers;
using Characters.Powers.Target;
using ChatSystem;
using GameLogic;
using GameLogic.Validation;
using RoleTarget;
using NUnit.Framework;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;
using UnityEngine.TestTools;

namespace Tests.PlayMode
{
    /// <summary>
    /// Powers v2 — the client/server-boundary regression net Murat asked for.
    ///
    /// The bug this locks down: PCursedVision and PEmbraceOfShadows emit OWNER-LOCAL presentation effects
    /// (RevealInfo Broadcast:false -> GameInfoRevealer.SetRevealLevel, a NON-replicated local dictionary
    /// write). v1 dispatched them on the caster's client; a v2 pass routed the whole decision behind a
    /// [Rpc(SendTo.Server)] hop, so for a non-host caster the reveal landed in the HOST's revealer and never
    /// reached the player. A host-only playtest (server == owner) is structurally blind to it — and so is
    /// the rest of the PlayMode suite (StartHost only, one revealer). This fixture boots a host PLUS one
    /// real in-process client (two NetworkManagers over UTP loopback) and gives EACH NM its OWN
    /// GameInfoRevealer (the reveal store is a plain local dictionary, not a NetworkVariable — so a
    /// standalone instance per NM is enough and avoids a replicated revealer's Start() wiring dance). It
    /// then asserts the discriminating property: a power cast BY the client writes the reveal into the
    /// CLIENT's revealer and NOT the host's. Re-introducing the SendTo.Server hop flips that and fails here.
    ///
    /// Substrate + reflection helpers follow the proven MultiClientGameFixture pattern (host created first
    /// keeps NetworkManager.Singleton; UTP loopback, not Facepunch). DOCUMENTED exception to the
    /// "route Start* through NetworkTestHelper" rule — the multi-NM boundary is the whole point.
    /// </summary>
    public class OwnerLocalEffectBoundaryTests
    {
        private const uint GmPrefabHash = 0xC0DE0200u;
        private const uint CmPrefabHash = 0xC0DE0201u;
        private const uint CharacterPrefabHash = 0xC0DE0202u;
        private const uint RtsPrefabHash = 0xC0DE0204u;
        private const uint EmbracePrefabHash = 0xC0DE0205u;
        private const uint CursedPrefabHash = 0xC0DE0206u;
        private const uint LackPrefabHash = 0xC0DE0207u;
        private const ushort LoopbackPort = 7799;

        private const ulong TargetSeat = 999UL;

        private GameObject _gmPrefabGo, _cmPrefabGo, _characterPrefabGo, _rtsPrefabGo, _embracePrefabGo, _cursedPrefabGo, _lackPrefabGo;
        private NetworkObject _gmPrefabNo, _cmPrefabNo, _characterPrefabNo, _rtsPrefabNo, _embracePrefabNo, _cursedPrefabNo, _lackPrefabNo;
        private GameState _seededState;

        private GameObject _hostNmGo, _clientNmGo;
        private NetworkManager _hostNm, _clientNm;

        private GameManager _hostGm;
        private CharacterManager _hostCm, _clientCm;
        private GameInfoRevealer _hostRevealer, _clientRevealer;
        private GameObject _hostRevealerGo, _clientRevealerGo, _hostRootGo, _clientRootGo;
        // Shared (not per-NM) singletons that CursedVision's non-discriminating effects resolve to
        // (ChatLocal -> ChatManager.instance, AddCardEffect -> CardEffectManager.instance). They only need
        // to exist so the client-side dispatch does not NRE before/after the discriminating reveal.
        private GameObject _chatGo, _cardGo, _dummyBoardGo;

        // Minimal state so GameManager.OnNetworkSpawn -> GetGameState(0) does not throw on an empty dict.
        private class DummyGameState : GameState
        {
            public override void StateUpdateClient() { }
            public override void StateUpdateServer() { }
        }

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            _gmPrefabGo = MakePrefab("BoundaryGmPrefab", GmPrefabHash, go =>
            {
                var gm = go.AddComponent<GameManager>();
                gm.ignoreGameLoop = true;
                _seededState = ScriptableObject.CreateInstance<DummyGameState>();
                gm.gameStates.Add(_seededState, new GameStateSettings());
            });
            _gmPrefabNo = _gmPrefabGo.GetComponent<NetworkObject>();
            _cmPrefabGo = MakePrefab("BoundaryCmPrefab", CmPrefabHash, go => go.AddComponent<CharacterManager>());
            _cmPrefabNo = _cmPrefabGo.GetComponent<NetworkObject>();
            _characterPrefabGo = MakePrefab("BoundaryCharacterPrefab", CharacterPrefabHash, go => go.AddComponent<Character>());
            _characterPrefabNo = _characterPrefabGo.GetComponent<NetworkObject>();
            _rtsPrefabGo = MakePrefab("BoundaryRtsPrefab", RtsPrefabHash, go => go.AddComponent<RoleTargetSystem>());
            _rtsPrefabNo = _rtsPrefabGo.GetComponent<NetworkObject>();
            _embracePrefabGo = MakePrefab("BoundaryEmbracePrefab", EmbracePrefabHash, go => go.AddComponent<PEmbraceOfShadows>());
            _embracePrefabNo = _embracePrefabGo.GetComponent<NetworkObject>();
            _cursedPrefabGo = MakePrefab("BoundaryCursedPrefab", CursedPrefabHash, go => go.AddComponent<PCursedVision>());
            _cursedPrefabNo = _cursedPrefabGo.GetComponent<NetworkObject>();
            _lackPrefabGo = MakePrefab("BoundaryLackPrefab", LackPrefabHash, go => go.AddComponent<PLackOfAffection>());
            _lackPrefabNo = _lackPrefabGo.GetComponent<NetworkObject>();

            _hostNmGo = new GameObject("BoundaryHostNM");
            _hostNm = _hostNmGo.AddComponent<NetworkManager>();
            ConfigureNm(_hostNm, _hostNmGo);
            _clientNmGo = new GameObject("BoundaryClientNM");
            _clientNm = _clientNmGo.AddComponent<NetworkManager>();
            ConfigureNm(_clientNm, _clientNmGo);

            Assert.IsTrue(NetworkManager.Singleton == _hostNm, "Host NM (first) must own the Singleton.");
            ResetManagerStatics();

            Assert.IsTrue(_hostNm.StartHost(), "StartHost failed.");

            // Spawn order GM -> CM -> RTS so the initial client sync creates them in that order and
            // RoleTargetSystem.Start (reads GameManager.For(nm)) finds a registered GameManager replica.
            _hostGm = _hostNm.SpawnManager.InstantiateAndSpawn(_gmPrefabNo, destroyWithScene: true).GetComponent<GameManager>();
            _hostCm = _hostNm.SpawnManager.InstantiateAndSpawn(_cmPrefabNo, destroyWithScene: true).GetComponent<CharacterManager>();
            ReflectionHelper.SetPrivateField(_hostGm, "characterManager", _hostCm);
            ReflectionHelper.SetPrivateField(_hostCm, "_characterPrefab", _characterPrefabNo);
            ReflectionHelper.SetPrivateField(_hostCm, "_charactersParent", _hostCm.transform);
            _hostNm.SpawnManager.InstantiateAndSpawn(_rtsPrefabNo, destroyWithScene: true);

            yield return NetworkTestHelper.WaitUntilAllSpawnedOrTimeout(5f, _hostGm, _hostCm);

            Assert.IsTrue(_clientNm.StartClient(), "StartClient failed.");

            yield return NetworkTestHelper.WaitUntilOrTimeout(
                () => GameManager.For(_clientNm) != null && CharacterManager.For(_clientNm) != null,
                10f, "Client GameManager/CharacterManager replicas never registered.");
            _clientCm = CharacterManager.For(_clientNm);

            // One standalone GameInfoRevealer per NM, kept INACTIVE: Start() (which asserts a scene-wired
            // gameManager and subscribes to onGameStarted) never runs, yet SetRevealLevel/GetCharacterInfo
            // are plain instance methods and work regardless. Two distinct local stores = the boundary.
            _hostRevealer = MakeStandaloneRevealer(out _hostRevealerGo, _hostCm);
            _clientRevealer = MakeStandaloneRevealer(out _clientRevealerGo, _clientCm);

            // A CompositionRoot per NM so CompositionRoot.For(nm).GameInfoRevealer resolves that NM's OWN
            // revealer. Awake binds a root to the Singleton only, so we inject both into the private per-NM
            // registry by reflection (bypassing the Singleton binding).
            _hostRootGo = MakeBoundRoot(_hostNm, _hostRevealer, _hostCm);
            _clientRootGo = MakeBoundRoot(_clientNm, _clientRevealer, _clientCm);

            CreateSharedSingletons();
        }

        // ChatManager + CardEffectManager are NOT de-singletonised (CompositionRoot serves ChatManager.instance
        // / CardEffectManager.instance), so they are ONE shared instance in this in-process 2-NM world — hence
        // non-discriminating for the boundary. They exist only so CursedVision's ChatLocal / AddCardEffect
        // effects do not NRE around the discriminating reveal.
        private void CreateSharedSingletons()
        {
            _chatGo = new GameObject("BoundaryChatManager");
            _chatGo.AddComponent<NetworkObject>();
            _chatGo.AddComponent<ChatManager>(); // Awake sets instance; AddMessageLocal is null-safe with no windows

            // BoardManager is needed ONLY as a non-null ref for CardEffectManager.Start's assert; kept INACTIVE
            // (its Awake never runs, so it never claims the BoardManager singleton).
            _dummyBoardGo = new GameObject("BoundaryDummyBoard");
            _dummyBoardGo.SetActive(false);
            var board = _dummyBoardGo.AddComponent<BoardManager>();

            // CardEffectManager: created inactive, boardManager wired, then activated so Awake (sets instance)
            // and Start (asserts boardManager, now non-null) run cleanly in order.
            _cardGo = new GameObject("BoundaryCardEffectManager");
            _cardGo.SetActive(false);
            var cem = _cardGo.AddComponent<CardEffectManager>();
            ReflectionHelper.SetPrivateField(cem, "boardManager", board);
            _cardGo.SetActive(true);
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            LogAssert.ignoreFailingMessages = true;

            if (_clientNm != null && _clientNm.IsListening) _clientNm.Shutdown();
            if (_hostNm != null && _hostNm.IsListening) _hostNm.Shutdown();
            yield return NetworkTestHelper.WaitUntilOrTimeout(
                () => (_clientNm == null || !_clientNm.IsListening) && (_hostNm == null || !_hostNm.IsListening),
                5f, "NGO did not stop listening after Shutdown().");

            LogAssert.ignoreFailingMessages = false;

            UnregisterBoundRoot(_hostNm);
            UnregisterBoundRoot(_clientNm);
            foreach (var go in new[] { _hostRootGo, _clientRootGo, _hostRevealerGo, _clientRevealerGo,
                                       _chatGo, _cardGo, _dummyBoardGo,
                                       _clientNmGo, _hostNmGo, _gmPrefabGo, _cmPrefabGo, _characterPrefabGo,
                                       _rtsPrefabGo, _embracePrefabGo, _cursedPrefabGo, _lackPrefabGo })
                if (go != null) Object.Destroy(go);
            if (_seededState != null) Object.Destroy(_seededState);

            ResetManagerStatics();
            ReflectionHelper.SetPrivateField(typeof(RoleTargetSystem), "instance", null);
            ReflectionHelper.SetPrivateField(typeof(ChatManager), "instance", null);
            ReflectionHelper.SetPrivateField(typeof(CardEffectManager), "instance", null);
            ReflectionHelper.SetPrivateField(typeof(BoardManager), "instance", null);
            yield return null;
        }

        /// <summary>
        /// The load-bearing regression assertion: when the CLIENT casts Embrace of Shadows on a
        /// matching-role target, the corruption reveal must land in the CLIENT's GameInfoRevealer, NOT the
        /// host's. The buggy SendTo.Server routing wrote it to the host — invisible to the caster.
        /// </summary>
        [UnityTest]
        public IEnumerator EmbraceCastByClient_RevealsOnClientRevealer_NotHost()
        {
            ulong clientId = _clientNm.LocalClientId;

            Character hostTarget = _hostCm.AddNewCharacter(TargetSeat);
            yield return NetworkTestHelper.WaitUntilSpawnedOrTimeout(hostTarget);
            hostTarget.role = new Role { roleName = "Embrace-Test" };

            var hostPower = _hostNm.SpawnManager.InstantiateAndSpawn(_embracePrefabNo, destroyWithScene: true)
                .GetComponent<PEmbraceOfShadows>();
            // Seat identity is a server-written NetworkVariable (not NGO ownership); this is ctx.OwnerSlot.
            hostPower.ownerClientId.Value = clientId;
            yield return NetworkTestHelper.WaitUntilSpawnedOrTimeout(hostPower);

            yield return NetworkTestHelper.WaitUntilOrTimeout(
                () => FindReplica<PEmbraceOfShadows>(_clientNm, hostPower.NetworkObjectId) != null
                      && _clientCm.GetCharacter(TargetSeat, false) != null,
                10f, "Client replicas of the power / target never arrived.");

            var clientPower = FindReplica<PEmbraceOfShadows>(_clientNm, hostPower.NetworkObjectId);
            Character clientTarget = _clientCm.GetCharacter(TargetSeat, false);
            // role is not a NetworkVariable; set it on the replica the client-side decision reads.
            clientTarget.role = new Role { roleName = "Embrace-Test" };

            // Exercise the effect ROUTING, not the target rules: replace the validator with an empty one
            // (Evaluate() is vacuously true) so CheckIsTargetValid passes on the replica.
            ReflectionHelper.SetPrivateField(clientPower, "targetValidator",
                new Validator<(ulong targetId, TargetUtils.TargetType targetType)>());

            var matchingRole = new Role { roleName = "Embrace-Test", ownerClientId = TargetSeat };

            Assert.AreEqual(RevealLevel.False,
                _clientRevealer.GetCharacterInfo(TargetSeat, clientId).isCorruptRevealed,
                "Precondition: client revealer starts with no corruption reveal.");

            // Cast on the CLIENT replica (the caster's own client). The fixed path runs the owner-local
            // effects here; the buggy SendTo.Server path would run them on the host instead.
            ReflectionHelper.InvokePrivateMethod(clientPower, "OnCharacterAndRolePicked", clientTarget, matchingRole);
            yield return null;
            yield return null;

            Assert.AreEqual(RevealLevel.Personal,
                _clientRevealer.GetCharacterInfo(TargetSeat, clientId).isCorruptRevealed,
                "The corruption reveal must land in the CASTER's (client) revealer.");
            Assert.AreEqual(RevealLevel.False,
                _hostRevealer.GetCharacterInfo(TargetSeat, clientId).isCorruptRevealed,
                "The owner-local reveal must NOT land on the host — that was the regression.");
        }

        /// <summary>
        /// Same boundary, second power: when the CLIENT casts Cursed Vision on a target, the target-
        /// corruption reveal must land in the CLIENT's revealer, not the host's. CursedVision also emits a
        /// card marker and an "élu" verdict chat, but both resolve to SHARED singletons (one instance across
        /// both NMs here), so they are not per-NM discriminating — the reveal is. They only need to not NRE.
        /// </summary>
        [UnityTest]
        public IEnumerator CursedVisionCastByClient_RevealsOnClientRevealer_NotHost()
        {
            ulong clientId = _clientNm.LocalClientId;

            // CursedVision's decision corrupts + self-reveals the OWNER too, so the owner seat needs a real
            // Character alongside the target.
            Character hostOwner = _hostCm.AddNewCharacter(clientId);
            Character hostTarget = _hostCm.AddNewCharacter(TargetSeat);
            yield return NetworkTestHelper.WaitUntilAllSpawnedOrTimeout(5f, hostOwner, hostTarget);
            // Chosen faction => the "élu" verdict branch; the reveal itself is faction-independent.
            hostTarget.role = new Role { factionType = FactionType.chosen, roleName = "Cursed-Test" };

            var hostPower = _hostNm.SpawnManager.InstantiateAndSpawn(_cursedPrefabNo, destroyWithScene: true)
                .GetComponent<PCursedVision>();
            hostPower.ownerClientId.Value = clientId;
            yield return NetworkTestHelper.WaitUntilSpawnedOrTimeout(hostPower);

            yield return NetworkTestHelper.WaitUntilOrTimeout(
                () => FindReplica<PCursedVision>(_clientNm, hostPower.NetworkObjectId) != null
                      && _clientCm.GetCharacter(TargetSeat, false) != null
                      && _clientCm.GetCharacter(clientId, false) != null,
                10f, "Client replicas of the power / target / owner never arrived.");

            var clientPower = FindReplica<PCursedVision>(_clientNm, hostPower.NetworkObjectId);
            // CursedVision corrupts + self-reveals the OWNER (ctx.OwnerSlot = ownerClientId.Value), so the
            // seat NetworkVariable must have replicated to the client replica before the cast — otherwise the
            // owner slot reads 0 and CorruptPlayer(0) dereferences a missing Character.
            yield return NetworkTestHelper.WaitUntilOrTimeout(
                () => clientPower.ownerClientId.Value == clientId,
                5f, "Client power replica never received the owner seat id.");

            Character clientTarget = _clientCm.GetCharacter(TargetSeat, false);
            clientTarget.role = new Role { factionType = FactionType.chosen, roleName = "Cursed-Test" };

            ReflectionHelper.SetPrivateField(clientPower, "targetValidator",
                new Validator<(ulong targetId, TargetUtils.TargetType targetType)>());

            Assert.AreEqual(RevealLevel.False,
                _clientRevealer.GetCharacterInfo(TargetSeat, clientId).isCorruptRevealed,
                "Precondition: client revealer starts with no corruption reveal.");

            // The fixture seeds no card-effect vocabulary, so AddCardEffect logs one error and returns (a
            // shared-singleton effect dispatched AFTER the discriminating reveal). Allow exactly that log.
            LogAssert.Expect(LogType.Error, new Regex("No card effect found for ID"));

            ReflectionHelper.InvokePrivateMethod(clientPower, "OnCharacterPicked", clientTarget);
            yield return null;
            yield return null;

            Assert.AreEqual(RevealLevel.Personal,
                _clientRevealer.GetCharacterInfo(TargetSeat, clientId).isCorruptRevealed,
                "The corruption reveal must land in the CASTER's (client) revealer.");
            Assert.AreEqual(RevealLevel.False,
                _hostRevealer.GetCharacterInfo(TargetSeat, clientId).isCorruptRevealed,
                "The owner-local reveal must NOT land on the host — that was the regression.");
        }

        /// <summary>
        /// PLackOfAffection is the TARGET-side variant: the decision resolves on the CONTACTED target's
        /// client (dispatched inside OnPlayerContactedRpc, keyed by IsTrueLocalTarget = GetLocalClientId() ==
        /// targetClientId), not the caster's. So the discriminating machine is the CLIENT NM acting as the
        /// target: we invoke the RPC body directly on the client's power replica with targetClientId == the
        /// client's own LocalClientId, and assert the SENDER's role reveal lands in the CLIENT's revealer,
        /// not the host's.
        /// </summary>
        [UnityTest]
        public IEnumerator LackOfAffectionContactedOnClient_RevealsSenderRoleOnClientRevealer_NotHost()
        {
            ulong clientId = _clientNm.LocalClientId; // the CONTACTED TARGET's real NGO identity
            const ulong SenderSeat = 777UL;           // caster's seat — distinct from clientId so the
                                                      // own-role auto-bump (subject == observer) can't mask it

            // The reveal's SUBJECT is the sender seat, so it needs a real Character too (GetCharacterInfo ->
            // AddCharacterToInfoList dereferences it); the target seat's faction gates the reveal branch.
            Character hostSender = _hostCm.AddNewCharacter(SenderSeat);
            Character hostTarget = _hostCm.AddNewCharacter(clientId);
            yield return NetworkTestHelper.WaitUntilAllSpawnedOrTimeout(5f, hostSender, hostTarget);
            hostSender.role = new Role { roleName = "Sender-Test" };
            hostTarget.role = new Role { factionType = FactionType.chosen, roleName = "Target-Test" };

            var hostPower = _hostNm.SpawnManager.InstantiateAndSpawn(_lackPrefabNo, destroyWithScene: true)
                .GetComponent<PLackOfAffection>();
            hostPower.ownerClientId.Value = SenderSeat;
            yield return NetworkTestHelper.WaitUntilSpawnedOrTimeout(hostPower);

            yield return NetworkTestHelper.WaitUntilOrTimeout(
                () => FindReplica<PLackOfAffection>(_clientNm, hostPower.NetworkObjectId) != null
                      && _clientCm.GetCharacter(clientId, false) != null
                      && _clientCm.GetCharacter(SenderSeat, false) != null,
                10f, "Client replicas of the power / target / sender never arrived.");

            // role is not a NetworkVariable; set the target faction on the replica the client-side decision reads.
            _clientCm.GetCharacter(clientId, false).role = new Role { factionType = FactionType.chosen, roleName = "Target-Test" };

            Assert.AreEqual(RevealLevel.False,
                _clientRevealer.GetCharacterInfo(SenderSeat, clientId).isRoleRevealed,
                "Precondition: client revealer starts with no role reveal for the sender seat.");

            // OnPlayerContactedRpc is [Rpc(SendTo.SpecifiedInParams)] — it cannot be invoked directly to
            // "receive" (that triggers a SEND that needs a target). Drive it end-to-end exactly like
            // production: send from the host power, targeted at the client, so NGO delivers it to the
            // client's replica which runs the body (RunClientDecisionEffects on the client). GetSafeRpcTarget
            // is the same wrapper the power uses (bot-safe).
            RpcParams rpcTarget = _hostCm.GetSafeRpcTarget(clientId);
            ReflectionHelper.InvokePrivateMethod(hostPower, "OnPlayerContactedRpc", clientId, SenderSeat, rpcTarget);

            yield return NetworkTestHelper.WaitUntilOrTimeout(
                () => _clientRevealer.GetCharacterInfo(SenderSeat, clientId).isRoleRevealed == RevealLevel.Personal,
                5f, "The sender's role reveal never reached the contacted TARGET's (client) revealer.");

            Assert.AreEqual(RevealLevel.False,
                _hostRevealer.GetCharacterInfo(SenderSeat, clientId).isRoleRevealed,
                "The target-local reveal must NOT land on the host.");
        }

        // --- helpers (MultiClientGameFixture pattern) ---

        private GameObject MakePrefab(string name, uint hash, System.Action<GameObject> addComponents)
        {
            var go = new GameObject(name);
            var no = go.AddComponent<NetworkObject>();
            SetGlobalObjectIdHash(no, hash);
            MarkAsNonSceneObject(no);
            addComponents(go);
            return go;
        }

        private void ConfigureNm(NetworkManager nm, GameObject go)
        {
            var transport = go.AddComponent<UnityTransport>();
            transport.SetConnectionData("127.0.0.1", LoopbackPort);
            nm.NetworkConfig = new NetworkConfig { NetworkTransport = transport, EnableSceneManagement = false };
            nm.NetworkConfig.Prefabs.Add(new NetworkPrefab { Prefab = _gmPrefabGo });
            nm.NetworkConfig.Prefabs.Add(new NetworkPrefab { Prefab = _cmPrefabGo });
            nm.NetworkConfig.Prefabs.Add(new NetworkPrefab { Prefab = _characterPrefabGo });
            nm.NetworkConfig.Prefabs.Add(new NetworkPrefab { Prefab = _rtsPrefabGo });
            nm.NetworkConfig.Prefabs.Add(new NetworkPrefab { Prefab = _embracePrefabGo });
            nm.NetworkConfig.Prefabs.Add(new NetworkPrefab { Prefab = _cursedPrefabGo });
            nm.NetworkConfig.Prefabs.Add(new NetworkPrefab { Prefab = _lackPrefabGo });
        }

        private static GameInfoRevealer MakeStandaloneRevealer(out GameObject go, CharacterManager cm)
        {
            go = new GameObject("StandaloneRevealer");
            go.SetActive(false); // no Start() -> no scene-wiring assert; methods still callable
            var rev = go.AddComponent<GameInfoRevealer>();
            ReflectionHelper.SetPrivateField(rev, "characterManager", cm);
            return rev;
        }

        private static T FindReplica<T>(NetworkManager nm, ulong networkObjectId) where T : Component
        {
            if (nm.SpawnManager.SpawnedObjects.TryGetValue(networkObjectId, out var no))
                return no.GetComponent<T>();
            return null;
        }

        private static GameObject MakeBoundRoot(NetworkManager nm, GameInfoRevealer revealer, CharacterManager cm)
        {
            var go = new GameObject("BoundRoot_" + nm.name);
            go.SetActive(false);
            var root = go.AddComponent<CompositionRoot>();
            ReflectionHelper.SetPrivateField(root, "gameInfoRevealer", revealer);
            ReflectionHelper.SetPrivateField(root, "characterManager", cm);
            ReflectionHelper.SetPrivateField(root, "_networkManager", nm);
            Registry()[nm] = root;
            return go;
        }

        private static void UnregisterBoundRoot(NetworkManager nm)
        {
            if (nm == null) return;
            var reg = Registry();
            if (reg.Contains(nm)) reg.Remove(nm);
        }

        private static System.Collections.IDictionary Registry() =>
            (System.Collections.IDictionary)typeof(CompositionRoot)
                .GetField("s_byNetworkManager", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);

        private static void SetGlobalObjectIdHash(NetworkObject no, uint hash) =>
            typeof(NetworkObject).GetField("GlobalObjectIdHash",
                BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public).SetValue(no, hash);

        private static void MarkAsNonSceneObject(NetworkObject no) =>
            typeof(NetworkObject).GetProperty("IsSceneObject",
                BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public).SetValue(no, (bool?)false);

        private static void ResetManagerStatics()
        {
            typeof(CharacterManager)
                .GetField("instance", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
                .SetValue(null, null);
            typeof(GameManager)
                .GetProperty("instance", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
                .SetValue(null, null);
        }
    }
}
