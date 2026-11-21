using System;
using UnityEngine;

namespace UI.BoardUI
{
    /// <summary>
    /// Where each card of the role picker's fan goes, and which card the pointer is on.
    /// Maquettes eventail-roles: v1 A1, the fan tightens so that every role stays on screen whatever their number (9
    /// roles used to push Luma and Ugës off screen, report of 2026-11-20); v2 H2 + Z1, the hovered card rises out of
    /// the fan along its own slant and grows a little while the others never move, and the card under the pointer is
    /// the one whose place AT REST is under it (sliding along the fan goes card by card, even under the grown card).
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
            [Tooltip("Largeur d'une carte.")]
            public float cardWidth;
            [Tooltip("Hauteur d'une carte.")]
            public float cardHeight;
            [Tooltip("Grossissement de la carte survolée par l'éventail (s'ajoute à celui du survol de la carte).")]
            public float hoverScale;
            [Tooltip("Grossissement à l'écran que le survol de la carte donne déjà (CardRoleAnimation : zoom et avancée vers la caméra), mesuré en jeu.")]
            public float cardHoverScale;
            [Tooltip("De combien la carte survolée sort de l'éventail, le long de sa pente.")]
            public float hoverLift;
            [Tooltip("De combien le survol de la carte (son avancée vers la caméra) la fait déjà monter à l'écran, mesuré en jeu : retiré de la montée le long de la pente.")]
            public float hoverForwardRise;
            [Tooltip("De combien la carte survolée passe au-dessus de la plus haute des autres, vers la caméra.")]
            public float hoverRaise;
            [Tooltip("De combien le survol de la carte (avancée vers la caméra) l'écarte du centre de l'écran, en perspective, mesuré en jeu. 1 = pas de perspective.")]
            public float hoverPerspective;
            [Tooltip("Où la caméra regarde, en x local (le centre de l'écran) : la perspective écarte la carte survolée de ce point.")]
            public float perspectiveCenterX;
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

        /// <summary>A card's "up" on screen (along its slant) in the fan's plane (x, z), for a yaw in degrees.</summary>
        public static Vector2 Up(float _yaw) => new(Mathf.Sin(_yaw * Mathf.Deg2Rad), Mathf.Cos(_yaw * Mathf.Deg2Rad));

        /// <summary>A card's "right" on screen in the fan's plane (x, z), for a yaw in degrees.</summary>
        public static Vector2 Right(float _yaw) => new(Mathf.Cos(_yaw * Mathf.Deg2Rad), -Mathf.Sin(_yaw * Mathf.Deg2Rad));

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

            // The hovered card rises along its own slant, keeps its angle, grows a little and comes on top. Nothing
            // else moves: the pointer finds every other card where it was.
            Slot _rest = _slots[_hovered];
            Vector2 _up = Up(_rest.yaw);
            float _x = _rest.position.x + _up.x * _s.hoverLift;

            // Where it APPEARS stays within the fan's width (the screen's edge, the pinned card on the right).
            float _w = _s.cardWidth;
            float _grown = _w * _s.hoverScale * _s.cardHoverScale;
            float _halfSpan = (_count - 1) * _step / 2f;
            float _min = _center.x - _halfSpan - _w / 2f + _grown / 2f, _max = _center.x + _halfSpan + _w / 2f - _grown / 2f;
            if (_min <= _max)
            {
                _x = Mathf.Clamp(_x, _min, _max);
            }

            // Its own hover brings it towards the camera, which pushes it away from the screen's centre in perspective
            // (the first card of a fan used to leave the screen, video of 2026-11-21): place it that much closer.
            float _perspective = _s.hoverPerspective > 0f ? _s.hoverPerspective : 1f;
            Slot _h = _rest;
            _h.scale = _s.hoverScale;
            _h.position.x = _s.perspectiveCenterX + (_x - _s.perspectiveCenterX) / _perspective;
            // Its own hover already brings it up the screen (it comes towards a camera that looks down): that much less,
            // but never below its place (the edge cards, tilted, dipped under the screen's bottom: autoplay, 2026-11-21).
            _h.position.z = _rest.position.z + Mathf.Max(_up.y * _s.hoverLift - _s.hoverForwardRise, MinLift);
            _h.position.y = _center.y + (_count - 1) * Mathf.Abs(_s.heightStep) + _s.hoverRaise;
            _slots[_hovered] = _h;
            return _slots;
        }

        /// <summary>The least the hovered card rises above its place, besides what its own hover does.</summary>
        public const float MinLift = 0.4f;

        /// <summary>Whether <paramref name="_point"/> (x, z in the fan's plane) is on a card drawn at <paramref name="_slot"/>,
        /// grown by <paramref name="_scale"/>.</summary>
        public static bool Contains(Slot _slot, Vector2 _point, Settings _s, float _scale = 1f)
        {
            Vector2 _d = _point - new Vector2(_slot.position.x, _slot.position.z);
            float _lx = Vector2.Dot(_d, Right(_slot.yaw)), _lz = Vector2.Dot(_d, Up(_slot.yaw));
            return Mathf.Abs(_lx) <= _s.cardWidth * _scale / 2f && Mathf.Abs(_lz) <= _s.cardHeight * _scale / 2f;
        }

        /// <summary>
        /// The card under <paramref name="_point"/> (x, z in the fan's plane), -1 for none (maquettes eventail-roles v2,
        /// Z1). The places at rest decide, the topmost first (the right card is drawn over the left one); off them,
        /// the hovered card keeps the pointer where it is drawn grown (going up onto it keeps it).
        /// </summary>
        /// <param name="_rest">the fan at rest (<see cref="Compute"/> with no card hovered).</param>
        /// <param name="_hovered">the card hovered now, -1 for none.</param>
        /// <param name="_hoveredSlot">where the hovered card is laid out now.</param>
        /// <param name="_hoveredApparentScale">how much bigger it is drawn than at rest (the fan's and its own hover).</param>
        public static int HitTest(Slot[] _rest, Vector2 _point, Settings _s, int _hovered, Slot _hoveredSlot, float _hoveredApparentScale)
            => HitTest(_rest, _ => _point, _point, _s, _hovered, _hoveredSlot, _hoveredApparentScale);

        /// <param name="_pointAtHeight">where the pointer's ray crosses the plane at a given height (y, towards the
        /// camera): each card is looked for at its own height (the fan is stacked towards the camera, and seen in
        /// perspective a single plane put the last cards' pointer one card off: autoplay calibration, 2026-11-21).</param>
        /// <param name="_hoveredPoint">where the ray crosses the risen card's plane.</param>
        public static int HitTest(Slot[] _rest, Func<float, Vector2> _pointAtHeight, Vector2 _hoveredPoint, Settings _s, int _hovered,
            Slot _hoveredSlot, float _hoveredApparentScale)
        {
            int _hit = -1;
            float _top = float.MinValue;
            for (int _i = 0; _i < _rest.Length; _i++)
            {
                if (_rest[_i].position.y >= _top && Contains(_rest[_i], _pointAtHeight(_rest[_i].position.y), _s))
                {
                    _hit = _i;
                    _top = _rest[_i].position.y;
                }
            }
            if (_hit >= 0)
            {
                return _hit;
            }
            return _hovered >= 0 && _hovered < _rest.Length && Contains(_hoveredSlot, _hoveredPoint, _s, _hoveredApparentScale) ? _hovered : -1;
        }
    }
}
