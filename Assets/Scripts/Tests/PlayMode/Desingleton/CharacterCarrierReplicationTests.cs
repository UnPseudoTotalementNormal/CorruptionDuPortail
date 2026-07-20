using System.Collections;
using Characters;
using NUnit.Framework;
using Unity.Netcode;
using UnityEngine.TestTools;

namespace Tests.PlayMode.Desingleton
{
    /// <summary>
    /// Catalog F — 2-NM replication of the Character state carriers not covered by CharacterStateReplicationTests:
    /// the message-per-turn pair (messageLeft / hasSentMessageThisTurn) and elimination. Each is a server-write
    /// NetworkVariable; a server write must be observed on the real remote client's replica after quiescence, and
    /// the true->false path is included where it applies (catches one-way / dirty-flag sync bugs a StartHost harness
    /// can never see). Reuses the clean MultiClientGameFixture.
    /// </summary>
    public class CharacterCarrierReplicationTests : MultiClientGameFixture
    {
        private IEnumerator SpawnPair(System.Action<Character, Character> assign)
        {
            ulong _seat = ClientNm.LocalClientId;
            yield return SpawnRealCharacterForClient(_seat);
            Character _host = HostCm.GetCharacter(_seat, false);
            Assert.IsNotNull(_host, "Host character missing.");
            Character _client = null;
            yield return WaitForClientReplica<Character>(_host, _c => _client = _c);
            assign(_host, _client);
        }

        // messageLeft (default 1). InfiniteMessage sets it to int.MaxValue; here we pin the raw carrier replicates.
        [UnityTest]
        public IEnumerator MessageLeft_ServerWrite_ObservedOnRemoteClient()
        {
            Character _host = null, _client = null;
            yield return SpawnPair((h, c) => { _host = h; _client = c; });
            Assert.AreEqual(1, _client.messageLeft.Value, "Baseline: default messageLeft is 1 on the client.");

            _host.messageLeft.Value = int.MaxValue;

            yield return NetworkTestHelper.WaitUntilStableOrTimeout(
                () => _client.messageLeft.Value == int.MaxValue, 5f, 3,
                "messageLeft never replicated to the client replica.");
        }

        // hasSentMessageThisTurn: false -> true -> false (the per-turn reset path).
        [UnityTest]
        public IEnumerator HasSentMessageThisTurn_TrueThenReset_ObservedOnRemoteClient()
        {
            Character _host = null, _client = null;
            yield return SpawnPair((h, c) => { _host = h; _client = c; });
            Assert.IsFalse(_client.hasSentMessageThisTurn.Value, "Baseline: not sent this turn.");

            _host.hasSentMessageThisTurn.Value = true;
            yield return NetworkTestHelper.WaitUntilStableOrTimeout(
                () => _client.hasSentMessageThisTurn.Value, 5f, 3,
                "hasSentMessageThisTurn=true never reached the client.");

            _host.hasSentMessageThisTurn.Value = false;
            yield return NetworkTestHelper.WaitUntilStableOrTimeout(
                () => !_client.hasSentMessageThisTurn.Value, 5f, 3,
                "The per-turn reset (true->false) never reached the client.");
        }

        // isEliminated: server write observed on the client.
        [UnityTest]
        public IEnumerator IsEliminated_ServerWrite_ObservedOnRemoteClient()
        {
            Character _host = null, _client = null;
            yield return SpawnPair((h, c) => { _host = h; _client = c; });
            Assert.IsFalse(_client.isEliminated.Value, "Baseline: not eliminated.");

            _host.isEliminated.Value = true;

            yield return NetworkTestHelper.WaitUntilStableOrTimeout(
                () => _client.isEliminated.Value, 5f, 3,
                "isEliminated never replicated to the client replica.");
        }
    }
}
