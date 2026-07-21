using Avatars;
using NUnit.Framework;
using UnityEngine;

namespace Tests.Editor.Avatars
{
    /// <summary>
    /// GOLDEN pinning the pure emote self-feedback helpers: <see cref="EmoteStop.ShouldStop"/> (a running emote
    /// stops on any key/click but NOT on mouse movement — movement is orbit) and <see cref="EmoteOrbit.ComputePose"/>
    /// (the third-person orbit camera stays at the right distance and looks back at the avatar). Pure functions →
    /// asserted with ZERO scene/Cinemachine setup (mirrors <see cref="EmoteWheelSelectionTests"/>).
    /// </summary>
    public class EmoteStopConditionTests
    {
        // --- ShouldStop: key OR mouse-button ends the emote; neither = keep playing (mouse-move never a param). ---

        [Test]
        public void ShouldStop_NoInput_False() => Assert.IsFalse(EmoteStop.ShouldStop(false, false));

        [Test]
        public void ShouldStop_AnyKey_True() => Assert.IsTrue(EmoteStop.ShouldStop(true, false));

        [Test]
        public void ShouldStop_AnyMouseButton_True() => Assert.IsTrue(EmoteStop.ShouldStop(false, true));

        [Test]
        public void ShouldStop_Both_True() => Assert.IsTrue(EmoteStop.ShouldStop(true, true));

        // --- ComputePose: distance from pivot is preserved; camera looks back at the pivot. ---

        [Test]
        public void ComputePose_YawZeroPitchZero_SitsBehindOnMinusZ()
        {
            EmoteOrbit.ComputePose(Vector3.zero, 0f, 0f, 3f, out Vector3 _pos, out Quaternion _rot);

            Assert.That(_pos.x, Is.EqualTo(0f).Within(1e-4f));
            Assert.That(_pos.y, Is.EqualTo(0f).Within(1e-4f));
            Assert.That(_pos.z, Is.EqualTo(-3f).Within(1e-4f));
            // Looks toward the pivot = +Z.
            Vector3 _fwd = _rot * Vector3.forward;
            Assert.That(_fwd.z, Is.EqualTo(1f).Within(1e-3f));
        }

        [Test]
        public void ComputePose_PreservesDistanceForAnyAngle()
        {
            Vector3 _pivot = new Vector3(2f, 1f, -4f);
            EmoteOrbit.ComputePose(_pivot, 37f, 22f, 5f, out Vector3 _pos, out _);
            Assert.That(Vector3.Distance(_pos, _pivot), Is.EqualTo(5f).Within(1e-3f));
        }

        [Test]
        public void ComputePose_LooksAtPivot()
        {
            Vector3 _pivot = new Vector3(-1f, 2f, 3f);
            EmoteOrbit.ComputePose(_pivot, 120f, -5f, 4f, out Vector3 _pos, out Quaternion _rot);

            Vector3 _expected = (_pivot - _pos).normalized;
            Vector3 _forward = _rot * Vector3.forward;
            Assert.That(Vector3.Dot(_forward, _expected), Is.EqualTo(1f).Within(1e-3f));
        }

        [Test]
        public void ComputePose_NegativeDistance_ClampedToPivot()
        {
            Vector3 _pivot = new Vector3(1f, 1f, 1f);
            EmoteOrbit.ComputePose(_pivot, 45f, 10f, -2f, out Vector3 _pos, out _);
            Assert.That(Vector3.Distance(_pos, _pivot), Is.EqualTo(0f).Within(1e-4f));
        }
    }
}
