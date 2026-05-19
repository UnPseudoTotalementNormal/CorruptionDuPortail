using DG.Tweening;
using Extensions;
using NUnit.Framework;
using UnityEngine;

namespace Tests.Editor
{
    public class CanvasGroupExtensionsTests
    {
        private GameObject _go;
        private CanvasGroup _canvasGroup;

        [SetUp]
        public void Setup()
        {
            _go = new GameObject();
            _canvasGroup = _go.AddComponent<CanvasGroup>();
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(_go);
        }

        [Test]
        public void DoShowGroup_SetsPropertiesCorrectly()
        {
            _canvasGroup.DoShowGroup(0.5f, true, true, 1f);

            Assert.IsTrue(_canvasGroup.interactable);
            Assert.IsTrue(_canvasGroup.blocksRaycasts);
        }

        [Test]
        public void DoHideGroup_SetsPropertiesCorrectly()
        {
            _canvasGroup.DoHideGroup(0.5f, false, false);

            Assert.IsFalse(_canvasGroup.interactable);
            Assert.IsFalse(_canvasGroup.blocksRaycasts);
        }
    }
}
