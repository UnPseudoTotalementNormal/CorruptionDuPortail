using Avatars;
using NUnit.Framework;
using UnityEngine;

namespace Tests.Editor.Avatars
{
    /// <summary>
    /// GOLDEN for the pure idle-motion math (<see cref="AvatarIdleMath"/>): the per-instance phase seed
    /// (deterministic + spread, for anti-lockstep) and the damped spring (converges, overshoots-then-settles)
    /// that drives the bow follow-through. Pure math → ZERO scene/network setup.
    /// </summary>
    public class AvatarIdleMathTests
    {
        [Test]
        public void PhaseSeed_IsDeterministic_AndInUnitRange()
        {
            Assert.That(AvatarIdleMath.PhaseSeed(12345), Is.EqualTo(AvatarIdleMath.PhaseSeed(12345)),
                "Same id must give the same phase.");
            float _p = AvatarIdleMath.PhaseSeed(-9999);
            Assert.That(_p, Is.InRange(0f, 1f), "Phase must be in [0,1).");
        }

        [Test]
        public void PhaseSeed_DiffersAcrossIds()
        {
            // Anti-lockstep: distinct avatars (distinct instance ids) must not share a phase.
            float _a = AvatarIdleMath.PhaseSeed(1001);
            float _b = AvatarIdleMath.PhaseSeed(1002);
            float _c = AvatarIdleMath.PhaseSeed(50050);
            Assert.That(_a, Is.Not.EqualTo(_b));
            Assert.That(_b, Is.Not.EqualTo(_c));
            Assert.That(_a, Is.Not.EqualTo(_c));
        }

        [Test]
        public void DampedSpring_ConvergesToTarget()
        {
            float _current = 8f;
            float _velocity = 0f;
            for (int _i = 0; _i < 1200; _i++) // 20 s at 60 fps
            {
                _current = AvatarIdleMath.DampedSpring(_current, ref _velocity, 0f, 120f, 14f, 1f / 60f);
            }
            Assert.That(_current, Is.EqualTo(0f).Within(0.05f), "Spring must settle at the target.");
            Assert.That(_velocity, Is.EqualTo(0f).Within(0.05f), "Velocity must die out.");
        }

        [Test]
        public void DampedSpring_PerturbedVelocity_OvershootsThenSettles()
        {
            // Model the bow being "thrown" by a turn: a velocity kick at rest must swing out then return to ~0.
            float _current = 0f;
            float _velocity = 25f;
            float _maxExcursion = 0f;
            for (int _i = 0; _i < 1200; _i++)
            {
                _current = AvatarIdleMath.DampedSpring(_current, ref _velocity, 0f, 120f, 14f, 1f / 60f);
                _maxExcursion = Mathf.Max(_maxExcursion, Mathf.Abs(_current));
            }
            Assert.That(_maxExcursion, Is.GreaterThan(0.1f), "A velocity kick must visibly throw the spring out.");
            Assert.That(_current, Is.EqualTo(0f).Within(0.05f), "It must trail back to neutral.");
        }
    }
}
