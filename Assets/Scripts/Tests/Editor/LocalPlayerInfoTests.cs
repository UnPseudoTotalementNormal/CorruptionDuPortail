using Network.Player;
using NUnit.Framework;

namespace Tests.Editor
{
    public class LocalPlayerInfoTests
    {
        [Test]
        public void CreateNewClientData_WithHash_ExtractsShortName()
        {
            string fullName = "Gamer#1234";
            LocalPlayerInfoHolder.CreateNewClientData(fullName);
            
            var info = LocalPlayerInfoHolder.playerInfo;
            Assert.AreEqual("Gamer#1234", info.playerFullName.ToString());
            Assert.AreEqual("Gamer", info.playerName.ToString(), "Short name should not include the hash");
        }

        [Test]
        public void CreateNewClientData_WithoutHash_UsesFullName()
        {
            string fullName = "SoloPlayer";
            LocalPlayerInfoHolder.CreateNewClientData(fullName);
            
            var info = LocalPlayerInfoHolder.playerInfo;
            Assert.AreEqual("SoloPlayer", info.playerFullName.ToString());
            Assert.AreEqual("SoloPlayer", info.playerName.ToString());
        }

        [Test]
        public void CreateNewClientData_Empty_HandlesGracefully()
        {
            LocalPlayerInfoHolder.CreateNewClientData("");
            Assert.AreEqual("", LocalPlayerInfoHolder.playerInfo.playerName.ToString());
        }
    }
}
