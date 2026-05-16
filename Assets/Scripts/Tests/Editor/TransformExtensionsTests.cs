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
    }
}
