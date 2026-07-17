using System.Collections;
using Characters;
using NUnit.Framework;
using Unity.Netcode;
using UnityEngine.TestTools;

namespace Tests.PlayMode.Desingleton
{
    /// <summary>
    /// 2-NM replication for the Character NetworkVariables not yet covered by CharacterStateReplicationTests
    /// (messageLeft, hasSentMessageThisTurn): a server-authoritative write must reach the real remote
    /// client's replica. Under StartHost (host==server, RTT=0) these reads happen in-process with no serialization,
    /// so a one-way / initial-value-only sync bug is invisible there. Reuses the clean MultiClientGameFixture.
    /// </summary>
    public class CharacterMiscStateReplicationTests : MultiClientGameFixture
    {
        [UnityTest]
        public IEnumerator MessageLeft_ServerWrite_ReplicatesToClient()
        {
            Character host = null, client = null;
            yield return SpawnPair(c => host = c, c => client = c);

            Assert.AreEqual(1, client.messageLeft.Value, "Baseline: messageLeft default is 1 on the client replica.");
            host.messageLeft.Value = 5;
            yield return NetworkTestHelper.WaitUntilStableOrTimeout(
                () => client.messageLeft.Value == 5, 5f, 3,
                "messageLeft server write never replicated to the client replica.");
        }

        [UnityTest]
        public IEnumerator HasSentMessageThisTurn_ServerWrite_ReplicatesToClient()
        {
            Character host = null, client = null;
            yield return SpawnPair(c => host = c, c => client = c);

            Assert.IsFalse(client.hasSentMessageThisTurn.Value, "Baseline: not sent this turn on the client.");
            host.hasSentMessageThisTurn.Value = true;
            yield return NetworkTestHelper.WaitUntilStableOrTimeout(
                () => client.hasSentMessageThisTurn.Value, 5f, 3,
                "hasSentMessageThisTurn server write never replicated to the client replica.");
            // And the reset back to false also crosses the wire (not initial-value-only sync).
            host.hasSentMessageThisTurn.Value = false;
            yield return NetworkTestHelper.WaitUntilStableOrTimeout(
                () => !client.hasSentMessageThisTurn.Value, 5f, 3,
                "The reset of hasSentMessageThisTurn (true->false) never replicated.");
        }

        // Spawn one server-owned Character for the client seat and resolve both the host object and the CLIENT's
        // own replica (via ClientNm's SpawnManager — GetCharacter resolves against Singleton=host in this fixture).
        private IEnumerator SpawnPair(System.Action<Character> assignHost, System.Action<Character> assignClient)
        {
            ulong id = ClientNm.LocalClientId;
            yield return SpawnRealCharacterForClient(id);
            Character host = HostCm.GetCharacter(id, false);
            Assert.IsNotNull(host, "Host character missing.");
            Character client = ClientNm.SpawnManager.SpawnedObjects[host.NetworkObjectId].GetComponent<Character>();
            Assert.IsNotNull(client, "Client replica missing.");
            Assert.AreNotSame(host, client, "Must be a distinct client replica.");
            assignHost(host);
            assignClient(client);
        }
    }
}
