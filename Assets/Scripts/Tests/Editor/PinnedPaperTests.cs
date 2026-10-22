using NUnit.Framework;
using UI.LobbyRoles;
using UnityEngine;

namespace Tests.Editor
{
    /// <summary>
    /// EditMode pins of <see cref="PinnedPaper"/> (mockup attribution-roles v9 A1): a paper's look comes from its key
    /// alone, stays in the chosen ranges, and the papers of one board do not all hang the same way.
    /// </summary>
    [Category("LobbyRoles")]
    public class PinnedPaperTests
    {
        [Test]
        public void SameKey_SameLook()
        {
            for (int key = 0; key < 200; key++)
            {
                Assert.AreEqual(PinnedPaper.TiltDeg(key), PinnedPaper.TiltDeg(key));
                Assert.AreEqual(PinnedPaper.Shift(key), PinnedPaper.Shift(key));
                Assert.AreEqual(PinnedPaper.PinX(key), PinnedPaper.PinX(key));
            }
        }

        [Test]
        public void Look_StaysInItsRanges()
        {
            for (int key = -50; key < 500; key++)
            {
                float tilt = PinnedPaper.TiltDeg(key);
                Vector2 shift = PinnedPaper.Shift(key);
                float pin = PinnedPaper.PinX(key);
                Assert.That(tilt, Is.InRange(-PinnedPaper.MaxTiltDeg, PinnedPaper.MaxTiltDeg), $"tilt of {key}");
                Assert.That(shift.x, Is.InRange(-PinnedPaper.MaxShiftPx, PinnedPaper.MaxShiftPx), $"shift x of {key}");
                Assert.That(shift.y, Is.InRange(-PinnedPaper.MaxShiftPx, PinnedPaper.MaxShiftPx), $"shift y of {key}");
                Assert.That(pin, Is.InRange(PinnedPaper.PinMinPercent, PinnedPaper.PinMaxPercent), $"pin of {key}");
            }
        }

        // Neighbouring keys (the six counters, a faction's cards) must not line up: the point is that no two hang alike.
        [Test]
        public void NeighbouringKeys_HangDifferently()
        {
            float minTilt = float.MaxValue, maxTilt = float.MinValue, minPin = float.MaxValue, maxPin = float.MinValue;
            for (int key = 0; key < 6; key++)
            {
                minTilt = Mathf.Min(minTilt, PinnedPaper.TiltDeg(key));
                maxTilt = Mathf.Max(maxTilt, PinnedPaper.TiltDeg(key));
                minPin = Mathf.Min(minPin, PinnedPaper.PinX(key));
                maxPin = Mathf.Max(maxPin, PinnedPaper.PinX(key));
            }
            Assert.Greater(maxTilt - minTilt, 1.5f, "the six counters should not share one tilt");
            Assert.Greater(maxPin - minPin, 10f, "the six counters should not share one pin place");
        }
    }
}
