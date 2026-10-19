using CorruptionDuPortail.Domain;
using NUnit.Framework;

namespace Tests.Editor
{
    /// <summary>
    /// T22 in-game voice: who hears whom. By day every connected player hears the speaker; at night only the players
    /// sharing a private chat channel with him (anomalies together, the messager with his correspondent). Hearing a
    /// voice at night is deduction information, so a leak here is a game-breaking bug.
    /// </summary>
    [Category("Voice")]
    public class VoiceRoutingTests
    {
        private const int General = 0;
        private const int Server = -1;
        private const int Anomaly = 1;
        private const int Ink = 515100;

        private ChatMembership _membership;

        [SetUp]
        public void SetUp()
        {
            _membership = new ChatMembership(General, Server);
            // Seats 1 and 2 are anomalies; seat 3 is the messager, seat 4 his correspondent; seat 5 has no channel.
            _membership.Grant(Anomaly, 1);
            _membership.Grant(Anomaly, 2);
            _membership.Grant(Ink, 3);
            _membership.Grant(Ink, 4);
        }

        private ulong[] Listeners(ulong _speaker, bool _isNight, params ulong[] _connected) =>
            VoiceRouting.Listeners(_speaker, _isNight, _connected, _membership.ChannelsOf, _membership.MembersOf).ToArray();

        [Test]
        public void Day_EveryoneConnectedHears_ButNotTheSpeaker()
        {
            CollectionAssert.AreEqual(new ulong[] { 0, 1, 2, 4, 5 }, Listeners(3, false, 0, 1, 2, 3, 4, 5));
        }

        [Test]
        public void Night_AnAnomalyIsHeardOnlyByTheOtherAnomalies()
        {
            CollectionAssert.AreEqual(new ulong[] { 2 }, Listeners(1, true, 0, 1, 2, 3, 4, 5));
        }

        [Test]
        public void Night_TheMessagerAndHisCorrespondentHearEachOther()
        {
            CollectionAssert.AreEqual(new ulong[] { 4 }, Listeners(3, true, 0, 1, 2, 3, 4, 5));
            CollectionAssert.AreEqual(new ulong[] { 3 }, Listeners(4, true, 0, 1, 2, 3, 4, 5));
        }

        [Test]
        public void Night_APlayerWithoutPrivateChannelIsHeardByNobody()
        {
            CollectionAssert.IsEmpty(Listeners(5, true, 0, 1, 2, 3, 4, 5));
        }

        [Test]
        public void Night_AnAnomalyWhoIsAlsoCorrespondentReachesBothChannels()
        {
            _membership.Grant(Ink, 2);

            CollectionAssert.AreEqual(new ulong[] { 1, 3, 4 }, Listeners(2, true, 0, 1, 2, 3, 4, 5));
        }

        [Test]
        public void Night_TheMessagerTalkingToAnAnomaly_IsNotHeardByTheOtherAnomalies()
        {
            // The messager (3) inked anomaly 2 instead of seat 4: only 2 hears him, never the other anomaly (1).
            _membership.Revoke(Ink, 4);
            _membership.Grant(Ink, 2);

            CollectionAssert.AreEqual(new ulong[] { 2 }, Listeners(3, true, 0, 1, 2, 3, 4, 5));
        }

        [Test]
        public void Night_ARevokedCorrespondentNoLongerHears()
        {
            _membership.Revoke(Ink, 4);

            CollectionAssert.IsEmpty(Listeners(3, true, 0, 1, 2, 3, 4, 5));
        }

        [Test]
        public void DisconnectedMembersAndSimulatedBotsAreSkipped()
        {
            _membership.Grant(Anomaly, 100);

            CollectionAssert.IsEmpty(Listeners(1, true, 0, 1, 3, 4), "seat 2 left, bot 100 has no connection");
            CollectionAssert.AreEqual(new ulong[] { 0, 3 }, Listeners(1, false, 0, 1, 3));
        }

        [Test]
        public void MaySpeak_AtNightOnlyWithAPrivateChannel()
        {
            Assert.IsTrue(VoiceRouting.MaySpeak(false, false));
            Assert.IsTrue(VoiceRouting.MaySpeak(true, true));
            Assert.IsFalse(VoiceRouting.MaySpeak(true, false));
        }

        [Test]
        public void IsNewerSequence_HandlesTheWrapAround()
        {
            Assert.IsTrue(VoiceRouting.IsNewerSequence(5, 4));
            Assert.IsFalse(VoiceRouting.IsNewerSequence(4, 5));
            Assert.IsFalse(VoiceRouting.IsNewerSequence(4, 4));
            Assert.IsTrue(VoiceRouting.IsNewerSequence(1, ushort.MaxValue));
            Assert.IsFalse(VoiceRouting.IsNewerSequence(ushort.MaxValue, 1));
        }
    }
}
