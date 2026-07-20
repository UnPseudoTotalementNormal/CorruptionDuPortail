using System.Collections;
using Characters;
using NUnit.Framework;
using Unity.Netcode;
using UnityEngine.TestTools;

namespace Tests.PlayMode.Desingleton
{
    /// <summary>
    /// Catalog I (golden games) — a scripted MULTI-STEP, MULTI-SEAT server sequence whose FINAL state must land
    /// coherently on the real remote client's replicas. The rest of the suite tests single mutations in isolation;
    /// this pins that a sequence (corrupt → heal, then chain a different seat) settles correctly on the client after
    /// quiescence — the "test the verbs together" case a per-mechanic suite structurally misses. Reuses the clean
    /// MultiClientGameFixture (resets its own statics; no leak dependency).
    /// </summary>
    public class GoldenSequenceReplicationTests : MultiClientGameFixture
    {
        private const ulong SeatB = 999UL;

        [UnityTest]
        public IEnumerator ScriptedSequence_CorruptHealThenChainOther_SettlesCoherentlyOnClient()
        {
            ulong _seatA = ClientNm.LocalClientId;
            yield return SpawnRealCharacterForClient(_seatA);
            yield return SpawnRealCharacterForClient(SeatB);

            Character _hostA = HostCm.GetCharacter(_seatA, false);
            Character _hostB = HostCm.GetCharacter(SeatB, false);
            Assert.IsNotNull(_hostA, "Host seat A missing.");
            Assert.IsNotNull(_hostB, "Host seat B missing.");

            // --- Scripted server sequence ---
            // Step 1+2: seat A is corrupted, then healed (heal clears corruption + marks healed).
            _hostA.CorruptPlayerServerRpc();
            _hostA.HealPlayerServerRpc();
            // Step 3: a DIFFERENT seat (B) is chained (which also corrupts it — ChainCharacterServer sets both).
            _hostB.ChainCharacterServer();

            // --- Resolve BOTH client replicas (client NM's own objects, via SpawnManager — not GetCharacter,
            // which resolves against Singleton=host in this 2-NM process). Bounded wait: the spawn helper
            // only guarantees the HOST projection, not the client-side replica.
            Character _clientA = null;
            Character _clientB = null;
            yield return WaitForClientReplica<Character>(_hostA, _c => _clientA = _c);
            yield return WaitForClientReplica<Character>(_hostB, _c => _clientB = _c);

            // --- The FINAL coherent state must settle on the client (all mutations drained).
            yield return NetworkTestHelper.WaitUntilStableOrTimeout(
                () => !_clientA.isCorrupted.Value && _clientA.isHealed.Value
                      && _clientB.isChained.Value && _clientB.isCorrupted.Value,
                10f, 3,
                "The scripted sequence's final state never settled coherently on the client replicas.");

            // Explicit final assertions (post-quiescence) for a clear failure signal.
            Assert.IsFalse(_clientA.isCorrupted.Value, "Seat A: healed, so not corrupted on the client.");
            Assert.IsTrue(_clientA.isHealed.Value, "Seat A: healed on the client.");
            Assert.IsTrue(_clientB.isChained.Value, "Seat B: chained on the client.");
            Assert.IsTrue(_clientB.isCorrupted.Value, "Seat B: chaining also corrupts, observed on the client.");
            // Cross-seat isolation held throughout: A's heal did not clear B, B's chain did not touch A.
            Assert.IsFalse(_clientA.isChained.Value, "Seat A must not be chained by B's chaining.");
            Assert.IsFalse(_clientB.isHealed.Value, "Seat B must not be healed by A's heal.");
        }

    }
}
