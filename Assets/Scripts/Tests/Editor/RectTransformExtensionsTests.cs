using Extensions;
using NUnit.Framework;
using UnityEngine;

namespace Tests.Editor
{
    public class RectTransformExtensionsTests
    {
        private GameObject _go;
        private RectTransform _rectTransform;

        [SetUp]
        public void Setup()
        {
            _go = new GameObject();
            _rectTransform = _go.AddComponent<RectTransform>();
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(_go);
        }

        [Test]
        public void SetToFullStretch_SetsAnchorsAndOffsetsToStretch()
        {
            // Initial arbitrary values
            _rectTransform.anchorMin = new Vector2(0.5f, 0.5f);
            _rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
            _rectTransform.offsetMin = new Vector2(10, 10);
            _rectTransform.offsetMax = new Vector2(-10, -10);

            _rectTransform.SetToFullStretch();

            Assert.AreEqual(Vector2.zero, _rectTransform.anchorMin);
            Assert.AreEqual(Vector2.one, _rectTransform.anchorMax);
            Assert.AreEqual(Vector2.zero, _rectTransform.offsetMin);
            Assert.AreEqual(Vector2.zero, _rectTransform.offsetMax);
        }
    }
}
