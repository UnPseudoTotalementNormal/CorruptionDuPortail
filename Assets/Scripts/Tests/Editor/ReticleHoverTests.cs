using NUnit.Framework;
using Reticle;

namespace Tests.Editor.Reticle
{
    /// <summary>
    /// GOLDEN for the pure reticle hover hysteresis (<see cref="ReticleHover"/>): the timers that stop the
    /// center-screen reticle from flickering as it skims targets. Pure → no scene/network setup.
    /// </summary>
    public class ReticleHoverTests
    {
        private const int A = 101;
        private const int B = 202;
        private const float ExitDwell = 0.12f;
        private const float SwitchDebounce = 0.06f;

        private static ReticleHover Fresh() => new ReticleHover(ExitDwell, SwitchDebounce);

        // Drive the hover onto target `id` until it commits; returns the hover at Current == id.
        private static ReticleHover Hovering(int _id)
        {
            ReticleHover _h = Fresh();
            for (int _i = 0; _i < 10 && _h.Current != _id; _i++)
            {
                _h.Tick(_id, 0.05f);
            }
            Assert.AreEqual(_id, _h.Current, "setup: never settled on the target");
            return _h;
        }

        [Test]
        public void Enter_OnlyAfterSwitchDebounce()
        {
            ReticleHover _h = Fresh();
            Assert.AreEqual(ReticleHover.None, _h.Tick(A, 0.05f).Entered, "fires too early (frame 1)");
            Assert.AreEqual(ReticleHover.None, _h.Tick(A, 0.005f).Entered, "fires before debounce elapsed");
            Assert.AreEqual(A, _h.Tick(A, 0.05f).Entered, "did not enter after debounce");
            Assert.AreEqual(A, _h.Current);
        }

        [Test]
        public void BriefMiss_DoesNotUnhover()
        {
            ReticleHover _h = Hovering(A);
            // One short frame off the target (under the exit dwell) then back on → must NOT exit.
            Assert.AreEqual(ReticleHover.None, _h.Tick(ReticleHover.None, 0.05f).Exited, "exited on a brief graze");
            ReticleHoverResult _back = _h.Tick(A, 0.05f);
            Assert.AreEqual(ReticleHover.None, _back.Exited, "exited after returning to the target");
            Assert.AreEqual(A, _h.Current);
        }

        [Test]
        public void Exit_ToEmptyAfterDwell()
        {
            ReticleHover _h = Hovering(A);
            _h.Tick(ReticleHover.None, 0.05f);
            _h.Tick(ReticleHover.None, 0.05f);
            ReticleHoverResult _r = _h.Tick(ReticleHover.None, 0.05f); // total 0.15 >= 0.12
            Assert.AreEqual(A, _r.Exited, "did not exit after the dwell");
            Assert.AreEqual(ReticleHover.None, _r.Entered);
            Assert.AreEqual(ReticleHover.None, _h.Current);
        }

        [Test]
        public void Switch_EmitsExitThenEnterSameTick()
        {
            ReticleHover _h = Hovering(A);
            _h.Tick(B, 0.04f);
            ReticleHoverResult _r = _h.Tick(B, 0.04f); // 0.08 >= 0.06 switch debounce
            Assert.AreEqual(A, _r.Exited, "did not exit the old target on switch");
            Assert.AreEqual(B, _r.Entered, "did not enter the new target on switch");
            Assert.AreEqual(B, _h.Current);
        }

        [Test]
        public void Reset_ReturnsCurrentToExit()
        {
            ReticleHover _h = Hovering(A);
            Assert.AreEqual(A, _h.Reset(), "Reset must report the target to exit");
            Assert.AreEqual(ReticleHover.None, _h.Current);
        }
    }
}
