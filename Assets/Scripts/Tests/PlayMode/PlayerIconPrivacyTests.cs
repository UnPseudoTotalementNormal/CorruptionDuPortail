using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using CorruptionDuPortail.Domain.PlayerIcons;
using GameLogic;
using GameLogic.GameStates;
using NUnit.Framework;
using Tests.PlayMode.Desingleton;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.TestTools;

namespace Tests.PlayMode
{
    /// <summary>
    /// THE critical test of the private player-icon system, over a REAL second NetworkManager (host + one
    /// genuine in-process client). The whole subsystem exists to make a marker visible to exactly ONE
    /// viewer; the corresponding leak would be structurally INVISIBLE in a host-only playtest, because the
    /// host legitimately sees every marker. Only a second peer can prove the absence of the leak.
    ///
    /// Covered: host-destined marker never reaches the client; client-destined marker never reaches the
    /// host; a peer that is served after markers already exist receives its own slice (the late-joiner
    /// push); and a simulated bot (clientId >= 100) is intercepted by the host via GetSafeRpcTarget
    /// instead of being sent over the wire.
    /// </summary>
    [Category("PlayerIcons")]
    public class PlayerIconPrivacyTests : MultiClientGameFixture
    {
        private const uint IconManagerPrefabHash = 0xC0DE0201u;

        // Arbitrary, distinct marker ids. In production an icon id is the declaring Power's
        // NetworkObjectId; the manager never dereferences it, so plain constants are honest here.
        private const ulong HostIconId = 4242UL;
        private const ulong ClientIconId = 7373UL;
        private const ulong BotIconId = 9191UL;

        private const ulong MarkedPlayerId = 3UL;

        private GameObject _iconManagerPrefabGo;
        private NetworkObject _iconManagerPrefabNo;

        private PlayerIconManager _hostIcons;
        private PlayerIconManager _clientIcons;

        // Declared by the awakening test only. Domain reload is disabled, so it must be destroyed and
        // removed from the (shared) GameManager again or it leaks into the rest of the PlayMode session.
        private AwakeningState _fixtureAwakeningState;

        [TearDown]
        public void DestroyFixtureAwakeningState()
        {
            if (_fixtureAwakeningState == null)
            {
                return;
            }
            if (HostGm != null)
            {
                HostGm.gameStates.Remove(_fixtureAwakeningState);
            }
            UnityEngine.Object.Destroy(_fixtureAwakeningState);
            _fixtureAwakeningState = null;
        }

