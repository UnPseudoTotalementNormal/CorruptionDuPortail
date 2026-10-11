using CorruptionDuPortail.Domain;
using NUnit.Framework;

namespace Tests.Editor
{
    /// <summary>Steam build: joining a friend's game from the Steam friends list (rich presence connect string).</summary>
    [Category("Networking")]
    public class SteamJoinRequestTests
    {
        [Test]
        public void ConnectString_RoundTripsTheLobbyId()
        {
            string _connect = SteamJoinRequest.ConnectFor(109775241283371533UL);

            Assert.AreEqual("+connect_lobby 109775241283371533", _connect);
            Assert.IsTrue(SteamJoinRequest.TryParseConnect(_connect, out ulong _id));
            Assert.AreEqual(109775241283371533UL, _id);
        }

        [Test]
        public void LaunchArguments_FindTheLobbyAmongOtherArguments()
        {
            string[] _args = { "CorruptionDuPortail_Steam.exe", "-screen-fullscreen", "0", "+connect_lobby", "109775241283371533" };

            Assert.IsTrue(SteamJoinRequest.TryParseArgs(_args, out ulong _id));
            Assert.AreEqual(109775241283371533UL, _id);
        }

        [Test]
        public void MalformedRequests_AreIgnored()
        {
            Assert.IsFalse(SteamJoinRequest.TryParseConnect(null, out _));
            Assert.IsFalse(SteamJoinRequest.TryParseConnect("+connect_lobby", out _), "no id");
            Assert.IsFalse(SteamJoinRequest.TryParseConnect("+connect_lobby abc", out _));
            Assert.IsFalse(SteamJoinRequest.TryParseConnect("+connect_lobby 0", out _));
            Assert.IsFalse(SteamJoinRequest.TryParseArgs(new[] { "CorruptionDuPortail.exe" }, out _));
            Assert.IsFalse(SteamJoinRequest.TryParseArgs(null, out _));
        }
    }
}
