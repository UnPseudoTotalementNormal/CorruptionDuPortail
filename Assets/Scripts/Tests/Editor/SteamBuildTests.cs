using System.Collections.Generic;
using CorruptionDuPortail.Domain;
using Network.Services;
using NUnit.Framework;

namespace Tests.Editor
{
    /// <summary>
    /// Steam build: Steam lobbies (short join code, host-checked password) and the separation between the Relay and
    /// Steam builds. The Steam calls themselves need a running Steam client and are left to playtests.
    /// </summary>
    [Category("Networking")]
    public class SteamBuildTests
    {
        private sealed class SequenceRandom : IRandomProvider
        {
            private int _next;
            public int Next(int maxExclusive) => maxExclusive == 0 ? 0 : _next++ % maxExclusive;
        }

        [Test]
        public void LobbyCode_IsSixCharactersOfTheUnambiguousAlphabet()
        {
            string _code = LobbyCode.Generate(new SequenceRandom());

            Assert.AreEqual(LobbyCode.Length, _code.Length);
            Assert.AreEqual("ABCDEF", _code);
            foreach (char c in LobbyCode.Alphabet)
            {
                StringAssert.DoesNotContain(c.ToString(), "01OIL", $"look-alike '{c}' in the alphabet");
            }
        }

        [Test]
        public void LobbyCode_LookupIgnoresCaseAndSpaces()
        {
            Assert.AreEqual("AB3K9Z", LobbyCode.Normalize(" ab3 k9z "));
            Assert.AreEqual(string.Empty, LobbyCode.Normalize(null));
        }

        [Test]
        public void LobbyPassword_OpenLobbyAcceptsEveryone_ProtectedLobbyNeedsTheExactPassword()
        {
            Assert.IsTrue(LobbyPasswordGate.Accepts(null, null));
            Assert.IsTrue(LobbyPasswordGate.Accepts(string.Empty, "anything"));
            Assert.IsTrue(LobbyPasswordGate.Accepts("motdepasse", "motdepasse"));
            Assert.IsFalse(LobbyPasswordGate.Accepts("motdepasse", "MotDePasse"));
            Assert.IsFalse(LobbyPasswordGate.Accepts("motdepasse", null));
        }

        [Test]
        public void ConnectionPayload_CarriesTheLobbyPassword_AndStillReadsFormat2()
        {
            var _payload = new ConnectionPayload { BuildVersion = "1.0", PlayerName = "Poyo", RejoinToken = "tok", LobbyPassword = "secret12" };
            Assert.IsTrue(ConnectionPayload.TryParse(_payload.ToBytes(), out ConnectionPayload _read));
            Assert.AreEqual("secret12", _read.LobbyPassword);
            Assert.AreEqual("tok", _read.RejoinToken);

            // A format-2 payload (before Steam lobbies) is the format-3 one without its last string.
            byte[] _format2 = new ConnectionPayload { BuildVersion = "1.0", PlayerName = "Old", RejoinToken = "tok" }.ToBytes();
            _format2[0] = 2;
            System.Array.Resize(ref _format2, _format2.Length - sizeof(ushort));
            Assert.IsTrue(ConnectionPayload.TryParse(_format2, out ConnectionPayload _old));
            Assert.AreEqual("Old", _old.PlayerName);
            Assert.AreEqual("tok", _old.RejoinToken);
            Assert.AreEqual(string.Empty, _old.LobbyPassword);
        }

        [Test]
        public void Rejoin_OnlyOffersGamesOfThisBuildsOnlineStack()
        {
            Assert.IsTrue(OnlineFlavorRules.CanRejoin(OnlineFlavor.Steam, "steam"));
            Assert.IsFalse(OnlineFlavorRules.CanRejoin(OnlineFlavor.Steam, "relay"));
            Assert.IsFalse(OnlineFlavorRules.CanRejoin(OnlineFlavor.Steam, "direct"));

            Assert.IsTrue(OnlineFlavorRules.CanRejoin(OnlineFlavor.Relay, "relay"));
            Assert.IsTrue(OnlineFlavorRules.CanRejoin(OnlineFlavor.Relay, "direct"));
            Assert.IsFalse(OnlineFlavorRules.CanRejoin(OnlineFlavor.Relay, "steam"));
            Assert.IsFalse(OnlineFlavorRules.CanRejoin(OnlineFlavor.Relay, null));
        }

        [Test]
        public void GameLobby_EmptyDataIsAbsent()
        {
            var _lobby = new GameLobby
            {
                Data = new Dictionary<string, string> { { GameLobby.HostSteamIdKey, "76561198000000000" }, { GameLobby.RelayJoinCodeKey, "" } },
            };

            Assert.IsTrue(_lobby.TryGetData(GameLobby.HostSteamIdKey, out string _host));
            Assert.AreEqual("76561198000000000", _host);
            Assert.IsFalse(_lobby.TryGetData(GameLobby.RelayJoinCodeKey, out _), "a Steam lobby has no Relay join code");
            Assert.IsFalse(_lobby.TryGetData("missing", out _));
        }
    }
}
