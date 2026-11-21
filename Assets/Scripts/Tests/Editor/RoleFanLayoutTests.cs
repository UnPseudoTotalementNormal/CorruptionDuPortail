using NUnit.Framework;
using UI.BoardUI;
using UnityEngine;

namespace Tests.Editor
{
    /// <summary>
    /// The role picker's fan (maquettes eventail-roles v1, A1). Report of 2026-11-20: with 9 roles the fixed spacing
    /// put Luma and Ugës off screen, so the Mage could not guess them.
    /// </summary>
    public class RoleFanLayoutTests
    {
        // The GameScene values (rolePickerCardSpacing 5, angle 8, height 0.15) and the new fields' defaults.
        private static RoleFanLayout.Settings Scene() => new()
        {
            spacing = 5f, spacingAngle = 8f, maxSpan = 24f, maxTotalAngle = 36f, edgeDrop = 2f, heightStep = 0.15f,
            cardWidth = 6.3f, hoverScale = 0.95f, cardHoverScale = 1.45f, hoverRaise = 0.3f, hoverGap = 0.3f,
        };

        [Test]
        public void EveryCountFitsTheMaxSpan()
        {
            RoleFanLayout.Settings _s = Scene();
            for (int _n = 1; _n <= 18; _n++)
            {
                RoleFanLayout.Slot[] _slots = RoleFanLayout.Compute(_n, -1, Vector3.zero, _s);
                Assert.AreEqual(_n, _slots.Length);
                foreach (RoleFanLayout.Slot _slot in _slots)
                {
                    Assert.LessOrEqual(Mathf.Abs(_slot.position.x), _s.maxSpan / 2f + 1e-4f, $"{_n} roles");
                    Assert.LessOrEqual(Mathf.Abs(_slot.yaw), _s.maxTotalAngle / 2f + 1e-4f, $"{_n} roles");
                    Assert.IsFalse(float.IsNaN(_slot.position.x) || float.IsNaN(_slot.yaw), $"{_n} roles");
                }
            }
        }

        [Test]
        public void NineRolesTightenInsteadOfLeavingTheScreen()
        {
            RoleFanLayout.Settings _s = Scene();
            RoleFanLayout.Slot[] _slots = RoleFanLayout.Compute(9, -1, Vector3.zero, _s);
            float _span = _slots[8].position.x - _slots[0].position.x;
            Assert.AreEqual(_s.maxSpan, _span, 1e-3f, "9 cards at spacing 5 would span 40, more than the screen");
            Assert.Less(RoleFanLayout.Step(9, _s), _s.spacing);
        }

        [Test]
        public void FewRolesKeepTheirSpacing()
        {
            RoleFanLayout.Settings _s = Scene();
            Assert.AreEqual(_s.spacing, RoleFanLayout.Step(4, _s), 1e-4f);
        }

        [Test]
        public void HoveringAnyCardKeepsEveryOtherOneShowingAStrip([Values(3, 5, 9, 11, 14, 18)] int _n)
        {
            RoleFanLayout.Settings _s = Scene();
            float _grown = _s.cardWidth * _s.hoverScale * _s.cardHoverScale;
            float _w = _s.cardWidth;
            RoleFanLayout.Slot[] _rest = RoleFanLayout.Compute(_n, -1, Vector3.zero, _s);
            float _half = (_n - 1) * RoleFanLayout.Step(_n, _s) / 2f;

            for (int _h = 0; _h < _n; _h++)
            {
                RoleFanLayout.Slot[] _slots = RoleFanLayout.Compute(_n, _h, Vector3.zero, _s);
                string _at = $"{_n} roles, card {_h} hovered";
                RoleFanLayout.Slot _hov = _slots[_h];

                Assert.AreEqual(0f, _hov.yaw, 1e-4f, _at);
                Assert.AreEqual(_s.hoverScale, _hov.scale, 1e-4f, _at);
                for (int _i = 0; _i < _n; _i++)
                {
                    if (_i == _h) continue;
                    Assert.Greater(_hov.position.y, _slots[_i].position.y, $"{_at}: drawn over card {_i}");
                    Assert.LessOrEqual(Mathf.Abs(_slots[_i].position.x), _half + 1e-3f, $"{_at}: card {_i} stays in the fan's width");
                }

                // The grown card does not spill out of the fan (screen edge, the pinned card on its right).
                if (2f * _half > _grown - _w)
                {
                    Assert.LessOrEqual(Mathf.Abs(_hov.position.x) + _grown / 2f, _half + _w / 2f + 1e-3f, $"{_at}: grown card inside the fan");
                }

                // The left neighbour still shows a strip left of the grown card; the right one starts right of it.
                if (_h > 0)
                {
                    float _strip = (_hov.position.x - _grown / 2f) - (_slots[_h - 1].position.x - _w / 2f);
                    Assert.GreaterOrEqual(_strip, RoleFanLayout.MinStrip - 1e-3f, $"{_at}: left neighbour's strip");
                }
                if (_h < _n - 1)
                {
                    Assert.GreaterOrEqual(_slots[_h + 1].position.x - _w / 2f, _hov.position.x + _grown / 2f - 1e-3f,
                        $"{_at}: right neighbour clear");
                }
                // Every other card keeps a strip of its own between its neighbours.
                for (int _i = 0; _i + 1 < _n; _i++)
                {
                    if (_i == _h || _i + 1 == _h) continue;
                    Assert.GreaterOrEqual(_slots[_i + 1].position.x - _slots[_i].position.x, RoleFanLayout.MinStrip - 1e-3f, _at);
                }

                // The pointer was on the card's visible strip at rest (its left part): it is still on the grown card.
                float _restLeft = _rest[_h].position.x - _w / 2f;
                Assert.GreaterOrEqual(_restLeft, _hov.position.x - _grown / 2f - 1e-3f, $"{_at}: pointer left of the card");
                Assert.LessOrEqual(_restLeft + 0.5f, _hov.position.x + _grown / 2f + 1e-3f, $"{_at}: pointer right of the card");
            }
        }
    }
}
