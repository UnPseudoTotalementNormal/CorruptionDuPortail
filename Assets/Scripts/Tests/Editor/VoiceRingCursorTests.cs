using CorruptionDuPortail.Domain;
using NUnit.Framework;

namespace Tests.Editor
{
    /// <summary>
    /// T22 voice playback ring: the write head keeps a short lead on the mixer, restarts a prebuffer ahead after a
    /// silence, drops late bursts instead of growing the delay, and reports what the mixer passed so it is cleared.
    /// </summary>
    [Category("Voice")]
    public class VoiceRingCursorTests
    {
        private const int Ring = 1000;
        private const int Prebuffer = 80;
        private const int MaxLead = 400;

        private VoiceRingCursor _cursor;

        [SetUp]
        public void SetUp() => _cursor = new VoiceRingCursor(Ring, Prebuffer, MaxLead);

        [Test]
        public void FirstPacket_IsWrittenOnePrebufferAheadOfTheReader()
        {
            Assert.IsTrue(_cursor.Starved);
            Assert.IsTrue(_cursor.TryPrepareWrite(100, out int _offset));
            Assert.AreEqual(Prebuffer, _offset);
            _cursor.Commit(100);
            Assert.IsFalse(_cursor.Starved);
        }

        [Test]
        public void FollowingPackets_AreWrittenRightAfterThePreviousOne()
        {
            _cursor.TryPrepareWrite(100, out _);
            _cursor.Commit(100);

            Assert.IsTrue(_cursor.TryPrepareWrite(100, out int _offset));
            Assert.AreEqual(Prebuffer + 100, _offset);
        }

        [Test]
        public void Advance_ReportsThePlayedRegion_AcrossTheRingEnd()
        {
            _cursor.Advance(900, out int _offset, out int _length);
            Assert.AreEqual(0, _offset);
            Assert.AreEqual(900, _length);

            _cursor.Advance(50, out _offset, out _length);
            Assert.AreEqual(900, _offset);
            Assert.AreEqual(150, _length, "900 -> 1000 then 0 -> 50");
            Assert.AreEqual(1050, _cursor.Played);
        }

        [Test]
        public void WriteOffsets_WrapAroundTheRing()
        {
            _cursor.Advance(950, out _, out _);

            Assert.IsTrue(_cursor.TryPrepareWrite(100, out int _offset));
            Assert.AreEqual((950 + Prebuffer) % Ring, _offset);
        }

        [Test]
        public void AfterTheReaderCaughtUp_TheNextPacketRestartsOnePrebufferAhead()
        {
            _cursor.TryPrepareWrite(100, out _);
            _cursor.Commit(100);
            _cursor.Advance(300, out _, out _); // played past the 180 written bytes

            Assert.IsTrue(_cursor.Starved);
            Assert.IsTrue(_cursor.TryPrepareWrite(100, out int _offset));
            Assert.AreEqual(300 + Prebuffer, _offset);
        }

        [Test]
        public void ALateBurst_OverTheMaximumLead_IsDropped()
        {
            _cursor.TryPrepareWrite(300, out _);
            _cursor.Commit(300); // lead 380

            Assert.IsFalse(_cursor.TryPrepareWrite(100, out _), "lead would reach 480 > 400");
            Assert.IsTrue(_cursor.TryPrepareWrite(20, out _), "lead 400 is still fine");
        }
    }
}
