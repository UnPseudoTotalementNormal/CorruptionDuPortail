using NUnit.Framework;
using UI;
using UI.CatPaw;
using UnityEngine;
using UnityEngine.UI;

namespace Tests.Editor
{
    /// <summary>The cat paw cursor's rules (maquettes curseur-patte v1: C3 free mouse, R2 seated reticle).</summary>
    public class CatPawCursorTests
    {
        [TestCase(CursorHint.None, false, PawState.Idle)]
        [TestCase(CursorHint.None, true, PawState.Idle)]
        [TestCase(CursorHint.Clickable, false, PawState.Claws)]
        [TestCase(CursorHint.Clickable, true, PawState.Press)]
        [TestCase(CursorHint.Refused, false, PawState.Refused)]
        [TestCase(CursorHint.Refused, true, PawState.Refused)]
        public void Paw_FollowsWhatIsUnderThePointer(CursorHint _hint, bool _pressing, PawState _expected)
        {
            Assert.AreEqual(_expected, CursorHints.Paw(_hint, _pressing));
        }

        [Test]
        public void Of_Nothing_IsNone()
        {
            Assert.AreEqual(CursorHint.None, CursorHints.Of(null));
            var _go = new GameObject("plain");
            try
            {
                Assert.AreEqual(CursorHint.None, CursorHints.Of(_go));
            }
            finally
            {
                Object.DestroyImmediate(_go);
            }
        }

        [Test]
        public void Of_CustomButton_ClawsThenCrossWhenGreyed()
        {
            var _go = new GameObject("button", typeof(RectTransform));
            var _child = new GameObject("label", typeof(RectTransform));
            _child.transform.SetParent(_go.transform);
            try
            {
                CustomButton _button = _go.AddComponent<CustomButton>();
                Assert.AreEqual(CursorHint.Clickable, CursorHints.Of(_child), "a hit on the button's label reads the button");
                _button.SetInteractable(false);
                Assert.AreEqual(CursorHint.Refused, CursorHints.Of(_child));
            }
            finally
            {
                Object.DestroyImmediate(_go);
            }
        }

        [Test]
        public void Of_UguiSelectable_FollowsInteractable()
        {
            var _go = new GameObject("toggle", typeof(RectTransform));
            try
            {
                Toggle _toggle = _go.AddComponent<Toggle>();
                Assert.AreEqual(CursorHint.Clickable, CursorHints.Of(_go));
                _toggle.interactable = false;
                Assert.AreEqual(CursorHint.Refused, CursorHints.Of(_go));
            }
            finally
            {
                Object.DestroyImmediate(_go);
            }
        }

        [Test]
        public void Art_EveryPawIsInResources()
        {
            foreach (PawState _state in System.Enum.GetValues(typeof(PawState)))
            {
                string _name = PawArt.Name(_state);
                Assert.IsNotNull(Resources.Load<Texture2D>("CatPaw/cursor48_" + _name), "cursor48_" + _name);
                Assert.IsNotNull(Resources.Load<Texture2D>("CatPaw/cursor96_" + _name), "cursor96_" + _name);
                Assert.IsNotNull(Resources.Load<Sprite>("CatPaw/reticle_" + _name), "reticle_" + _name + " (Sprite import)");
            }
            Assert.IsNotNull(Resources.Load<Sprite>("CatPaw/reticle_dot"));
        }
    }
}
