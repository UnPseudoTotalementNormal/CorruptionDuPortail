using System;
using UnityEngine;

namespace FX
{
    /// <summary>
    /// The geometry of <see cref="CorkBoardLamp"/>, a framing projector: a spot light set on the board's normal whose
    /// cookie is the board's own rectangle, so the beam covers the board and nothing in front of it (the table, the
    /// cards). The cookie also cancels the spot's own fall-off (inverse square, slant) and replaces it with
    /// <see cref="Profile"/>: brightest at the top middle, softer towards both ends and the bottom edge, like a lamp
    /// hung above a picture. Pure: no scene, no light, tested in EditMode.
    /// </summary>
    public static class CorkBoardLampFraming
    {
        /// <summary>How the light is spread over the board, in fractions of the brightest point (top middle).</summary>
        [Serializable]
        public struct Profile
        {
            [Tooltip("Brightness at both ends of the board.")]
            [Range(0f, 1f)] public float endLevel;
            [Tooltip("Brightness along the bottom edge (the top edge is 1).")]
            [Range(0f, 1f)] public float bottomLevel;
            [Tooltip("Width of the soft border inside the board's outline, in fractions of its length.")]
            [Range(0.001f, 0.2f)] public float feather;
            [Tooltip("The beam stops this far inside the board's outline (fraction of its length): no light on what " +
                     "touches its edges.")]
            [Range(0f, 0.1f)] public float inset;

            public static Profile Default => new Profile { endLevel = 0.35f, bottomLevel = 0.45f, feather = 0.035f, inset = 0.01f };
        }

        /// <summary>Where the light sits and how wide its cone is, for one board.</summary>
        public readonly struct Framing
        {
            /// <summary>Light position: on the board's normal, <see cref="Distance"/> in front of its face.</summary>
            public readonly Vector3 Position;
            /// <summary>Light rotation: looking at the board, its up being the board's up.</summary>
            public readonly Quaternion Rotation;
            public readonly float Distance;
            /// <summary>Half the board's length and height (world units).</summary>
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

        // The board's ends sit this far inside the cookie: the corners of its rectangle stay inside the cone's circle
        // and the spot's own angular fade (between inner and outer angle) never reaches them.
        private const float CookieFill = 0.94f;

        /// <summary>
        /// Frames a box-shaped board (a mesh of local bounds <paramref name="_localBounds"/>, its face being the side
        /// that looks along <c>-forward</c>, the way a Quad faces its viewer).
        /// </summary>
        public static Framing Frame(Transform _board, Bounds _localBounds, float _distance)
        {
            Vector3 _scale = _board.lossyScale;
            float _halfLength = Mathf.Abs(_localBounds.extents.x * _scale.x);
            float _halfHeight = Mathf.Abs(_localBounds.extents.y * _scale.y);
            float _halfThickness = Mathf.Abs(_localBounds.extents.z * _scale.z);
            Vector3 _normal = -_board.forward;
            Vector3 _faceCentre = _board.TransformPoint(_localBounds.center) + _normal * _halfThickness;
            Vector3 _position = _faceCentre + _normal * _distance;
            Quaternion _rotation = Quaternion.LookRotation(-_normal, _board.up);
            float _tanHalfAngle = _halfLength / (CookieFill * _distance);
            return new Framing(_position, _rotation, _distance, _halfLength, _halfHeight, _tanHalfAngle);
        }

        /// <summary>
        /// Light the spot must let through toward light-space direction (<paramref name="_tanX"/>, <paramref name="_tanY"/>),
        /// before normalisation: the profile there, times what cancels the inverse square and the slant on the board.
        /// 0 outside the board.
        /// </summary>
        public static float CookieValue(in Framing _framing, in Profile _profile, float _tanX, float _tanY)
        {
            float _x = _tanX * _framing.Distance;
            float _y = _tanY * _framing.Distance;
            float _u = _x / _framing.HalfLength;
            float _v = _y / _framing.HalfHeight;
            float _limit = 1f - _profile.inset;
            // The border is as wide on the short sides as on the long ones (world units, not fractions).
            float _feather = Mathf.Max(_profile.feather, 1e-4f);
            float _featherV = _feather * _framing.HalfLength / _framing.HalfHeight;
            float _edge = Smooth01((_limit - Mathf.Abs(_u)) / _feather) * Smooth01((_limit - Mathf.Abs(_v)) / _featherV);
            if (_edge <= 0f)
            {
                return 0f;
            }
            float _along = Mathf.Lerp(_profile.endLevel, 1f, Mathf.Cos(Mathf.Clamp(_u, -1f, 1f) * Mathf.PI * 0.5f));
            float _across = Mathf.Lerp(_profile.bottomLevel, 1f, 0.5f + 0.5f * Mathf.Clamp(_v, -1f, 1f));
            // Irradiance from a point light on the normal: I · cookie · d / r³. Multiplying by (r / d)³ leaves the profile.
            float _d = _framing.Distance;
            float _r = Mathf.Sqrt(_d * _d + _x * _x + _y * _y);
            float _compensation = _r * _r * _r / (_d * _d * _d);
            return _edge * _along * _across * _compensation;
        }

        /// <summary>
        /// The cookie (R8, linear, clamped), normalised so its brightest texel is 1. <paramref name="_centreValue"/>:
        /// its value straight ahead (board centre), to turn a wanted brightness into an intensity
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
                name = "CorkBoardLampCookie",
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
            };
            _cookie.SetPixelData(_pixels, 0);
            _cookie.Apply(false, true);
            _centreValue = CookieValue(_framing, _profile, 0f, 0f) / _max;
            return _cookie;
        }

        /// <summary>
        /// Spot intensity (candela) giving <paramref name="_illuminance"/> (lux) at the board centre, through a cookie
        /// whose centre lets <paramref name="_centreValue"/> through.
        /// </summary>
        public static float Intensity(float _illuminance, float _distance, float _centreValue) =>
            _illuminance * _distance * _distance / Mathf.Max(_centreValue, 0.01f);

        private static float Smooth01(float _t)
        {
            _t = Mathf.Clamp01(_t);
            return _t * _t * (3f - 2f * _t);
        }
    }
}
