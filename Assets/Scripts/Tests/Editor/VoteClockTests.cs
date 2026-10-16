using CorruptionDuPortail.Domain;
using NUnit.Framework;

namespace Tests.Editor
{
    /// <summary>
    /// Board task T19: the vote candle follows the vote timer on the host (read each frame) and on a client (an update
    /// a second). It melts with the time really gone, races when everyone has voted without burning down at once, and
    /// ticks only in the last minute, faster and faster.
    /// </summary>
    public class VoteClockTests
    {
        private const float Dt = 1f / 60f;

        // The host: the timer goes down every frame; everyone votes when collapseAt seconds are left (0 = never).
        private static (VoteClock clock, int ticks, float meltAtCollapse) RunHost(float duration, float collapseAt)
        {
            var _clock = new VoteClock();
            float _raw = duration;
            _clock.Start(duration, _raw);
            int _ticks = 0;
            float _melt = -1f;
            bool _collapsed = false;
            while (_raw > 0f)
            {
                _raw -= Dt;
                if (!_collapsed && collapseAt > 0f && _raw <= duration - collapseAt)
                {
                    _raw = System.Math.Min(_raw, 5f);
                    _collapsed = true;
                    _melt = _clock.Melt;
                }
                if (_clock.Advance(_raw, Dt))
                {
                    _ticks++;
                }
            }
            return (_clock, _ticks, _melt);
        }

        [Test]
        public void RunsOut_MeltsAllTheWay_TicksOnlyInTheLastMinute()
        {
            var (_clock, _ticks, _) = RunHost(300f, 0f);
            Assert.That(_clock.Melt, Is.EqualTo(1f).Within(0.01f));
            Assert.That(_clock.Tension, Is.EqualTo(1f));
            // 50 ticks at one a second, then the final 10 s, faster: more than 10 there, never a hundred.
            Assert.That(_ticks, Is.InRange(62, 75));
        }

        [Test]
        public void EveryoneVoted_RacesWithoutBurningDown()
        {
            var (_clock, _ticks, _melt) = RunHost(300f, 20f);
            Assert.That(_clock.FinalSpan, Is.EqualTo(5f).Within(0.05f));
            Assert.That(_clock.Melt, Is.LessThan(0.1f), "25 s of a 300 s vote: the candle is still tall");
            Assert.That(_clock.Melt, Is.GreaterThan(_melt), "it keeps melting at its pace");
            Assert.That(_ticks, Is.InRange(6, 12), "the clock raced over the last 5 s");
        }

        [Test]
        public void Client_StaleTimerFromTheLastVote_DoesNotBurnTheNewCandle()
        {
            var _clock = new VoteClock();
            _clock.Start(300f, 0.4f); // yesterday's last update
            _clock.Advance(0.4f, Dt);
            Assert.That(_clock.TimeLeft, Is.GreaterThan(299f));
            Assert.That(_clock.Tension, Is.EqualTo(0f));
            _clock.Advance(299f, Dt); // the server's first update
            Assert.That(_clock.TimeLeft, Is.EqualTo(299f));
            Assert.That(_clock.Melt, Is.LessThan(0.01f));
        }

        [Test]
        public void Client_CountsDownBetweenUpdates()
        {
            var _clock = new VoteClock();
            _clock.Start(300f, 300f);
            _clock.Advance(120f, Dt); // a rejoin in the middle of the vote: 180 s are gone
            Assert.That(_clock.Melt, Is.EqualTo(0.6f).Within(0.01f));
            for (int _i = 0; _i < 30; _i++)
            {
                _clock.Advance(120f, Dt);
            }
            Assert.That(_clock.TimeLeft, Is.EqualTo(119.5f).Within(0.02f), "half a second counted down locally");
        }
    }
}
