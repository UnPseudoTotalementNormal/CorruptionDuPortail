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

        [Test]
        public void Session_NotKeptAlive_PastTheSeatGrace_IsIgnored()
        {
            // The host left (or the seat expired) a while ago: the token was handed long before, the connection last
            // seen longer ago than a seat stays reserved. Nothing to rejoin, the menu must not offer it.
            long _now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            var _gone = new RejoinSessionStore.Session
            {
                token = "gone",
                hostKind = "relay",
                savedAtUnixSeconds = _now - 3600,
                lastSeenUnixSeconds = _now - (long)RejoinSessionStore.FreshSeconds - 1,
            };
            PlayerPrefs.SetString("cdp.rejoin.session" + Suffix, JsonUtility.ToJson(_gone));

            Assert.IsFalse(RejoinSessionStore.TryLoad(out _));
            Assert.AreEqual(string.Empty, RejoinSessionStore.TokenForConnection());
        }

        [Test]
        public void KeepAlive_KeepsAnOldTokenOpen_WhileConnected()
        {
            long _now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            var _seated = new RejoinSessionStore.Session
            {
                token = "seated",
                hostKind = "relay",
                savedAtUnixSeconds = _now - (long)RejoinSessionStore.FreshSeconds + 5,
            };
            PlayerPrefs.SetString("cdp.rejoin.session" + Suffix, JsonUtility.ToJson(_seated));

            RejoinSessionStore.KeepAlive();

            Assert.IsTrue(RejoinSessionStore.TryLoad(out RejoinSessionStore.Session _session));
            Assert.That(_session.lastSeenUnixSeconds, Is.GreaterThanOrEqualTo(_now));
            Assert.AreEqual("seated", _session.token);
        }

        [Test]
        public void KeepAlive_WithoutASession_SavesNothing()
        {
            RejoinSessionStore.KeepAlive();

            Assert.IsFalse(RejoinSessionStore.TryLoad(out _));
        }
    }
}
