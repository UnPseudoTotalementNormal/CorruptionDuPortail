using CorruptionDuPortail.Domain;
using NUnit.Framework;

namespace Tests.Editor
{
    /// <summary>
    /// NET-11 server chat membership: grant / revoke / public channels, and the read-only views (members of a
    /// channel, channels of a member) that autoplay and tests read to know who should receive a private message.
    /// </summary>
    [Category("ChatMembership")]
    public class ChatMembershipTests
    {
        private const int General = 0;
        private const int Server = -1;
        private const int Anomaly = 1;
        private const int Ink = 4242;

        private ChatMembership _membership;

        [SetUp]
        public void SetUp() => _membership = new ChatMembership(General, Server);

        [Test]
        public void PublicChannels_EveryoneIsMember_AndCannotBeGranted()
        {
            Assert.IsTrue(_membership.IsMember(General, 7));
            Assert.IsFalse(_membership.Grant(General, 7));
            CollectionAssert.IsEmpty(_membership.ChannelsOf(7));
        }

        [Test]
        public void ChannelsOf_ListsOnlyThePrivateChannelsOfThatMember_Sorted()
        {
            _membership.Grant(Ink, 3);
            _membership.Grant(Anomaly, 3);
            _membership.Grant(Anomaly, 101);

            CollectionAssert.AreEqual(new[] { Anomaly, Ink }, _membership.ChannelsOf(3));
            CollectionAssert.AreEqual(new[] { Anomaly }, _membership.ChannelsOf(101));
            CollectionAssert.IsEmpty(_membership.ChannelsOf(5));
        }

        [Test]
        public void Revoke_RemovesTheChannelFromTheMemberView()
        {
            _membership.Grant(Ink, 3);
            _membership.Grant(Ink, 8);

            Assert.IsTrue(_membership.Revoke(Ink, 3));

            CollectionAssert.IsEmpty(_membership.ChannelsOf(3));
            CollectionAssert.AreEqual(new ulong[] { 8 }, _membership.MembersOf(Ink));
        }
    }
}
