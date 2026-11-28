using System.Collections.Generic;
using NUnit.Framework;
using UI.BoardUI;
using UnityEngine;

namespace Tests.Editor
{
    /// <summary>
    /// The role picker's fan. Maquettes eventail-roles v1 (A1): with 9 roles the fixed spacing put Luma and Ugës off
    /// screen (report of 2026-11-20). v2 (H2 + Z1): the cards moved aside on hover, so sliding the mouse to the next
    /// card landed on another one that had moved (Poyo, 2026-11-21): now nothing moves but the hovered card, and the
    /// places at rest decide which card is under the pointer.
    /// </summary>
    public class RoleFanLayoutTests
    {
        // The GameScene values (rolePickerCardSpacing 5, angle 8, height 0.15) and the new fields' defaults.
        private static RoleFanLayout.Settings Scene() => new()
        {
            spacing = 5f, spacingAngle = 8f, maxSpan = 24f, maxTotalAngle = 36f, edgeDrop = 2f, heightStep = 0.15f,
            cardWidth = 6.3f, cardHeight = 8.8f, hoverScale = 0.84f, cardHoverScale = 1.45f, hoverLift = 2.6f, hoverRaise = 0.3f,
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
        public void HoveringACardMovesNoOtherOne([Values(3, 9, 11, 18)] int _n)
        {
            RoleFanLayout.Settings _s = Scene();
            RoleFanLayout.Slot[] _rest = RoleFanLayout.Compute(_n, -1, Vector3.zero, _s);
            for (int _h = 0; _h < _n; _h++)
            {
                RoleFanLayout.Slot[] _slots = RoleFanLayout.Compute(_n, _h, Vector3.zero, _s);
                for (int _i = 0; _i < _n; _i++)
                {
                    if (_i == _h) continue;
                    Assert.AreEqual(_rest[_i].position, _slots[_i].position, $"{_n} roles, card {_h} hovered: card {_i} moved");
                    Assert.AreEqual(_rest[_i].yaw, _slots[_i].yaw, 1e-5f);
                }
            }
        }

        [Test]
        public void TheHoveredCardRisesAlongItsSlantAndStaysInTheFan([Values(3, 9, 11, 18)] int _n)
        {
            RoleFanLayout.Settings _s = Scene();
            RoleFanLayout.Slot[] _rest = RoleFanLayout.Compute(_n, -1, Vector3.zero, _s);
            float _half = (_n - 1) * RoleFanLayout.Step(_n, _s) / 2f;
            float _grown = _s.cardWidth * _s.hoverScale * _s.cardHoverScale;
            for (int _h = 0; _h < _n; _h++)
            {
                RoleFanLayout.Slot _hov = RoleFanLayout.Compute(_n, _h, Vector3.zero, _s)[_h];
                string _at = $"{_n} roles, card {_h} hovered";
                Assert.AreEqual(_rest[_h].yaw, _hov.yaw, 1e-5f, $"{_at}: keeps its angle");
                Assert.AreEqual(_s.hoverScale, _hov.scale, 1e-5f, _at);
                Assert.Greater(_hov.position.z, _rest[_h].position.z + _s.hoverLift * 0.9f, $"{_at}: rises");
                _s.hoverForwardRise = 2.9f; // what its own hover already raises it on screen is taken off
                float _expected = _rest[_h].position.z + Mathf.Max(RoleFanLayout.Up(_rest[_h].yaw).y * _s.hoverLift - 2.9f, RoleFanLayout.MinLift);
                Assert.AreEqual(_expected, RoleFanLayout.Compute(_n, _h, Vector3.zero, _s)[_h].position.z, 1e-4f, _at);
                _s.hoverForwardRise = 0f;
                for (int _i = 0; _i < _n; _i++)
                {
                    if (_i != _h) Assert.Greater(_hov.position.y, _rest[_i].position.y, $"{_at}: drawn over card {_i}");
                }
                Assert.LessOrEqual(Mathf.Abs(_hov.position.x) + _grown / 2f, _half + _s.cardWidth / 2f + 1e-3f, $"{_at}: inside the fan");
            }
        }

        [Test]
        public void TheHoveredCardAppearsWhereTheFlatLayoutPutsIt([Values(0, 4, 10)] int _h)
        {
            // Hovered, a card comes towards the camera and spreads away from the screen's centre (x = 3 here: the
            // fan pushed left by a pinned card): placed closer to it, it appears where the flat layout wants it.
            RoleFanLayout.Settings _flat = Scene();
            RoleFanLayout.Settings _persp = Scene();
            _persp.hoverPerspective = 1.32f;
            _persp.perspectiveCenterX = 3f;
            RoleFanLayout.Slot[] _a = RoleFanLayout.Compute(11, _h, Vector3.zero, _flat);
            RoleFanLayout.Slot[] _b = RoleFanLayout.Compute(11, _h, Vector3.zero, _persp);
            Assert.AreEqual(_a[_h].position.x, 3f + (_b[_h].position.x - 3f) * 1.32f, 1e-3f);
        }

        [Test]
        public void SlidingAlongTheFanGoesCardByCard([Values(3, 9, 11, 18)] int _n)
        {
            // The pointer slides from left to right through the middle of the cards, the fan reacting at each step:
            // every card is hovered once, in order, even under the grown card.
            RoleFanLayout.Settings _s = Scene();
            RoleFanLayout.Slot[] _rest = RoleFanLayout.Compute(_n, -1, Vector3.zero, _s);
            float _apparent = _s.hoverScale * _s.cardHoverScale;
            var _seen = new List<int>();
            int _hovered = -1;
            float _from = _rest[0].position.x - _s.cardWidth, _to = _rest[_n - 1].position.x + _s.cardWidth / 2f - 0.1f;
            for (float _x = _from; _x <= _to; _x += 0.05f)
            {
                RoleFanLayout.Slot _slot = _hovered >= 0 ? RoleFanLayout.Compute(_n, _hovered, Vector3.zero, _s)[_hovered] : default;
                int _hit = RoleFanLayout.HitTest(_rest, new Vector2(_x, -1f), _s, _hovered, _slot, _apparent);
                if (_hit != _hovered && _hit >= 0) _seen.Add(_hit);
                _hovered = _hit;
            }
            CollectionAssert.AreEqual(System.Linq.Enumerable.Range(0, _n), _seen, $"{_n} roles: {string.Join(",", _seen)}");
        }

        [Test]
        public void EachCardIsLookedForAtItsOwnHeight()
        {
            // A camera above the fan's centre: the ray through a card's strip (the last ones stacked 1.5 higher) crosses
            // the base plane further out. Looked for at its own height, it is that card; in the base plane, not always.
            RoleFanLayout.Settings _s = Scene();
            RoleFanLayout.Slot[] _rest = RoleFanLayout.Compute(11, -1, Vector3.zero, _s);
            var _eye = new Vector3(0f, 30f, 0f);
            for (int _i = 0; _i < 11; _i++)
            {
                // on its visible strip: just inside its left edge, at mid-height
                Vector2 _r = RoleFanLayout.Right(_rest[_i].yaw);
                Vector3 _target = _rest[_i].position + new Vector3(_r.x, 0f, _r.y) * (-_s.cardWidth / 2f + 0.4f);
                Vector3 _dir = _target - _eye;
                Vector2 PointAt(float _y) { Vector3 _p = _eye + _dir * ((_y - _eye.y) / _dir.y); return new Vector2(_p.x, _p.z); }
                int _hit = RoleFanLayout.HitTest(_rest, PointAt, PointAt(0f), _s, -1, default, 1f);
                Assert.AreEqual(_i, _hit, $"card {_i}");
            }
        }

        [Test]
        public void GoingUpOntoTheGrownCardKeepsIt()
        {
            RoleFanLayout.Settings _s = Scene();
            RoleFanLayout.Slot[] _rest = RoleFanLayout.Compute(11, -1, Vector3.zero, _s);
            RoleFanLayout.Slot _hov = RoleFanLayout.Compute(11, 5, Vector3.zero, _s)[5];
            // Just above the fan's middle card at rest, on the risen card.
            var _above = new Vector2(_hov.position.x, _rest[5].position.z + _s.cardHeight / 2f + 1f);
            Assert.AreEqual(5, RoleFanLayout.HitTest(_rest, _above, _s, 5, _hov, _s.hoverScale * _s.cardHoverScale));
            Assert.AreEqual(-1, RoleFanLayout.HitTest(_rest, _above, _s, -1, default, 1f), "nothing hovered: nothing there");
        }
    }
}