        /// <summary>
        /// Fires the state's own <c>onStateStartServer</c>. <c>AwakeningState.OnStartStateServer()</c> cannot
        /// be called here — it drives the entire awakening (characters, audio, timers) — and a C# event
        /// cannot be raised from outside its declaring type, so the event is raised through its backing
        /// field. The subscription itself is what is under test: a null handler means PlayerIconManager
        /// never hooked this state, which is the silent failure mode.
        /// </summary>
        private static void RaiseStateStartServer(GameState _state)
        {
            FieldInfo _field = typeof(GameState).GetField("onStateStartServer",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(_field,
                "GameState.onStateStartServer went missing — the awakening purge has no trigger to hook.");

            var _handler = (Action)_field.GetValue(_state);
            Assert.IsNotNull(_handler,
                "Nothing is subscribed to this AwakeningState's onStateStartServer — PlayerIconManager " +
                "never wired the awakening purge through GameManager.GetGameStates.");
            _handler.Invoke();
        }

        protected override void BuildExtraNetworkPrefabs(List<GameObject> _templates)
        {
            _iconManagerPrefabGo = new GameObject("FixtureIconManagerPrefab");
            _iconManagerPrefabNo = _iconManagerPrefabGo.AddComponent<NetworkObject>();
            SetGlobalObjectIdHash(_iconManagerPrefabNo, IconManagerPrefabHash);
            MarkAsNonSceneObject(_iconManagerPrefabNo);
            _iconManagerPrefabGo.AddComponent<PlayerIconManager>();
            _templates.Add(_iconManagerPrefabGo);
        }

        /// <summary>
        /// Spawns the real manager on the host and waits for the CLIENT NM's own replica to register in
        /// its For(nm) registry. Both peers then hold a genuinely distinct manager instance, which is what
        /// makes "the client never received it" a meaningful assertion.
        /// </summary>
        private IEnumerator SpawnIconManagers()
        {
            _hostIcons = HostNm.SpawnManager.InstantiateAndSpawn(_iconManagerPrefabNo, destroyWithScene: true)
                .GetComponent<PlayerIconManager>();

            yield return NetworkTestHelper.WaitUntilSpawnedOrTimeout(_hostIcons, 5f);
            yield return NetworkTestHelper.WaitUntilOrTimeout(
                () => PlayerIconManager.For(ClientNm) != null,
                10f,
                "Client replica of the PlayerIconManager never registered (replication did not complete).");

            _clientIcons = PlayerIconManager.For(ClientNm);
            Assert.AreNotSame(_hostIcons, _clientIcons, "Host and client managers must be distinct instances.");
        }

        private static IEnumerator Settle()
        {
            // A targeted RPC needs a real tick to travel. Several frames, so "the client never got it" is
            // an observation of a settled system rather than of a race we won.
            for (int _i = 0; _i < 10; _i++)
            {
                yield return null;
            }
        }

        /// <summary>What this peer holds AS ITSELF.</summary>
        private static bool Holds(PlayerIconManager _manager, ulong _iconId, ulong _markedClientId)
        {
            foreach (var _entry in _manager.GetLocalIcons())
            {
                if (_entry.IconId == _iconId && _entry.MarkedClientId == _markedClientId)
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>What this peer holds ON BEHALF OF another viewer (the intercepted simulated-bot case).</summary>
        private static bool HoldsForViewer(PlayerIconManager _manager, ulong _viewerClientId, ulong _iconId, ulong _markedClientId)
        {
            foreach (var _entry in _manager.GetLocalIconsForViewer(_viewerClientId))
            {
                if (_entry.IconId == _iconId && _entry.MarkedClientId == _markedClientId)
                {
                    return true;
                }
            }
            return false;
        }

        [UnityTest]
        public IEnumerator MarkerForTheClient_ReachesTheClient_AndNeverTheHost()
        {
            yield return SpawnIconManagers();

            ulong _clientId = ClientNm.LocalClientId;
            _hostIcons.AddIcon(ClientIconId, MarkedPlayerId, _clientId, PlayerIconLifetime.Persistent);

            yield return NetworkTestHelper.WaitUntilOrTimeout(
                () => Holds(_clientIcons, ClientIconId, MarkedPlayerId),
                5f,
                "The viewer's own client never received its slice.");
            yield return Settle();

            Assert.IsFalse(Holds(_hostIcons, ClientIconId, MarkedPlayerId),
                "INFORMATION LEAK: a marker destined for the client landed in the host's own visible slice.");
            Assert.AreEqual(0, _hostIcons.GetLocalIcons().Count,
                "INFORMATION LEAK: the host holds a locally-visible icon it was never the viewer of.");
        }

        [UnityTest]
        public IEnumerator MarkerForTheHost_StaysOnTheHost_AndNeverReachesTheClient()
        {
            yield return SpawnIconManagers();

            // The viewer IS the server: the manager short-circuits and applies the slice locally, no RPC.
            _hostIcons.AddIcon(HostIconId, MarkedPlayerId, NetworkManager.ServerClientId, PlayerIconLifetime.Persistent);

            yield return NetworkTestHelper.WaitUntilOrTimeout(
                () => Holds(_hostIcons, HostIconId, MarkedPlayerId),
                5f,
                "The host never applied its own slice locally (the server short-circuit did not fire).");
            yield return Settle();

            Assert.AreEqual(0, _clientIcons.GetLocalIcons().Count,
                "INFORMATION LEAK: the real client received a marker whose only viewer is the host.");
        }

        [UnityTest]
        public IEnumerator ServerTable_KeepsTheTwoViewersApart()
        {
            yield return SpawnIconManagers();

            ulong _clientId = ClientNm.LocalClientId;
            _hostIcons.AddIcon(HostIconId, MarkedPlayerId, NetworkManager.ServerClientId, PlayerIconLifetime.Persistent);
            _hostIcons.AddIcon(ClientIconId, MarkedPlayerId, _clientId, PlayerIconLifetime.Persistent);
            yield return Settle();

            Assert.AreEqual(1, _hostIcons.GetServerMarkersFor(NetworkManager.ServerClientId).Count);
            Assert.AreEqual(1, _hostIcons.GetServerMarkersFor(_clientId).Count);
            Assert.AreEqual(HostIconId, _hostIcons.GetServerMarkersFor(NetworkManager.ServerClientId)[0].IconId);
            Assert.AreEqual(ClientIconId, _hostIcons.GetServerMarkersFor(_clientId)[0].IconId);

            // And each peer still sees only its own row.
            Assert.IsTrue(Holds(_hostIcons, HostIconId, MarkedPlayerId));
            Assert.IsFalse(Holds(_hostIcons, ClientIconId, MarkedPlayerId));
            Assert.IsTrue(Holds(_clientIcons, ClientIconId, MarkedPlayerId));
            Assert.IsFalse(Holds(_clientIcons, HostIconId, MarkedPlayerId));
        }

        [UnityTest]
        public IEnumerator ExactDuplicateMarker_IsDeduplicated()
        {
            yield return SpawnIconManagers();

            ulong _clientId = ClientNm.LocalClientId;
            _hostIcons.AddIcon(ClientIconId, MarkedPlayerId, _clientId, PlayerIconLifetime.Persistent);
            _hostIcons.AddIcon(ClientIconId, MarkedPlayerId, _clientId, PlayerIconLifetime.Persistent);
            yield return Settle();

            Assert.AreEqual(1, _hostIcons.GetServerMarkersFor(_clientId).Count,
                "The same (icon, marked, viewer) triple must be kept exactly once.");
            Assert.AreEqual(1, _clientIcons.GetLocalIcons().Count);
        }

        /// <summary>
        /// The purge is exercised through its REAL WIRING, not by calling ClearTransientMarkers(): a real
        /// AwakeningState is declared on the GameManager BEFORE the manager spawns, so PlayerIconManager has
        /// to find it through its production path (CompositionRoot -> GameManager.GetGameStates ->
        /// onStateStartServer) for anything at all to happen here. Calling the purge directly would prove the
        /// filtering and nothing about the subscription — and a missing subscription is precisely the silent
        /// failure iteration 2 is about.
        /// </summary>
        [UnityTest]
        public IEnumerator AwakeningStart_PurgesOnlyTheTransientMarkers_ThroughItsRealWiring()
        {
            _fixtureAwakeningState = ScriptableObject.CreateInstance<AwakeningState>();
            HostGm.gameStates.Add(_fixtureAwakeningState, new GameStateSettings());

            yield return SpawnIconManagers();

            ulong _clientId = ClientNm.LocalClientId;
            _hostIcons.AddIcon(ClientIconId, MarkedPlayerId, _clientId, PlayerIconLifetime.Persistent);
            _hostIcons.AddIcon(BotIconId, MarkedPlayerId, _clientId, PlayerIconLifetime.ClearAtAwakeningStart);

            yield return NetworkTestHelper.WaitUntilOrTimeout(
                () => _clientIcons.GetLocalIcons().Count == 2,
                5f,
                "The client never received both markers.");

            RaiseStateStartServer(_fixtureAwakeningState);

            yield return NetworkTestHelper.WaitUntilOrTimeout(
                () => _clientIcons.GetLocalIcons().Count == 1,
                5f,
                "The purged slice never reached the client.");
            yield return Settle();

            Assert.IsTrue(Holds(_clientIcons, ClientIconId, MarkedPlayerId), "A Persistent marker must survive the purge.");
            Assert.IsFalse(Holds(_clientIcons, BotIconId, MarkedPlayerId), "A ClearAtAwakeningStart marker must be gone.");
        }

        [UnityTest]
        public IEnumerator ClientServedAfterMarkersExist_ReceivesItsOwnSliceOnly()
        {
            yield return SpawnIconManagers();

            ulong _clientId = ClientNm.LocalClientId;
            _hostIcons.AddIcon(HostIconId, MarkedPlayerId, NetworkManager.ServerClientId, PlayerIconLifetime.Persistent);
            _hostIcons.AddIcon(ClientIconId, MarkedPlayerId, _clientId, PlayerIconLifetime.Persistent);
            yield return Settle();

            // Replay the server-side connect handler for an already-populated table — the same code path a
            // genuine late joiner takes (OnClientConnectedCallback -> PushSliceTo). Reached by reflection
            // because NGO offers no way to raise its own connect callback from a test.
            MethodInfo _onClientConnected = typeof(PlayerIconManager)
                .GetMethod("OnClientConnected", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(_onClientConnected,
                "PlayerIconManager.OnClientConnected went missing — the late-joiner push has no server-side trigger.");
            _onClientConnected.Invoke(_hostIcons, new object[] { _clientId });

            yield return Settle();

            Assert.IsTrue(Holds(_clientIcons, ClientIconId, MarkedPlayerId),
                "A peer served after markers already exist must receive its own slice.");
            Assert.AreEqual(1, _clientIcons.GetLocalIcons().Count,
                "INFORMATION LEAK: the late-joiner push handed the client more than its own slice.");
        }

        /// <summary>
        /// The host and an intercepted simulated bot must COEXIST on the same peer. GetSafeRpcTarget routes
        /// clientId >= 100 to the host (client 0), so the host applies the bot's slice itself — and with a
        /// single flat local list that application wiped the host's OWN icons. The host is therefore given a
        /// marker FIRST, and the assertion is that it SURVIVES.
        /// </summary>
        [UnityTest]
        public IEnumerator SimulatedBotMarker_IsInterceptedByTheHost_WithoutErasingTheHostsOwnSlice()
        {
            yield return SpawnIconManagers();

            _hostIcons.AddIcon(HostIconId, MarkedPlayerId, NetworkManager.ServerClientId, PlayerIconLifetime.Persistent);
            yield return NetworkTestHelper.WaitUntilOrTimeout(
                () => Holds(_hostIcons, HostIconId, MarkedPlayerId),
                5f,
                "The host never applied its own slice locally (the server short-circuit did not fire).");

            _hostIcons.AddIcon(BotIconId, MarkedPlayerId, SimulatedBotClientId, PlayerIconLifetime.Persistent);
            yield return NetworkTestHelper.WaitUntilOrTimeout(
                () => HoldsForViewer(_hostIcons, SimulatedBotClientId, BotIconId, MarkedPlayerId),
                5f,
                "The simulated bot's slice was not intercepted by the host (GetSafeRpcTarget wrap missing?).");
            yield return Settle();

            // THE regression this test exists for: both slices live on the host, neither overwrites the other.
            Assert.IsTrue(Holds(_hostIcons, HostIconId, MarkedPlayerId),
                "The host's OWN marker was erased when it intercepted the simulated bot's slice.");
            Assert.AreEqual(1, _hostIcons.GetLocalIcons().Count,
                "The host's own slice must still hold exactly its own marker.");
            Assert.IsFalse(Holds(_hostIcons, BotIconId, MarkedPlayerId),
                "The bot's marker bled into the slice the host sees AS ITSELF.");
            Assert.AreEqual(1, _hostIcons.GetLocalIconsForViewer(SimulatedBotClientId).Count,
                "The bot's own slice must hold exactly the bot's marker.");

            Assert.AreEqual(0, _clientIcons.GetLocalIcons().Count,
                "INFORMATION LEAK: a simulated bot's (or the host's) marker travelled to the real client.");
            Assert.AreEqual(1, _hostIcons.GetServerMarkersFor(SimulatedBotClientId).Count,
                "The bot's marker must still be recorded under the BOT's viewer id on the server.");
        }
    }
}
