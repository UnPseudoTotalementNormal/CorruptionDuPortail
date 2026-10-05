using CorruptionDuPortail.Domain;
using NUnit.Framework;

namespace Tests.Editor
{
    /// <summary>Rejoin step 1: the pure seat-reservation bookkeeping behind a mid-game disconnect.</summary>
    [Category("Networking")]
    public class SeatReservationsTests
    {
        [Test]
        public void Reserved_Seat_IsNotExpired_BeforeItsDeadline()
        {
            var _seats = new SeatReservations();
            Assert.IsTrue(_seats.Reserve(1, 100.0, 120.0));

            Assert.IsTrue(_seats.IsReserved(1));
            CollectionAssert.IsEmpty(_seats.TakeExpired(219.9));
            Assert.IsTrue(_seats.IsReserved(1));
        }

        [Test]
        public void Expired_Seat_IsTakenExactlyOnce()
        {
            var _seats = new SeatReservations();
            _seats.Reserve(1, 100.0, 120.0);

            CollectionAssert.AreEqual(new[] { 1UL }, _seats.TakeExpired(220.0));
            CollectionAssert.IsEmpty(_seats.TakeExpired(500.0), "an expired seat is settled once, never twice");
            Assert.IsFalse(_seats.IsReserved(1));
        }

        [Test]
        public void Second_Reservation_KeepsTheFirstDeadline()
        {
            // Two ignition sources (liveness then transport) reach the leave pipeline: the later one must not extend it.
            var _seats = new SeatReservations();
            _seats.Reserve(1, 100.0, 120.0);

            Assert.IsFalse(_seats.Reserve(1, 150.0, 120.0));
            CollectionAssert.AreEqual(new[] { 1UL }, _seats.TakeExpired(220.0));
        }

        [Test]
        public void Released_Seat_NeverExpires()
        {
            var _seats = new SeatReservations();
            _seats.Reserve(1, 100.0, 120.0);

            Assert.IsTrue(_seats.Release(1), "the player took his seat back");
            CollectionAssert.IsEmpty(_seats.TakeExpired(1000.0));
            Assert.IsFalse(_seats.Release(1));
        }

        [Test]
        public void Several_Seats_ExpireIndependently_InIdOrder()
        {
            var _seats = new SeatReservations();
            _seats.Reserve(3, 0.0, 10.0);
            _seats.Reserve(1, 0.0, 10.0);
            _seats.Reserve(2, 5.0, 10.0);

            CollectionAssert.AreEqual(new[] { 1UL, 3UL }, _seats.TakeExpired(10.0));
            CollectionAssert.AreEqual(new[] { 2UL }, _seats.TakeExpired(15.0));
        }
    }
}
