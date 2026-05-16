using Extensions;
using Network;
using NUnit.Framework;

namespace Tests.Editor
{
    public class UlongExtensionsTests
    {
        [Test]
        public void IsFakeClientId_WithRealClientId_ReturnsFalse()
        {
            ulong clientId = 5;
            Assert.IsFalse(clientId.IsFakeClientId());
        }

        [Test]
        public void IsFakeClientId_WithFakeClientId_ReturnsTrue()
        {
            ulong fakeId = GameValues.FAKE_CLIENT_ID - 2;
            Assert.IsTrue(fakeId.IsFakeClientId());
        }

        [Test]
        public void GetPlayerName_WithFakeClientId_ReturnsAIName()
        {
            ulong fakeId = GameValues.FAKE_CLIENT_ID - 3; // Index 3
            string name = fakeId.GetPlayerName();
            
            Assert.AreEqual("AI 3", name);
        }
        
        // Note: GetPlayerName with a real ClientId requires LobbyPlayerInfoHolder.instance
        // which relies on NetworkBehaviour and NetworkList. This is better tested in PlayMode.
    }
}
