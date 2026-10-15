using System;
using UnityEngine;

namespace FX
{
    /// <summary>
    /// The geometry of a <see cref="FramedLamp"/>, a framing projector: a spot light set on the normal of a flat target
    /// (the cork board, the letters) whose cookie is drawn on the target's own outline, so the beam lights the target and
    /// nothing around it (the table, the cards). The cookie also cancels the spot's own fall-off (inverse square, slant)
    /// and replaces it with <see cref="Profile"/>: a soft pool that fades out before the target's edges, like a lamp hung
    /// above a picture. Pure: no scene, no light, tested in EditMode.
    /// </summary>
    public static class LampFraming
    {
        /// <summary>
        /// How the light is spread over the target, in fractions of its brightest point. A soft pool, not a frame: full
        /// in a core, then fading out over <see cref="endFade"/> towards both ends and <see cref="edgeFade"/> towards
        /// the top and bottom edges, along a curve with no visible step (rounded corners, flat at both ends of the fade).
        /// </summary>
        [Serializable]
        public struct Profile
        {
            [Tooltip("Length (m) over which the light fades out towards each end of the target.")]
            [Min(0.01f)] public float endFade;
            [Tooltip("Length (m) over which the light fades out towards the top and bottom edges.")]
            [Min(0.01f)] public float edgeFade;
            [Tooltip("Brightness left near both ends before their fade, as a lamp hung above the middle would give.")]
            [Range(0f, 1f)] public float endLevel;
            [Tooltip("Brightness along the bottom edge (the top edge is 1).")]
            [Range(0f, 1f)] public float bottomLevel;
            [Tooltip("How far (m) the pool may reach past the target's edges, onto what it stands on (0 = the target only).")]
            [Min(0f)] public float spill;
            [Tooltip("Off: nothing past the top edge (a standing board: that light would fly over it onto whoever sits " +
                     "behind). On: the pool spills past the top edge too (a target lying flat).")]
            public bool spillPastTopEdge;

            /// <summary>The cork board's look (variant C): a pool brighter at the top, a little halo at its foot.</summary>
            public static Profile Default => new Profile
            {
                endFade = 2.5f, edgeFade = 1.1f, endLevel = 0.75f, bottomLevel = 0.55f, spill = 0.6f,
            };
        }

        /// <summary>Where the light sits and how wide its cone is, for one target.</summary>
        public readonly struct Framing
        {
            /// <summary>Light position: on the target's normal, <see cref="Distance"/> in front of its face.</summary>
            public readonly Vector3 Position;
            /// <summary>Light rotation: looking at the target, its up being the target's up.</summary>
            public readonly Quaternion Rotation;
            public readonly float Distance;
            /// <summary>Half the target's length and height (world units).</summary>
            public readonly float HalfLength;
            public readonly float HalfHeight;
            /// <summary>Tangent of the cone's half angle: the cookie's edge, in light space.</summary>
            public readonly float TanHalfAngle;

            public Framing(Vector3 _position, Quaternion _rotation, float _distance, float _halfLength, float _halfHeight,
                float _tanHalfAngle)
            {
                Position = _position;
                Rotation = _rotation;
                Distance = _distance;
                HalfLength = _halfLength;
                HalfHeight = _halfHeight;
                TanHalfAngle = _tanHalfAngle;
            }

            /// <summary>Full cone angle (degrees), for <see cref="Light.spotAngle"/>.</summary>
            public float SpotAngle => 2f * Mathf.Atan(TanHalfAngle) * Mathf.Rad2Deg;
        }

        // The pool's corners sit this far inside the cone's circle (the cookie's square holds that circle): the cone never
        // cuts them and the spot's own angular fade (between inner and outer angle) never reaches them.
        private const float CookieFill = 0.96f;
        // Metres kept dark under the top edge, so the cookie's filtering never lets a sliver past it.
        private const float TopEdgeMargin = 0.01f;

