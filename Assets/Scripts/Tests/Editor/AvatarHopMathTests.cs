using Avatars;
using NUnit.Framework;
using UnityEngine;

namespace Tests.Editor.Avatars
{
    /// <summary>
    /// GOLDEN for the pure hop math (<see cref="AvatarHopMath"/>): the half-sine arc (ground-anchored, never
    /// below the floor), the squash/stretch factor, and the amplitude ease. Pure math → ZERO scene/network.
    /// </summary>
    public class AvatarHopMathTests
    {
        [Test]
        public void Arc_IsGroundedAtIntegerPhase_PeaksAtApex()
        {
            Assert.That(AvatarHopMath.Arc(0f), Is.EqualTo(0f).Within(1e-4f), "Ground at phase 0.");
            Assert.That(AvatarHopMath.Arc(1f), Is.EqualTo(0f).Within(1e-4f), "Ground at phase 1.");
            Assert.That(AvatarHopMath.Arc(0.5f), Is.EqualTo(1f).Within(1e-4f), "Apex at phase 0.5.");
        }

        [Test]
        public void Arc_IsNeverNegative_AcrossManyPhases()
        {
            for (int _i = 0; _i <= 200; _i++)
            {
                float _p = _i / 100f; // 0..2 (two hops)
                Assert.That(AvatarHopMath.Arc(_p), Is.GreaterThanOrEqualTo(0f), $"Arc negative at phase {_p}.");
            }
        }

        [Test]
        public void SquashStretch_ShortWideAtGround_TallThinAtApex()
        {
            Assert.That(AvatarHopMath.SquashStretch(0f), Is.EqualTo(-1f).Within(1e-4f), "Squash (-1) at the ground.");
            Assert.That(AvatarHopMath.SquashStretch(0.5f), Is.EqualTo(1f).Within(1e-4f), "Stretch (+1) at the apex.");
        }

        [Test]
        public void EaseAmplitude_ConvergesToTarget()
        {
            float _a = 0f;
            for (int _i = 0; _i < 600; _i++)
            {
                _a = AvatarHopMath.EaseAmplitude(_a, 1f, 8f, 1f / 60f);
            }
            Assert.That(_a, Is.EqualTo(1f).Within(0.01f), "Amplitude must ease to the moving target.");

            for (int _i = 0; _i < 600; _i++)
            {
                _a = AvatarHopMath.EaseAmplitude(_a, 0f, 8f, 1f / 60f);
            }
            Assert.That(_a, Is.EqualTo(0f).Within(0.01f), "Amplitude must ease back to rest when stopped.");
        }
    }
}
