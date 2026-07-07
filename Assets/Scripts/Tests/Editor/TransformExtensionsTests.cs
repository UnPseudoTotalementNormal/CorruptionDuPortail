using Extensions;
using NUnit.Framework;
using UnityEngine;

namespace Tests.Editor
{
    public class TransformExtensionsTests
    {
        [Test]
        public void ResetLocalValues_SetsTransformToIdentity()
        {
            GameObject go = new GameObject();
            Transform t = go.transform;

            t.localPosition = new Vector3(10, 20, 30);
            t.localRotation = Quaternion.Euler(45, 90, 180);
            t.localScale = new Vector3(2, 2, 2);

            t.ResetLocalValues();

            Assert.AreEqual(Vector3.zero, t.localPosition);
            Assert.AreEqual(Quaternion.identity, t.localRotation);
            Assert.AreEqual(Vector3.one, t.localScale);

            Object.DestroyImmediate(go);
        }

        [Test]
        public void GetWorldBounds_WithoutRenderers_ReturnsPointBounds()
        {
            GameObject go = new GameObject();
            go.transform.position = new Vector3(5, 10, 15);

            Bounds bounds = go.transform.GetWorldBounds();

            Assert.AreEqual(new Vector3(5, 10, 15), bounds.center);
            Assert.AreEqual(Vector3.zero, bounds.size);

            Object.DestroyImmediate(go);
        }

        [Test]
        public void GetWorldBounds_WithMultipleRenderers_ReturnsEncapsulatedBounds()
        {
            GameObject parent = new GameObject("Parent");
            parent.transform.position = Vector3.zero;

            GameObject child1 = GameObject.CreatePrimitive(PrimitiveType.Cube); // Size 1x1x1
            child1.transform.SetParent(parent.transform);
            child1.transform.localPosition = new Vector3(5, 0, 0);

            GameObject child2 = GameObject.CreatePrimitive(PrimitiveType.Cube); // Size 1x1x1
            child2.transform.SetParent(parent.transform);
            child2.transform.localPosition = new Vector3(-5, 0, 0);

            Bounds bounds = parent.transform.GetWorldBounds();

            // The bounds should encapsulate both cubes.
            // Cube 1 center: (5,0,0), size: (1,1,1) -> max x = 5.5, min x = 4.5
            // Cube 2 center: (-5,0,0), size: (1,1,1) -> max x = -4.5, min x = -5.5
            // Encapsulated min = (-5.5, -0.5, -0.5), max = (5.5, 0.5, 0.5)
            // Center = (0, 0, 0), Size = (11, 1, 1)

            Assert.AreEqual(Vector3.zero, bounds.center);
            Assert.AreEqual(new Vector3(11, 1, 1), bounds.size);

            Object.DestroyImmediate(parent);
        }

        // --- TryGetScreenBounds ---

        private static Camera MakeCamera(out RenderTexture rt)
        {
            GameObject camGo = new GameObject("TestCamera");
            Camera cam = camGo.AddComponent<Camera>();
            cam.transform.position = Vector3.zero;
            cam.transform.rotation = Quaternion.identity; // looks down +Z
            rt = new RenderTexture(800, 600, 0);
            cam.targetTexture = rt; // deterministic pixel dimensions, independent of the game view
            return cam;
        }

        private static void CleanupCamera(Camera cam, RenderTexture rt)
        {
            cam.targetTexture = null;
            rt.Release();
            Object.DestroyImmediate(rt);
            Object.DestroyImmediate(cam.gameObject);
        }

        [Test]
        public void TryGetScreenBounds_NullCamera_ReturnsFalse()
        {
            Bounds bounds = new Bounds(new Vector3(0, 0, 10), Vector3.one);

            bool ok = bounds.TryGetScreenBounds(null, out Vector2 size, out Vector2 center);

            Assert.IsFalse(ok);
            Assert.AreEqual(Vector2.zero, size);
            Assert.AreEqual(Vector2.zero, center);
        }

        [Test]
        public void TryGetScreenBounds_FrontFacingCenteredBox_ReturnsSymmetricScreenBox()
        {
            Camera cam = MakeCamera(out RenderTexture rt);
            Bounds bounds = new Bounds(new Vector3(0, 0, 10), new Vector3(2, 2, 2));

            bool ok = bounds.TryGetScreenBounds(cam, out Vector2 size, out Vector2 center);

            Assert.IsTrue(ok);
            Assert.Greater(size.x, 0f);
            Assert.Greater(size.y, 0f);
            // A box centered on the camera axis projects symmetrically around the screen center.
            Assert.AreEqual(cam.pixelWidth * 0.5f, center.x, 1f);
            Assert.AreEqual(cam.pixelHeight * 0.5f, center.y, 1f);

            CleanupCamera(cam, rt);
        }

        [Test]
        public void TryGetScreenBounds_BoxBehindCamera_ReturnsFalse()
        {
            Camera cam = MakeCamera(out RenderTexture rt);
            Bounds bounds = new Bounds(new Vector3(0, 0, -10), new Vector3(2, 2, 2));

            bool ok = bounds.TryGetScreenBounds(cam, out Vector2 size, out Vector2 center);

            Assert.IsFalse(ok);
            Assert.AreEqual(Vector2.zero, size);

            CleanupCamera(cam, rt);
        }

        [Test]
        public void TryGetScreenBounds_BoxStraddlingNearPlane_BuildsFiniteBoxFromFrontCorners()
        {
            Camera cam = MakeCamera(out RenderTexture rt);
            // Straddles the near plane: back corners at z = -1 (skipped), front corners at z = 3 (kept).
            Bounds bounds = new Bounds(new Vector3(0, 0, 1), new Vector3(2, 2, 4));

            bool ok = bounds.TryGetScreenBounds(cam, out Vector2 size, out Vector2 center);

            Assert.IsTrue(ok); // at least the front corners are valid
            Assert.Greater(size.x, 0f);
            Assert.Greater(size.y, 0f);
            Assert.IsFalse(float.IsNaN(size.x) || float.IsNaN(size.y));
            Assert.IsFalse(float.IsInfinity(size.x) || float.IsInfinity(size.y));
            Assert.IsFalse(float.IsNaN(center.x) || float.IsNaN(center.y));

            CleanupCamera(cam, rt);
        }

        [Test]
        public void TryGetScreenBounds_DeepOffCenterBox_EnclosesNaiveTwoCornerBox()
        {
            Camera cam = MakeCamera(out RenderTexture rt);
            // Deep in Z and off to the side — the regime where the old 2-corner math was wrong.
            Bounds bounds = new Bounds(new Vector3(3, 0, 12), new Vector3(2, 2, 8));

            bool ok = bounds.TryGetScreenBounds(cam, out Vector2 size, out Vector2 center);

            Assert.IsTrue(ok);

            // The old approach projected only min/max. Those two points are among the 8 corners, so the
            // full 8-corner box must enclose them — i.e. be at least as large on each axis.
            Vector3 screenMin = cam.WorldToScreenPoint(bounds.min);
            Vector3 screenMax = cam.WorldToScreenPoint(bounds.max);
            Vector2 naive = new Vector2(Mathf.Abs(screenMax.x - screenMin.x), Mathf.Abs(screenMax.y - screenMin.y));

            Assert.GreaterOrEqual(size.x, naive.x - 0.001f);
            Assert.GreaterOrEqual(size.y, naive.y - 0.001f);

            CleanupCamera(cam, rt);
        }
    }
}
