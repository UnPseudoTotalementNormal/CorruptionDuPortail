using System.Reflection;
using System.Text.RegularExpressions;
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
        public void CreateNewClientData_Empty_FallsBackToDefaultPseudo()
        {
            // NET-02: an empty name no longer replicates an empty pseudo; it falls back to the "Player####" default.
            LocalPlayerInfoHolder.CreateNewClientData("");
            StringAssert.IsMatch(@"^Player\d+$", LocalPlayerInfoHolder.playerInfo.playerName.ToString());
        }

        // Story 13.0 — the domain-reload-disabled reset restores a fresh "Player####" default so a name set
        // in a prior Play session does not leak into the next (AC#3). Invoked via reflection because it is a
        // private editor-only [RuntimeInitializeOnLoadMethod]; EditMode runs in the editor so it is compiled.
        [Test]
        public void Reset_RestoresPlayerPrefixedDefault()
        {
            LocalPlayerInfoHolder.CreateNewClientData("Custom#42");
            Assert.AreEqual("Custom", LocalPlayerInfoHolder.playerInfo.playerName.ToString(),
                "guard: a non-default custom name must be set before the reset for this test to be meaningful.");

            var resetMethod = typeof(LocalPlayerInfoHolder)
                .GetMethod("ResetStaticsForDomainReloadDisabled", BindingFlags.NonPublic | BindingFlags.Static);
            Assert.IsNotNull(resetMethod,
                "ResetStaticsForDomainReloadDisabled was not found — renamed or removed? (keep the editor reset).");
            resetMethod.Invoke(null, null);

            string name = LocalPlayerInfoHolder.playerInfo.playerName.ToString();
            Assert.IsTrue(Regex.IsMatch(name, @"^Player\d+$"),
                $"Reset default name '{name}' should match the preserved 'Player####' semantics.");
        }

        // Story 13.0 — equality parity battery (AC#1/#7). The hand-written Equals/GetHashCode were dropped in
        // favour of a single tidy IEquatable impl (record struct was rejected by Unity's C# 9 compiler,
        // CS8773). These pin that the equality still compares ALL FOUR fields exactly as the old
        // hand-written set did — the NetworkList<PlayerInfo> element semantics must not move.
        private static PlayerInfo Sample() => new()
        {
            playerName = "Alice",
            playerFullName = "Alice#1234",
            playerClientId = 7,
            playerSteamId = 99,
        };

        [Test]
        public void Equality_IdenticalFields_AreEqualAndHashEqual()
        {
            PlayerInfo a = Sample();
            PlayerInfo b = Sample();

            Assert.IsTrue(a.Equals(b), "Two infos with identical fields must be equal.");
            Assert.IsTrue(a.Equals((object)b), "Equals(object) must agree with Equals(PlayerInfo).");
            Assert.AreEqual(a.GetHashCode(), b.GetHashCode(), "Equal infos must hash-equal.");
        }

        [Test]
        public void Equality_DiffersByPlayerName_NotEqual()
        {
            PlayerInfo a = Sample();
            PlayerInfo b = Sample();
            b.playerName = "Bob";

            Assert.IsFalse(a.Equals(b), "Differing playerName must break equality.");
        }

        [Test]
        public void Equality_DiffersByPlayerFullName_NotEqual()
        {
            PlayerInfo a = Sample();
            PlayerInfo b = Sample();
            b.playerFullName = "Alice#9999";

            Assert.IsFalse(a.Equals(b), "Differing playerFullName must break equality.");
        }

        [Test]
        public void Equality_DiffersByClientId_NotEqual()
        {
            PlayerInfo a = Sample();
            PlayerInfo b = Sample();
            b.playerClientId = 8;

            Assert.IsFalse(a.Equals(b), "Differing playerClientId must break equality.");
        }

        [Test]
        public void Equality_DiffersBySteamId_NotEqual()
        {
            PlayerInfo a = Sample();
            PlayerInfo b = Sample();
            b.playerSteamId = 100;

            Assert.IsFalse(a.Equals(b), "Differing playerSteamId must break equality.");
        }
    }
}
