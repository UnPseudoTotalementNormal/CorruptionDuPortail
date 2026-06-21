using Avatars;
using NUnit.Framework;
using UnityEngine;

namespace Tests.Editor.Avatars
{
    /// <summary>
    /// Story 13.4 — the GOLDEN pinning the pure embodied-look clamp math
    /// (<see cref="EmbodiedLookClamp"/>, DO4 yaw ±75° / pitch ±40°). Pure float function → asserted
    /// with ZERO scene/network setup. One assert per boundary so a regression names the offending axis.
    /// </summary>
    public class EmbodiedLookClampTests
    {
        // DO4 defaults; speed 1 so a raw delta maps 1:1 to degrees (the clamp is what is under test).
        private const float YawClamp = 75f;
        private const float PitchClamp = 40f;
        private const float Speed = 1f;

        private static EmbodiedLookAngles Apply(float _yaw, float _pitch, Vector2 _delta) =>
            EmbodiedLookClamp.Apply(_yaw, _pitch, _delta, Speed, Speed, YawClamp, PitchClamp);

        [Test]
        public void Yaw_AccumulatesWithinRange()
        {
            // From centre, a +30 delta stays well inside ±75 → accumulates 1:1.
            Assert.AreEqual(30f, Apply(0f, 0f, new Vector2(30f, 0f)).Yaw, 1e-4f);
        }

        [Test]
        public void Yaw_ClampsToPositiveBound()
        {
            // A huge positive delta clamps to +yawClamp, never beyond.
            Assert.AreEqual(YawClamp, Apply(0f, 0f, new Vector2(1000f, 0f)).Yaw, 1e-4f);
        }

        [Test]
        public void Yaw_ClampsToNegativeBound()
        {
            Assert.AreEqual(-YawClamp, Apply(0f, 0f, new Vector2(-1000f, 0f)).Yaw, 1e-4f);
        }

        [Test]
        public void Pitch_MouseUpLooksUp_NegativeEuler()
        {
            // Mouse up (positive delta.y) SUBTRACTS from pitch (looks up = negative local-X euler).
            Assert.AreEqual(-20f, Apply(0f, 0f, new Vector2(0f, 20f)).Pitch, 1e-4f);
        }

        [Test]
        public void Pitch_ClampsToPositiveBound()
        {
            // Mouse down (negative delta.y) ADDS to pitch → clamps to +pitchClamp.
            Assert.AreEqual(PitchClamp, Apply(0f, 0f, new Vector2(0f, -1000f)).Pitch, 1e-4f);
        }

        [Test]
        public void Pitch_ClampsToNegativeBound()
        {
            Assert.AreEqual(-PitchClamp, Apply(0f, 0f, new Vector2(0f, 1000f)).Pitch, 1e-4f);
        }

        [Test]
        public void AccumulatesOntoCurrentAngles()
        {
            // Starts from a non-zero current yaw/pitch and adds the (speed-scaled) delta.
            EmbodiedLookAngles _result = Apply(10f, -5f, new Vector2(15f, 5f));
            Assert.AreEqual(25f, _result.Yaw, 1e-4f);   // 10 + 15
            Assert.AreEqual(-10f, _result.Pitch, 1e-4f); // -5 - 5
        }
    }
}
