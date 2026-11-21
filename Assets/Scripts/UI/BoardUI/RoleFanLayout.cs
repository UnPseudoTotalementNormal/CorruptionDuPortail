using System;
using UnityEngine;

namespace UI.BoardUI
{
    /// <summary>
    /// Where each card of the role picker's fan goes (maquettes eventail-roles v1, A1). The fan tightens so that every
    /// role stays on screen, whatever their number (9 roles used to push Luma and Ugës off screen, report of
    /// 2026-11-20); the hovered card straightens up, grows and comes on top, and the others tighten on both sides so
    /// that each one still shows a strip.
    /// Local space of the picker's centre: x = right on screen, z = up on screen, y = towards the camera.
    /// </summary>
    public static class RoleFanLayout
    {
        [Serializable]
        public struct Settings
        {
            [Tooltip("Écart entre deux cartes quand il y a la place (centre à centre).")]
            public float spacing;
            [Tooltip("Rotation entre deux cartes quand il y a la place (degrés).")]
            public float spacingAngle;
            [Tooltip("Largeur max de l'éventail, du centre de la première carte au centre de la dernière : au-delà, les cartes se resserrent.")]
            public float maxSpan;
            [Tooltip("Rotation max entre la première et la dernière carte (degrés).")]
            public float maxTotalAngle;
            [Tooltip("De combien les cartes du bord descendent à l'écran.")]
            public float edgeDrop;
            [Tooltip("Hauteur ajoutée par carte vers la caméra : la carte de droite passe sur celle de gauche.")]
            public float heightStep;
            [Tooltip("Largeur d'une carte (pour écarter les voisines de la carte survolée).")]
            public float cardWidth;
            [Tooltip("Grossissement de la carte survolée par l'éventail (s'ajoute à celui du survol de la carte).")]
            public float hoverScale;
            [Tooltip("Grossissement à l'écran que le survol de la carte donne déjà (CardRoleAnimation : zoom et avancée vers la caméra), mesuré en jeu.")]
            public float cardHoverScale;
            [Tooltip("De combien la carte survolée passe au-dessus de la plus haute des autres, vers la caméra.")]
            public float hoverRaise;
            [Tooltip("Marge entre la carte survolée et ses voisines écartées.")]
            public float hoverGap;
        }

        public struct Slot
        {
            public Vector3 position;
            public float yaw;
            public float scale;
        }

        /// <summary>Centre-to-centre distance between two neighbours of a fan of <paramref name="_count"/> cards.</summary>
        public static float Step(int _count, Settings _s) =>
            _count > 1 ? Mathf.Min(_s.spacing, _s.maxSpan / (_count - 1)) : 0f;

        /// <param name="_count">number of cards.</param>
        /// <param name="_hovered">index of the hovered card, -1 for none.</param>
        /// <param name="_center">the fan's centre (local position of the picker's centre, plus any offset).</param>
        public static Slot[] Compute(int _count, int _hovered, Vector3 _center, Settings _s)
        {
            Slot[] _slots = new Slot[Mathf.Max(0, _count)];
            if (_count <= 0)
            {
                return _slots;
            }

            float _c = (_count - 1) / 2f;
            float _step = Step(_count, _s);
            float _yawStep = _count > 1 ? Mathf.Min(_s.spacingAngle, _s.maxTotalAngle / (_count - 1)) : 0f;

            for (int _i = 0; _i < _count; _i++)
            {
                float _d = _i - _c;
                float _drop = _c > 0f ? _s.edgeDrop * (_d / _c) * (_d / _c) : 0f;
                _slots[_i] = new Slot
                {
                    position = _center + new Vector3(_d * _step, _i * _s.heightStep, -_drop),
                    yaw = _d * _yawStep,
                    scale = 1f,
                };
            }

            if (_hovered < 0 || _hovered >= _count)
            {
                return _slots;
            }

            // Each card shows the strip its right neighbour leaves uncovered (the right card is drawn over the left one).
            // Hovered, a card comes over all of them, straight and grown (G wide): its left neighbour must still show
            // its strip left of it, its right neighbour must start right of it. The fan keeps its rest width: both
            // sides tighten their strips to make that room, and the hovered card stays under the pointer.
            float _grown = _s.cardWidth * _s.hoverScale * _s.cardHoverScale;
            float _w = _s.cardWidth;
            float _halfSpan = (_count - 1) * _step / 2f;
            float _left = _center.x - _halfSpan, _right = _center.x + _halfSpan;
            float _leftGap = _grown / 2f - _w / 2f + _s.hoverGap;    // + the left neighbour's strip
            float _rightGap = _grown / 2f + _w / 2f + _s.hoverGap;   // the right neighbour clear of the grown card
            int _nLeft = _hovered, _nRight = _count - 1 - _hovered;

            float _restX = _slots[_hovered].position.x;
            float _xh = _restX;
            if (_nLeft > 0 && _nRight > 0 && _count > 2)
            {
                // Where both sides would show the same strips (the right side needs more room: its first card must
                // clear the grown card entirely, the left one only shows its strip beside it).
                float _even = (_right - _left - _leftGap - _rightGap) / (_count - 2);
                _xh = _left + _leftGap + _nLeft * _even;
            }

            // The pointer was on the card's strip at rest (its left part, one step wide): the grown card still covers it.
            float _growth = (_grown - _w) / 2f;
            _xh = Mathf.Clamp(_xh, _restX - _w / 2f + _step - _grown / 2f + PointerMargin, _restX + _growth - PointerMargin);
            // The grown card stays within the fan's width (screen edge, pinned card on the right)...
            if (_right - _left > 2f * _growth)
            {
                _xh = Mathf.Clamp(_xh, _left + _growth, _right - _growth);
            }
            // ...and leaves each side room for its strips.
            float _minX = _nLeft > 0 ? _left + _leftGap + _nLeft * MinStrip : float.MinValue;
            float _maxX = _nRight > 0 ? _right - _rightGap - (_nRight - 1) * MinStrip : float.MaxValue;
            if (_minX <= _maxX)
            {
                _xh = Mathf.Clamp(_xh, _minX, _maxX);
            }

            Slot _h = _slots[_hovered];
            _h.yaw = 0f;
            _h.scale = _s.hoverScale;
            _h.position.x = _xh;
            _h.position.y = _center.y + (_count - 1) * Mathf.Abs(_s.heightStep) + _s.hoverRaise;
            _slots[_hovered] = _h;

            if (_nLeft > 0)
            {
                float _strip = Mathf.Clamp((_xh - _left - _leftGap) / _nLeft, MinStrip, _step);
                float _x = _xh - _leftGap - Mathf.Min(_strip, _w);
                for (int _i = _hovered - 1; _i >= 0; _i--)
                {
                    _slots[_i].position.x = _x;
                    _x -= _strip;
                }
            }

            if (_nRight > 0)
            {
                float _strip = _nRight > 1 ? Mathf.Clamp((_right - _xh - _rightGap) / (_nRight - 1), MinStrip, _step) : _step;
                float _x = _xh + _rightGap;
                for (int _i = _hovered + 1; _i < _count; _i++)
                {
                    _slots[_i].position.x = _x;
                    _x += _strip;
                }
            }

            return _slots;
        }

        /// <summary>The narrowest strip a card keeps visible while another one is hovered.</summary>
        public const float MinStrip = 0.6f;

        /// <summary>How far inside the grown card the pointer's rest strip stays when the hovered card moves.</summary>
        public const float PointerMargin = 0.3f;
    }
}
