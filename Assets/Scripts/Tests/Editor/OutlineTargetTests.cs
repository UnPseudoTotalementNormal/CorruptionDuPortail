using FX;
using NUnit.Framework;
using UnityEngine;

namespace Tests.Editor
{
    public class OutlineTargetTests
    {
        private static readonly int ActiveCountId = Shader.PropertyToID("_CdpOutlineCount");

        private GameObject _root;
        private MeshRenderer _body;
        private SkinnedMeshRenderer _fur;
        private ParticleSystemRenderer _sparks;

        [SetUp]
        public void SetUp()
        {
            _root = new GameObject("Outlined");
            _body = new GameObject("Body").AddComponent<MeshRenderer>();
            _body.transform.SetParent(_root.transform);
            _fur = new GameObject("Fur").AddComponent<SkinnedMeshRenderer>();
            _fur.transform.SetParent(_body.transform);
            _sparks = new GameObject("Sparks").AddComponent<ParticleSystemRenderer>();
            _sparks.transform.SetParent(_root.transform);
        }

        [TearDown]
        public void TearDown()
        {
            _root.SetOutline(OutlineState.None);
            Object.DestroyImmediate(_root);
        }

        [Test]
        public void RenderingLayers_ForEveryState_AreDeclared()
        {
            foreach (string _name in new[] { "Outline Available", "Outline Hover", "Outline Active", "Outline Selected" })
            {
                Assert.AreNotEqual(0u, RenderingLayerMask.GetMask(_name), _name);
            }
        }

        [Test]
        public void SetOutline_MarksMeshesOnly_AndKeepsTheirOtherBits()
        {
            uint _hover = RenderingLayerMask.GetMask("Outline Hover");
            uint _default = _body.renderingLayerMask;
            uint _particles = _sparks.renderingLayerMask;

            _root.SetOutline(OutlineState.Hover);

            Assert.AreEqual(_default | _hover, _body.renderingLayerMask);
            Assert.AreEqual(_fur.renderingLayerMask & _hover, _hover);
            Assert.AreEqual(_particles, _sparks.renderingLayerMask, "particles are not outlined");
            Assert.AreEqual(0, _root.layer, "the GameObject layer is never touched");
        }

        [Test]
        public void SetOutline_SwitchingState_ReplacesTheBit()
        {
            uint _hover = RenderingLayerMask.GetMask("Outline Hover");
            uint _active = RenderingLayerMask.GetMask("Outline Active");

            _root.SetOutline(OutlineState.Hover);
            _root.SetOutline(OutlineState.Active);

            Assert.AreEqual(0u, _body.renderingLayerMask & _hover);
            Assert.AreEqual(_active, _body.renderingLayerMask & _active);
        }

        [Test]
        public void SetOutline_None_ClearsBits_AndStopsThePass()
        {
            uint _before = _body.renderingLayerMask;
            int _count = Shader.GetGlobalInt(ActiveCountId);

            _root.SetOutline(OutlineState.Selected);
            Assert.AreEqual(_count + 1, Shader.GetGlobalInt(ActiveCountId));

            _root.SetOutline(OutlineState.None);
            Assert.AreEqual(_before, _body.renderingLayerMask);
            Assert.AreEqual(_count, Shader.GetGlobalInt(ActiveCountId));
        }

        [Test]
        public void SetOutline_None_OnAnUnmarkedObject_AddsNothing()
        {
            _root.SetOutline(OutlineState.None);

            Assert.IsNull(_root.GetComponent<OutlineTarget>());
        }
    }
}
