using CorruptionDuPortail.Domain;
using NUnit.Framework;

namespace Tests.Editor
{
    public class RevealVisibilityRulesTests
    {
        [Test]
        public void HostViewer_RefreshesUi_WhenObserverIsHost()
        {
            Assert.IsTrue(RevealVisibilityRules.ShouldRefreshLocalUi(0, 0));
        }

        [Test]
        public void NonHostViewer_DoesNotRefreshUi_WhenStorageSentinelLeaksIn()
        {
            // The shipped bug: the reveal RPC passed the 0 storage sentinel as the observer id.
            // On a non-host client (localClientId != 0) the UI refresh was silently skipped.
            Assert.IsFalse(RevealVisibilityRules.ShouldRefreshLocalUi(0, 1));
        }

        [Test]
        public void NonHostViewer_RefreshesUi_WhenObserverIsSelf()
        {
            Assert.IsTrue(RevealVisibilityRules.ShouldRefreshLocalUi(1, 1));
        }

        [Test]
        public void BotBrain_NeverRefreshesLocalHumanUi()
        {
            // Simulated bot brains use ids >= 100 and must never flip the local human's card.
            Assert.IsFalse(RevealVisibilityRules.ShouldRefreshLocalUi(101, 1));
        }
    }
}
