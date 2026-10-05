using CorruptionDuPortail.Domain;
using NUnit.Framework;

namespace Tests.Editor
{
    /// <summary>Rejoin 02: seat ↔ transport aliases and session tokens (pure).</summary>
    [Category("Networking")]
    public class SeatDirectoryTests
    {
        [Test]
        public void Unaliased_Ids_MapToThemselves()
        {
            var _seats = new SeatDirectory();
            Assert.AreEqual(3UL, _seats.SeatOf(3));
            Assert.AreEqual(3UL, _seats.TransportOf(3));
            Assert.IsFalse(_seats.IsAlias(3));
        }

        [Test]
        public void Rejoined_Transport_PlaysTheOriginalSeat_BothWays()
        {
            var _seats = new SeatDirectory();
            _seats.Bind(9, 3);

            Assert.AreEqual(3UL, _seats.SeatOf(9), "messages from the new connection act for the original seat");
            Assert.AreEqual(9UL, _seats.TransportOf(3), "messages for the seat go to the new connection");
            Assert.IsTrue(_seats.IsAlias(9));
        }

        [Test]
        public void Second_Rejoin_ReplacesTheFirstAlias()
        {
            var _seats = new SeatDirectory();
            _seats.Bind(9, 3);
            _seats.Bind(12, 3);

            Assert.AreEqual(12UL, _seats.TransportOf(3));
            Assert.AreEqual(9UL, _seats.SeatOf(9), "the old connection no longer plays the seat");
            Assert.AreEqual(3UL, _seats.SeatOf(12));
        }

        [Test]
        public void Unbind_ForgetsTheAlias_AndReportsTheSeat()
        {
            var _seats = new SeatDirectory();
            _seats.Bind(9, 3);

            Assert.IsTrue(_seats.Unbind(9, out ulong _seat));
            Assert.AreEqual(3UL, _seat);
            Assert.AreEqual(3UL, _seats.TransportOf(3));
            Assert.IsFalse(_seats.Unbind(9, out _));
        }

        [Test]
        public void Token_ResolvesToItsSeat_AndANewTokenReplacesTheOld()
        {
            var _seats = new SeatDirectory();
            _seats.IssueToken(3, "abc");
            Assert.IsTrue(_seats.TryGetSeatOfToken("abc", out ulong _seat));
            Assert.AreEqual(3UL, _seat);

            _seats.IssueToken(3, "def");
            Assert.IsFalse(_seats.TryGetSeatOfToken("abc", out _), "a replaced token no longer opens the seat");
            Assert.IsTrue(_seats.TryGetSeatOfToken("def", out _));
            Assert.IsFalse(_seats.TryGetSeatOfToken("", out _));
            Assert.IsFalse(_seats.TryGetSeatOfToken(null, out _));
        }

        [Test]
        public void ConnectionPayload_CarriesTheRejoinToken_AndStillReadsFormat1()
        {
            var _payload = new ConnectionPayload { BuildVersion = "1.0", PlayerName = "Poyo", RejoinToken = "tok" };
            Assert.IsTrue(ConnectionPayload.TryParse(_payload.ToBytes(), out ConnectionPayload _read));
            Assert.AreEqual("tok", _read.RejoinToken);
            Assert.AreEqual("Poyo", _read.PlayerName);

            byte[] _format1 = Format1Bytes("1.0", "Old");
            Assert.IsTrue(ConnectionPayload.TryParse(_format1, out ConnectionPayload _old));
            Assert.AreEqual("Old", _old.PlayerName);
            Assert.AreEqual(string.Empty, _old.RejoinToken);
        }

        // A format-1 payload as an older build wrote it (no rejoin token).
        private static byte[] Format1Bytes(string _build, string _name)
        {
            using var _stream = new System.IO.MemoryStream();
            using (var _writer = new System.IO.BinaryWriter(_stream, System.Text.Encoding.UTF8, true))
            {
                _writer.Write((byte)1);
                foreach (string _value in new[] { _build, _name, "" })
                {
                    byte[] _bytes = System.Text.Encoding.UTF8.GetBytes(_value);
                    _writer.Write((ushort)_bytes.Length);
                    _writer.Write(_bytes);
                }
                _writer.Write(0UL);
                _writer.Write(false);
            }
            return _stream.ToArray();
        }
    }
}
