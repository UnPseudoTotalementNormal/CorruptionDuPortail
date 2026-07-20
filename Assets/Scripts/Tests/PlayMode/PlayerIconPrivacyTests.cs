using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using CorruptionDuPortail.Domain.PlayerIcons;
using GameLogic;
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

        [UnityTest]
        public IEnumerator AwakeningStart_PurgesOnlyTheTransientMarkers()
        {
            yield return SpawnIconManagers();

            ulong _clientId = ClientNm.LocalClientId;
            _hostIcons.AddIcon(ClientIconId, MarkedPlayerId, _clientId, PlayerIconLifetime.Persistent);
            _hostIcons.AddIcon(BotIconId, MarkedPlayerId, _clientId, PlayerIconLifetime.ClearAtAwakeningStart);

            yield return NetworkTestHelper.WaitUntilOrTimeout(
                () => _clientIcons.GetLocalIcons().Count == 2,
                5f,
                "The client never received both markers.");

            _hostIcons.ClearTransientMarkers();

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

        [UnityTest]
        public IEnumerator SimulatedBotMarker_IsInterceptedByTheHost_AndNeverSentOverTheWire()
        {
            yield return SpawnIconManagers();

            // GetSafeRpcTarget routes clientId >= 100 to the host (client 0), so the host processes the
            // bot's slice itself — the bot-debug flow, unchanged.
            _hostIcons.AddIcon(BotIconId, MarkedPlayerId, SimulatedBotClientId, PlayerIconLifetime.Persistent);

            yield return NetworkTestHelper.WaitUntilOrTimeout(
                () => Holds(_hostIcons, BotIconId, MarkedPlayerId),
                5f,
                "The simulated bot's slice was not intercepted by the host (GetSafeRpcTarget wrap missing?).");
            yield return Settle();

            Assert.AreEqual(0, _clientIcons.GetLocalIcons().Count,
                "INFORMATION LEAK: a simulated bot's marker travelled to the real client.");
            Assert.AreEqual(1, _hostIcons.GetServerMarkersFor(SimulatedBotClientId).Count,
                "The bot's marker must still be recorded under the BOT's viewer id on the server.");
        }
    }
}
