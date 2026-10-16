using MessageSystem;
using NUnit.Framework;

namespace Tests.Editor
{
    /// <summary>
    /// When a player can send an anonymous message: the send button works and the letters' lamp is lit (board task T11)
    /// exactly then, at night, with messages left, once per night.
    /// </summary>
    public class MessageAvailabilityTests
    {
        [Test]
        public void AtNight_WithMessagesLeft_NotSentYet_CanSend()
        {
            Assert.IsTrue(MessageAvailability.CanSend(true, 1, false));
            Assert.IsTrue(MessageAvailability.CanSend(true, 1001, false), "the infinite-message power");
        }

        [Test]
        public void ByDay_CannotSend()
        {
            Assert.IsFalse(MessageAvailability.CanSend(false, 3, false));
        }

        [Test]
        public void WithNoMessageLeft_CannotSend()
        {
            Assert.IsFalse(MessageAvailability.CanSend(true, 0, false));
        }

        [Test]
        public void OnceSentThisNight_CannotSendAgain()
        {
            Assert.IsFalse(MessageAvailability.CanSend(true, 2, true));
        }
    }
}
