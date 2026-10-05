using System;
using Network;
using NUnit.Framework;
using UnityEngine;

namespace Tests.Editor
{
    /// <summary>Rejoin 02: the client-side session (token + how to reach the host) survives in PlayerPrefs.</summary>
    [Category("Networking")]
    public class RejoinSessionStoreTests
    {
        private const string Suffix = "-editmode-tests";

        [SetUp]
        public void UseATestKey()
        {
            RejoinSessionStore.KeySuffix = Suffix;
            RejoinSessionStore.Clear();
        }

        [TearDown]
        public void CleanUp()
        {
            RejoinSessionStore.Clear();
            RejoinSessionStore.SetConnectionTarget(string.Empty, string.Empty, string.Empty);
            RejoinSessionStore.KeySuffix = string.Empty;
        }

        [Test]
        public void Token_IsSaved_WithTheHostItCameFrom()
        {
            RejoinSessionStore.SetConnectionTarget("relay", "JOINCODE", "LOBBY1");
            RejoinSessionStore.Remember("tok-1");

            Assert.IsTrue(RejoinSessionStore.TryLoad(out RejoinSessionStore.Session _session));
            Assert.AreEqual("tok-1", _session.token);
            Assert.AreEqual("relay", _session.hostKind);
            Assert.AreEqual("JOINCODE", _session.hostAddress);
            Assert.AreEqual("LOBBY1", _session.lobbyCode);
            Assert.AreEqual("tok-1", RejoinSessionStore.TokenForConnection());
        }

        [Test]
        public void Clear_LeavesNothingToRejoin()
        {
            RejoinSessionStore.Remember("tok-2");
            RejoinSessionStore.Clear();

            Assert.IsFalse(RejoinSessionStore.TryLoad(out _));
            Assert.AreEqual(string.Empty, RejoinSessionStore.TokenForConnection());
        }

        [Test]
        public void Stale_Session_IsIgnored()
        {
            var _old = new RejoinSessionStore.Session
            {
                token = "old",
                hostKind = "relay",
                savedAtUnixSeconds = DateTimeOffset.UtcNow.ToUnixTimeSeconds() - 7 * 3600,
            };
            PlayerPrefs.SetString("cdp.rejoin.session" + Suffix, JsonUtility.ToJson(_old));

            Assert.IsFalse(RejoinSessionStore.TryLoad(out _), "a session older than a game can last is ignored");
            Assert.AreEqual(string.Empty, RejoinSessionStore.TokenForConnection());
        }
    }
}
