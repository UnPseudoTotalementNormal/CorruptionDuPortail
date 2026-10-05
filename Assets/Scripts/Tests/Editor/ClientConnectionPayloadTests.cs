using Network;
using NUnit.Framework;
using Unity.Netcode;
using UnityEngine;

namespace Tests.Editor
{
    /// <summary>
    /// Regression for investigation late-joiner-character-desync: NGO defers object creation for a client that is
    /// still synchronizing GameScene (no time limit) but purges the NetworkVariable / parent deltas sent for those
    /// objects after <see cref="NetworkConfig.SpawnTimeout"/>. A player seated during a load longer than that window
    /// became a ghost (ownerClientId = FAKE_CLIENT_ID) with a missing player for the whole game. Every client start
    /// path goes through <see cref="ClientConnectionPayload.Apply"/>, which must keep the window open for the whole
    /// join the host tolerates (<see cref="JoinHandshake.SyncTotalTimeoutSeconds"/>).
    /// </summary>
    [Category("Networking")]
    public class ClientConnectionPayloadTests
    {
        private GameObject _host;
        private NetworkManager _networkManager;

        [SetUp]
        public void CreateNetworkManager()
        {
            _host = new GameObject("ClientConnectionPayloadTests NetworkManager");
            _networkManager = _host.AddComponent<NetworkManager>();
            _networkManager.NetworkConfig = new NetworkConfig();
        }

        [TearDown]
        public void DestroyNetworkManager()
        {
            if (_host != null)
            {
                Object.DestroyImmediate(_host);
            }
        }

        [Test]
        public void Apply_KeepsDeferredMessagesForTheWholeJoinWindow()
        {
            _networkManager.NetworkConfig.SpawnTimeout = 10f; // the serialized BootScene value

            ClientConnectionPayload.Apply(_networkManager);

            Assert.GreaterOrEqual(_networkManager.NetworkConfig.SpawnTimeout, JoinHandshake.SyncTotalTimeoutSeconds,
                "A client loading longer than SpawnTimeout loses the deltas of objects spawned meanwhile (ghost Character, " +
                "missing player): the window must cover the host's join cap.");
        }

        [Test]
        public void Apply_NeverShortensALongerWindow()
        {
            _networkManager.NetworkConfig.SpawnTimeout = 600f;

            ClientConnectionPayload.Apply(_networkManager);

            Assert.AreEqual(600f, _networkManager.NetworkConfig.SpawnTimeout);
        }
    }
}
