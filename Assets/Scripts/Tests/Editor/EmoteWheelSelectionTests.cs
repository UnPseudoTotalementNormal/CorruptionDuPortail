using Avatars;
using NUnit.Framework;
using UnityEngine;

namespace Tests.Editor.Avatars
{
    /// <summary>
    /// GOLDEN pinning the pure emote-wheel selection math (<see cref="EmoteWheelSelection"/>): direction →
    /// highlighted sector, clockwise-from-up convention, wrap at the top, dead-zone, and the single-emote /
    /// empty degenerate cases. Pure vector function → asserted with ZERO scene/UI setup (mirrors
    /// <see cref="EmbodiedLookClampTests"/>).
    /// </summary>
    public class EmoteWheelSelectionTests
    {
        private const float Deadzone = 0.2f;

        // --- AngleClockwiseFromUp: cardinal directions map to 0/90/180/270, clockwise from up. ---

        [Test]
        public void Angle_Up_IsZero()
        {
            Assert.AreEqual(0f, EmoteWheelSelection.AngleClockwiseFromUp(new Vector2(0f, 1f)), 1e-3f);
        }

        [Test]
        public void Angle_Right_Is90()
        {
            Assert.AreEqual(90f, EmoteWheelSelection.AngleClockwiseFromUp(new Vector2(1f, 0f)), 1e-3f);
        }

        [Test]
        public void Angle_Down_Is180()
        {
            Assert.AreEqual(180f, EmoteWheelSelection.AngleClockwiseFromUp(new Vector2(0f, -1f)), 1e-3f);
        }

        [Test]
        public void Angle_Left_Is270()
        {
            Assert.AreEqual(270f, EmoteWheelSelection.AngleClockwiseFromUp(new Vector2(-1f, 0f)), 1e-3f);
        }

        [Test]
        public void Angle_ZeroVector_IsZero()
        {
            Assert.AreEqual(0f, EmoteWheelSelection.AngleClockwiseFromUp(Vector2.zero), 1e-3f);
        }

        // --- SelectionIndex with 4 emotes (top / right / bottom / left). ---

        [Test]
        public void Select4_Up_IsIndex0()
        {
            Assert.AreEqual(0, EmoteWheelSelection.SelectionIndex(new Vector2(0f, 1f), 4, Deadzone));
        }

        [Test]
        public void Select4_Right_IsIndex1()
        {
            Assert.AreEqual(1, EmoteWheelSelection.SelectionIndex(new Vector2(1f, 0f), 4, Deadzone));
        }

        [Test]
        public void Select4_Down_IsIndex2()
        {
            Assert.AreEqual(2, EmoteWheelSelection.SelectionIndex(new Vector2(0f, -1f), 4, Deadzone));
        }

        [Test]
        public void Select4_Left_IsIndex3()
        {
            Assert.AreEqual(3, EmoteWheelSelection.SelectionIndex(new Vector2(-1f, 0f), 4, Deadzone));
        }

        [Test]
        public void Select4_JustLeftOfUp_WrapsToIndex0()
        {
            // A hair counter-clockwise of straight up (~359°) must round back to the top sector, not to left.
            var _dir = new Vector2(-0.05f, 1f);
            Assert.AreEqual(0, EmoteWheelSelection.SelectionIndex(_dir, 4, Deadzone));
        }

        [Test]
        public void Select4_WithinWedge_SelectsThatEmote()
        {
            // 30° clockwise from up is still inside the top wedge (±45°) → index 0.
            var _dir = new Vector2(Mathf.Sin(30f * Mathf.Deg2Rad), Mathf.Cos(30f * Mathf.Deg2Rad));
            Assert.AreEqual(0, EmoteWheelSelection.SelectionIndex(_dir, 4, Deadzone));
        }

        // --- Dead-zone + degenerate counts. ---

        [Test]
        public void Select_InsideDeadzone_IsNone()
        {
            Assert.AreEqual(EmoteWheelSelection.None,
                EmoteWheelSelection.SelectionIndex(new Vector2(0f, 0.1f), 4, Deadzone));
        }

        [Test]
        public void Select_ExactlyZero_IsNone()
        {
            Assert.AreEqual(EmoteWheelSelection.None,
                EmoteWheelSelection.SelectionIndex(Vector2.zero, 4, Deadzone));
        }

        [Test]
        public void Select_ZeroCount_IsNone()
        {
            Assert.AreEqual(EmoteWheelSelection.None,
                EmoteWheelSelection.SelectionIndex(new Vector2(0f, 1f), 0, Deadzone));
        }

        [Test]
        public void Select_SingleEmote_AnyDirectionSelectsIt()
        {
            // With one emote the whole ring is its wedge — any direction past the dead-zone picks index 0.
            Assert.AreEqual(0, EmoteWheelSelection.SelectionIndex(new Vector2(0f, 1f), 1, Deadzone));
            Assert.AreEqual(0, EmoteWheelSelection.SelectionIndex(new Vector2(1f, 0f), 1, Deadzone));
            Assert.AreEqual(0, EmoteWheelSelection.SelectionIndex(new Vector2(-1f, -1f), 1, Deadzone));
        }

        [Test]
        public void Select_SingleEmote_DeadzoneStillNone()
        {
            Assert.AreEqual(EmoteWheelSelection.None,
                EmoteWheelSelection.SelectionIndex(new Vector2(0f, 0.05f), 1, Deadzone));
        }

        // --- SectorCenterAngle: UI layout anchors. ---

        [Test]
        public void SectorCenter_Four_EvenlySpaced()
        {
            Assert.AreEqual(0f, EmoteWheelSelection.SectorCenterAngle(0, 4), 1e-3f);
            Assert.AreEqual(90f, EmoteWheelSelection.SectorCenterAngle(1, 4), 1e-3f);
            Assert.AreEqual(180f, EmoteWheelSelection.SectorCenterAngle(2, 4), 1e-3f);
            Assert.AreEqual(270f, EmoteWheelSelection.SectorCenterAngle(3, 4), 1e-3f);
        }

        [Test]
        public void SectorCenter_ZeroCount_IsZero()
        {
            Assert.AreEqual(0f, EmoteWheelSelection.SectorCenterAngle(0, 0), 1e-3f);
        }
    }
}