        /// <summary>
        /// Frames a box-shaped board (a mesh of local bounds <paramref name="_localBounds"/>, its face being the side
        /// that looks along <c>-forward</c>, the way a Quad faces its viewer).
        /// </summary>
        public static Framing Frame(Transform _board, Bounds _localBounds, float _distance, float _spill = 0f)
        {
            Vector3 _scale = _board.lossyScale;
            float _halfLength = Mathf.Abs(_localBounds.extents.x * _scale.x);
            float _halfHeight = Mathf.Abs(_localBounds.extents.y * _scale.y);
            float _halfThickness = Mathf.Abs(_localBounds.extents.z * _scale.z);
            Vector3 _normal = -_board.forward;
            Vector3 _faceCentre = _board.TransformPoint(_localBounds.center) + _normal * _halfThickness;
            return Frame(_faceCentre, _normal, _board.up, _halfLength, _halfHeight, _distance, _spill);
        }

        /// <summary>
        /// Frames something lying flat, of world bounds <paramref name="_worldBounds"/> (a renderer's): the light hangs
        /// straight above its top face, the pool's length along world X and its height along world Z.
        /// </summary>
        public static Framing FrameFromAbove(Bounds _worldBounds, float _distance, float _spill = 0f)
        {
            Vector3 _topCentre = new Vector3(_worldBounds.center.x, _worldBounds.max.y, _worldBounds.center.z);
            return Frame(_topCentre, Vector3.up, Vector3.forward, _worldBounds.extents.x, _worldBounds.extents.z,
                _distance, _spill);
        }

        /// <summary>
        /// Frames a flat rectangle of centre <paramref name="_faceCentre"/> facing <paramref name="_normal"/>, of half size
        /// <paramref name="_halfLength"/> × <paramref name="_halfHeight"/> (its height along <paramref name="_up"/>).
        /// </summary>
        public static Framing Frame(Vector3 _faceCentre, Vector3 _normal, Vector3 _up, float _halfLength, float _halfHeight,
            float _distance, float _spill = 0f)
        {
            _normal = _normal.normalized;
            Vector3 _position = _faceCentre + _normal * _distance;
            Quaternion _rotation = Quaternion.LookRotation(-_normal, _up);
            // The cone is round: it must hold the pool's corners, its diagonal.
            float _spillOut = Mathf.Max(_spill, 0f);
            float _cornerReach = new Vector2(_halfLength + _spillOut, _halfHeight + _spillOut).magnitude;
            float _tanHalfAngle = _cornerReach / (CookieFill * _distance);
            return new Framing(_position, _rotation, _distance, _halfLength, _halfHeight, _tanHalfAngle);
        }

        /// <summary>
        /// The wanted brightness at (<paramref name="_x"/>, <paramref name="_y"/>) metres from the target's centre, along
        /// its length and its height (up = towards the top edge): the <see cref="Profile"/>. 0 past the pool (the target's
        /// outline plus the spill) and, unless <see cref="Profile.spillPastTopEdge"/>, above the top edge.
        /// </summary>
        public static float Shape(in Framing _framing, in Profile _profile, float _x, float _y)
        {
            // Cut on a standing board's own top edge (minus a hair for the cookie's filtering): the step sits on the edge
            // itself, so it never shows, and nothing flies over the board.
            if (!_profile.spillPastTopEdge && _y > _framing.HalfHeight - TopEdgeMargin)
            {
                return 0f;
            }
            float _spill = Mathf.Max(_profile.spill, 0f);
            float _reachX = _framing.HalfLength + _spill;
            float _reachY = _framing.HalfHeight + _spill;
            float _fadeX = Mathf.Clamp(_profile.endFade, 0.01f, _reachX);
            float _fadeY = Mathf.Clamp(_profile.edgeFade, 0.01f, _reachY);
            // Distance out of the full-brightness core, in fade lengths: an ellipse arc round each corner.
            float _qx = Mathf.Max(Mathf.Abs(_x) - (_reachX - _fadeX), 0f) / _fadeX;
            float _qy = Mathf.Max(Mathf.Abs(_y) - (_reachY - _fadeY), 0f) / _fadeY;
            float _t2 = _qx * _qx + _qy * _qy;
            if (_t2 >= 1f)
            {
                return 0f;
            }
            float _pool = (1f - _t2) * (1f - _t2);
            float _along = Mathf.Lerp(_profile.endLevel, 1f, Mathf.Cos(Mathf.Clamp(_x / _reachX, -1f, 1f) * Mathf.PI * 0.5f));
            float _across = Mathf.Lerp(_profile.bottomLevel, 1f, 0.5f + 0.5f * Mathf.Clamp(_y / _framing.HalfHeight, -1f, 1f));
            return _pool * _along * _across;
        }

