using NUnit.Framework;
using TooltipSystem;
using UnityEngine;

namespace Tests.Editor
{
    /// <summary>
    /// <see cref="TooltipManager.KeepOnScreen"/>: a tooltip beside a trigger near a screen edge flips to the other
    /// side and stays inside the screen (playtest 2026-10-29: the robot counter's tooltip, above a counter in the
    /// top-right corner, came out of the top of the screen).
    /// </summary>
    [Category("Tooltip")]
    public class TooltipKeepOnScreenTests
    {
        private static readonly Vector2 Screen = new Vector2(1920f, 1080f);

        [Test]
        public void TooltipThatFits_IsPlacedAsAsked()
        {
            (Vector2 _centre, bool _flipped) = TooltipManager.KeepOnScreen(new Vector2(960f, 500f), new Vector2(100f, 100f),
                new Vector2(300f, 80f), Vector2.up, Vector2.zero, Screen);

            Assert.IsFalse(_flipped);
            Assert.AreEqual(new Vector2(960f, 590f), _centre);
        }

        [Test]
        public void TooltipAboveATriggerAtTheTop_FlipsBelow()
        {
            (Vector2 _centre, bool _flipped) = TooltipManager.KeepOnScreen(new Vector2(1700f, 1000f), new Vector2(100f, 100f),
                new Vector2(300f, 120f), Vector2.up, Vector2.zero, Screen);

            Assert.IsTrue(_flipped);
            Assert.AreEqual(1000f - 50f - 60f, _centre.y, 0.001f);
        }

        [Test]
        public void TooltipWiderThanTheRoomLeft_IsClampedInside()
        {
            (Vector2 _centre, bool _) = TooltipManager.KeepOnScreen(new Vector2(1880f, 500f), new Vector2(60f, 60f),
                new Vector2(600f, 80f), Vector2.up, Vector2.zero, Screen);

            Assert.AreEqual(1920f - 300f, _centre.x, 0.001f);
            Assert.LessOrEqual(_centre.y + 40f, Screen.y);
        }
    }
}
