using System.Collections;
using GameLogic;
using Characters;
using NUnit.Framework;
using Unity.Netcode;
using UnityEngine.TestTools;

namespace Tests.PlayMode.Desingleton
{
    /// <summary>
    /// Story 5.0 self-tests — prove the MultiClientGameFixture works: a real second
    /// client over loopback observes the server's currentGameStateIndex travelling the
    /// wire (a thing StartHost can never exercise), the de-singletonisation invariants
    /// from 5.0e still hold through the fixture, and the optional simulated-bot
    /// interception path is covered.
    /// </summary>
    public class MultiClientGameFixtureTests : MultiClientGameFixture
    {
        // AC 1 + 4 + 5: drive the server index across real ticks and assert the remote
        // client recorded the ordered OnValueChanged sequence over the wire.
        [UnityTest]
        public IEnumerator RemoteClient_RecordsOrderedIndexTrace_AcrossRealTicks()
        {
            // Before any explicit server write, the count of observed entries is
            // timing-dependent: NGO may or may not raise OnValueChanged for the index 0
            // carried in the spawn payload, AND GameManager.OnNetworkSpawn writes
            // currentGameStateIndex.Value = 0 on the server — so the observed pre-write
            // count can be 0, 1, or 2 across machines/ticks (SpawnPayloadFiredInitialValue
            // records whether ANY fired, for 5.3's benefit). We do NOT pin the count;
            // we require only that every pre-write entry is the initial index 0, then key
            // the explicit-transition assertions off the captured baseline.
            int _baseline = RemoteIndexTrace.Count;
            for (int i = 0; i < _baseline; i++)
            {
                Assert.AreEqual(0, RemoteIndexTrace[i],
                    $"Pre-write trace entry {i} must be the initial index 0, got {RemoteIndexTrace[i]}.");
            }

            // Drive 0 -> 1 -> 2 on the server, one real tick between writes.
            SetServerIndex(1);
            yield return WaitForRemoteTraceCount(_baseline + 1);

            SetServerIndex(2);
            yield return WaitForRemoteTraceCount(_baseline + 2);

            // The two explicit writes must appear, in order, as the last two entries.
            int _n = RemoteIndexTrace.Count;
            Assert.AreEqual(1, RemoteIndexTrace[_n - 2], "First explicit transition (->1) missing or out of order.");
            Assert.AreEqual(2, RemoteIndexTrace[_n - 1], "Second explicit transition (->2) missing or out of order.");

            // And it really crossed the wire: the client's replica Value matches.
            Assert.AreEqual(2, ClientGm.currentGameStateIndex.Value,
                "Client replica's index Value did not converge to the server's last write.");
        }

        // AC 2: the de-singletonisation invariants from 5.0e still hold through the
        // fixture — a smoke test so a future regression fails here too.
        [UnityTest]
        public IEnumerator DesingletonInvariants_HoldThroughTheFixture()
        {
            Assert.IsTrue(NetworkManager.Singleton == HostNm, "Host must own NetworkManager.Singleton.");

            Assert.IsNotNull(ClientGm, "Client GameManager replica missing.");
            Assert.IsNotNull(ClientCm, "Client CharacterManager replica missing.");
            Assert.IsTrue(ClientGm.IsSpawned, "Client GameManager replica not spawned.");
            Assert.IsTrue(ClientCm.IsSpawned, "Client CharacterManager replica not spawned.");

            // Façades stay on the host.
            Assert.AreSame(HostGm, GameManager.instance, "GameManager.instance must stay on the host.");
            Assert.AreSame(HostCm, CharacterManager.instance, "CharacterManager.instance must stay on the host.");

            // For(nm) resolves per-NetworkManager instances; client replicas are distinct.
            Assert.AreSame(HostGm, GameManager.For(HostNm), "For(host) must resolve the host's GameManager.");
            Assert.AreSame(ClientGm, GameManager.For(ClientNm), "For(client) must resolve the client's GameManager.");
            Assert.AreNotSame(GameManager.For(HostNm), GameManager.For(ClientNm), "Per-NM GameManagers must differ.");
            Assert.AreNotSame(CharacterManager.For(HostNm), CharacterManager.For(ClientNm), "Per-NM CharacterManagers must differ.");
            yield return null;
        }

        // AC 3: optional simulated-bot interception path (opt-in, additive). Adding the
        // bot proof must not perturb the real-client invariants asserted above.
        [UnityTest]
        public IEnumerator SimulatedBot_IsInterceptedByHost()
        {
            AssertSimulatedBotIsIntercepted();
            yield return null;
        }
    }
}