        /// <summary>
        /// Light the spot must let through toward light-space direction (<paramref name="_tanX"/>, <paramref name="_tanY"/>),
        /// before normalisation: <see cref="Shape"/> there, times what cancels the inverse square and the slant on the
        /// target's plane. 0 outside the pool.
        /// </summary>
        public static float CookieValue(in Framing _framing, in Profile _profile, float _tanX, float _tanY)
        {
            float _x = _tanX * _framing.Distance;
            float _y = _tanY * _framing.Distance;
            float _shape = Shape(_framing, _profile, _x, _y);
            if (_shape <= 0f)
            {
                return 0f;
            }
            // Irradiance from a point light on the normal: I · cookie · d / r³. Multiplying by (r / d)³ leaves the shape.
            float _d = _framing.Distance;
            float _r = Mathf.Sqrt(_d * _d + _x * _x + _y * _y);
            return _shape * _r * _r * _r / (_d * _d * _d);
        }

        /// <summary>
        /// The cookie (R8, linear, clamped), normalised so its brightest texel is 1. <paramref name="_centreValue"/>:
        /// its value straight ahead (target centre), to turn a wanted brightness into an intensity
        /// (<see cref="Intensity"/>).
        /// </summary>
        public static Texture2D BuildCookie(in Framing _framing, in Profile _profile, int _resolution, out float _centreValue)
        {
            float[] _values = new float[_resolution * _resolution];
            float _max = 0f;
            for (int _j = 0; _j < _resolution; _j++)
            {
                float _tanY = ((_j + 0.5f) / _resolution * 2f - 1f) * _framing.TanHalfAngle;
                for (int _i = 0; _i < _resolution; _i++)
                {
                    float _tanX = ((_i + 0.5f) / _resolution * 2f - 1f) * _framing.TanHalfAngle;
                    float _value = CookieValue(_framing, _profile, _tanX, _tanY);
                    _values[_j * _resolution + _i] = _value;
                    _max = Mathf.Max(_max, _value);
                }
            }
            if (_max <= 0f)
            {
                _max = 1f;
            }
            byte[] _pixels = new byte[_values.Length];
            for (int _k = 0; _k < _values.Length; _k++)
            {
                _pixels[_k] = (byte)Mathf.RoundToInt(255f * _values[_k] / _max);
            }
            Texture2D _cookie = new Texture2D(_resolution, _resolution, TextureFormat.R8, false, true)
            {
                name = "LampCookie",
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
            };
            _cookie.SetPixelData(_pixels, 0);
            _cookie.Apply(false, true);
            _centreValue = CookieValue(_framing, _profile, 0f, 0f) / _max;
            return _cookie;
        }

        /// <summary>
        /// Spot intensity (candela) giving <paramref name="_illuminance"/> (lux) at the target centre, through a cookie
        /// whose centre lets <paramref name="_centreValue"/> through.
        /// </summary>
        public static float Intensity(float _illuminance, float _distance, float _centreValue) =>
            _illuminance * _distance * _distance / Mathf.Max(_centreValue, 0.01f);

    }
}
