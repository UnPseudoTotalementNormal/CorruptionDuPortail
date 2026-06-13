using System.Collections;
using GameLogic;
using NUnit.Framework;
using UnityEngine.TestTools;

namespace Tests.PlayMode.Desingleton
{
    /// <summary>
    /// Story 6.3 (AC2) — proves <see cref="CompositionRoot.For"/> resolves the per-NetworkManager
    /// service graph for BOTH in-process NetworkManagers of the <see cref="MultiClientGameFixture"/>
    /// (host + real client), by delegating to the 5.0c/5.0d per-NM registries. Neither NM has a
    /// scene-placed CompositionRoot in the fixture, so this also proves a root *instance* is NOT
    /// required for resolution — the static answers for any NM the manager registries know, which is
    /// what keeps the fixture green with zero fixture changes.
    /// </summary>
    public class CompositionRootResolutionTests : MultiClientGameFixture
    {
        [UnityTest]
        public IEnumerator For_ResolvesPerNetworkManager_GraphForBothClients()
        {
            // Host NM resolves to the host-owned managers.
            Assert.AreSame(HostCm, CompositionRoot.For(HostNm).CharacterManager,
                "CompositionRoot.For(host).CharacterManager must resolve the host's CharacterManager.");
            Assert.AreSame(HostGm, CompositionRoot.For(HostNm).GameManager,
                "CompositionRoot.For(host).GameManager must resolve the host's GameManager.");

            // Client NM (no scene-placed root of its own) resolves to ITS OWN replicas, never the
            // host's — the per-NM isolation the despaghettification track depends on.
            Assert.AreSame(ClientCm, CompositionRoot.For(ClientNm).CharacterManager,
                "CompositionRoot.For(client).CharacterManager must resolve the client replica, not the host's.");
            Assert.AreSame(ClientGm, CompositionRoot.For(ClientNm).GameManager,
                "CompositionRoot.For(client).GameManager must resolve the client replica, not the host's.");

            yield break;
        }
    }
}
