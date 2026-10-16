using NUnit.Framework;
using Presentation;
using UnityEngine;

namespace Tests.Editor
{
    /// <summary>Where a cat's own spotlight hangs (board task T11, <see cref="CatSpotlightPose"/>).</summary>
    public class CatSpotlightPoseTests
    {
        private const float Tolerance = 1e-4f;

        private static void AssertNear(Vector3 _expected, Vector3 _actual)
        {
            Assert.That(Vector3.Distance(_expected, _actual), Is.LessThan(Tolerance), $"expected {_expected}, got {_actual}");
        }

        [Test]
        public void StraightAbove_HangsOverTheHeadPointingDown()
        {
            Pose _pose = CatSpotlightPose.Compute(new Vector3(3f, -10f, 5f), Vector3.back, 90f, 30f);
            AssertNear(new Vector3(3f, 20f, 5f), _pose.position);
            AssertNear(Vector3.down, _pose.forward);
        }

        [Test]
        public void Tilted_HangsOnTheTableSideAndAimsAtTheHead()
        {
            Vector3 _head = new Vector3(0f, -10f, 20f);
            Pose _pose = CatSpotlightPose.Compute(_head, Vector3.back, 60f, 30f);
            // On the side the cat faces (toward the table, here -z), 60° up.
            AssertNear(_head + new Vector3(0f, 30f * Mathf.Sin(60f * Mathf.Deg2Rad), -30f * Mathf.Cos(60f * Mathf.Deg2Rad)), _pose.position);
            AssertNear((_head - _pose.position).normalized, _pose.forward);
        }

        [Test]
        public void TiltedFacing_IgnoresItsVerticalPart()
        {
            Pose _flat = CatSpotlightPose.Compute(Vector3.zero, Vector3.right, 60f, 10f);
            Pose _tilted = CatSpotlightPose.Compute(Vector3.zero, new Vector3(1f, -0.5f, 0f), 60f, 10f);
            AssertNear(_flat.position, _tilted.position);
        }

        [Test]
        public void DegenerateFacing_FallsBackToWorldForward()
        {
            Pose _pose = CatSpotlightPose.Compute(Vector3.zero, Vector3.up, 0f, 10f);
            AssertNear(new Vector3(0f, 0f, 10f), _pose.position);
        }

        [Test]
        public void ElevationOutOfRange_IsClamped()
        {
            Pose _over = CatSpotlightPose.Compute(Vector3.zero, Vector3.forward, 120f, 10f);
            AssertNear(new Vector3(0f, 10f, 0f), _over.position);
            Pose _under = CatSpotlightPose.Compute(Vector3.zero, Vector3.forward, -20f, 10f);
            AssertNear(new Vector3(0f, 0f, 10f), _under.position);
        }
    }
}
