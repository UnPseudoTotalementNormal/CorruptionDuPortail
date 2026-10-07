using System.Collections;
using System.Reflection;
using Board.UI;
using NUnit.Framework;
using UI;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.TestTools;

namespace Tests.PlayMode
{
    /// <summary>
    /// Board3DButton forwards a 3D-model click to its logic-only CustomButton, and honours that button's
    /// enabled gate (e.g. AnonymeMessageButton disables itself when a message may not be sent). This is the
    /// user-facing contract of the 3D message buttons; the reticle/OnMouseDown plumbing that produces the
    /// pointer event is exercised in play, not here.
    /// </summary>
    public class Board3DButtonTests
    {
        [UnityTest]
        public IEnumerator ForwardsClickWhenTargetEnabled_AndGatesWhenDisabled()
        {
            var _buttonGo = new GameObject("Btn");
            var _button = _buttonGo.AddComponent<CustomButton>();
            int _clicks = 0;
            _button.onButtonClicked += () => _clicks++;

            var _modelGo = new GameObject("Model");
            _modelGo.AddComponent<BoxCollider>();
            var _board3DButton = _modelGo.AddComponent<Board3DButton>();
            typeof(Board3DButton)
                .GetField("targetButton", BindingFlags.NonPublic | BindingFlags.Instance)
                .SetValue(_board3DButton, _button);

            // Let Awake settle.
            yield return null;

            _board3DButton.OnPointerClick(new PointerEventData(EventSystem.current));
            Assert.AreEqual(1, _clicks, "Click must forward to the target CustomButton while it is enabled.");

            _button.enabled = false;
            _board3DButton.OnPointerClick(new PointerEventData(EventSystem.current));
            Assert.AreEqual(1, _clicks, "Click must be gated (ignored) while the target CustomButton is disabled.");

            Object.Destroy(_modelGo);
            Object.Destroy(_buttonGo);
        }

        // Pure decision behind the reticle's "don't reach 3D through UI" rule: suppressed iff a blocking uGUI
        // graphic is under the reticle (RaycastAll returned at least one hit). The live RaycastAll wiring and
        // the Vote non-regression are covered by the manual play matrix in the spec — not faithfully mockable.
        [Test]
        public void ShouldSuppressWorld_TrueOnlyWhenUiHitPresent()
        {
            Assert.IsFalse(Reticle.ReticleInteractor.ShouldSuppressWorld(0), "no uGUI under reticle → world active");
            Assert.IsTrue(Reticle.ReticleInteractor.ShouldSuppressWorld(1), "one blocking graphic → world suppressed");
            Assert.IsTrue(Reticle.ReticleInteractor.ShouldSuppressWorld(5), "several hits → still suppressed");
        }

        // A free cursor is the UI module's: a reticle click on top delivered each click twice (duplicate votes).
        [Test]
        public void ShouldDispatchClick_OnlyWithALockedCursor()
        {
            Assert.IsTrue(Reticle.ReticleInteractor.ShouldDispatchClick(true), "locked cursor → the reticle clicks");
            Assert.IsFalse(Reticle.ReticleInteractor.ShouldDispatchClick(false), "free cursor → the UI module clicks, not the reticle");
        }

        // Hover feedback rides the IPointer path (module + PhysicsRaycaster in free cursor, reticle in
        // embodied): enter hovers, exit clears — no stuck hover, no legacy OnMouse* involved.
        [UnityTest]
        public IEnumerator IPointer_HoversOnEnter_AndClearsOnExit()
        {
            var _modelGo = new GameObject("Model", typeof(BoxCollider));
            var _board3DButton = _modelGo.AddComponent<Board3DButton>();

            yield return null; // Awake

            _board3DButton.OnPointerEnter(new PointerEventData(EventSystem.current));
            Assert.IsTrue(Hovering(_board3DButton), "Model should hover on IPointerEnter.");

            _board3DButton.OnPointerExit(new PointerEventData(EventSystem.current));
            Assert.IsFalse(Hovering(_board3DButton), "Hover should clear on IPointerExit (no stuck hover).");

            Object.Destroy(_modelGo);
        }

        private static bool Hovering(Board3DButton _target) =>
            (bool)typeof(Board3DButton)
                .GetField("_hovering", BindingFlags.NonPublic | BindingFlags.Instance)
                .GetValue(_target);
    }
}
