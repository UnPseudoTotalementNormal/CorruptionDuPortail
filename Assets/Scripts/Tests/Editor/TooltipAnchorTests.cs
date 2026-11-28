using Board;
using NUnit.Framework;
using TooltipSystem;
using UnityEditor;
using UnityEngine;

namespace Tests.Editor
{
    /// <summary>
    /// What a bubble sits beside. The card marks bubble was anchored on the card root, a 0x0 rect: the bubble opened on
    /// the middle of the card instead of above it (2026-11-28). The card now names its face, and the manager drops an
    /// empty anchor for the trigger's own bounds.
    /// </summary>
    [Category("Tooltip")]
    public class TooltipAnchorTests
    {
        [TestCase(0f, 0f, true)]
        [TestCase(630f, 0f, true)]
        [TestCase(0.5f, 880f, true)]
        [TestCase(630f, 880f, false)]
        public void EmptyAnchor_IsAnyRectWithNoArea(float _width, float _height, bool _empty)
        {
            Assert.AreEqual(_empty, TooltipManager.IsEmptyAnchor(new Vector2(_width, _height)));
        }

        [Test]
        public void CardPrefab_FaceRect_IsTheWholeCardFace()
        {
            GameObject _prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Card.prefab");
            Assert.IsNotNull(_prefab, "Card.prefab moved");
            RectTransform _face = _prefab.GetComponent<Card>().FaceRect;

            Assert.IsNotNull(_face, "Card.faceRect is not wired");
            Assert.IsFalse(TooltipManager.IsEmptyAnchor(_face.rect.size), $"{_face.name} has no area: {_face.rect.size}");
            Assert.AreEqual(new Vector2(630f, 880f), _face.rect.size, "the face is the card's FrontCanvas");
        }
    }
}
