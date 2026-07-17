using System.Collections;
using Characters;
using NUnit.Framework;
using Unity.Netcode;
using UnityEngine.TestTools;

namespace Tests.PlayMode.Desingleton
{
    /// <summary>
    /// A second CLIENT→SERVER leg (distinct from the CorruptPlayerServerRpc one): HealPlayerServerRpc is
    /// [Rpc(SendTo.Server, RequireOwnership=false)], so a non-owner client CAN drive it. Invoking it on the client
    /// replica of a corrupted character must serialize to the host, clear the corruption + set healed on the SERVER
    /// (a client cannot write these server-write NetworkVariables locally, so the host observing the change proves
    /// the RPC travelled), then re-replicate down. Reuses the clean MultiClientGameFixture.
    /// </summary>
    public class ClientToServerHealTests : MultiClientGameFixture
    {
        [UnityTest]
        public IEnumerator HealServerRpc_FromClient_ClearsCorruptionOnServer_ThenReReplicates()
        {
            ulong id = ClientNm.LocalClientId;
            yield return SpawnRealCharacterForClient(id);
            Character host = HostCm.GetCharacter(id, false);
            Assert.IsNotNull(host, "Host character missing.");
            Character client = ClientNm.SpawnManager.SpawnedObjects[host.NetworkObjectId].GetComponent<Character>();
            Assert.IsNotNull(client, "Client replica missing.");

            // Corrupt on the server first so heal has something to clear.
            host.CorruptPlayerServerRpc();
            yield return NetworkTestHelper.WaitUntilStableOrTimeout(
                () => client.isCorrupted.Value, 5f, 3, "Corruption never reached the client replica.");

            // CLIENT invokes the heal ServerRpc on its replica → serializes to the host.
            client.HealPlayerServerRpc();

            // Server state must change (a client cannot write these server-write NVs locally → proof of travel).
            yield return NetworkTestHelper.WaitUntilStableOrTimeout(
                () => host.isHealed.Value && !host.isCorrupted.Value, 5f, 3,
                "The heal ServerRpc sent from the client never mutated the server state.");

            // And the server write re-replicates back down to the client replica.
            yield return NetworkTestHelper.WaitUntilStableOrTimeout(
                () => client.isHealed.Value && !client.isCorrupted.Value, 5f, 3,
                "The server heal never re-replicated to the client replica.");
        }
    }
}
