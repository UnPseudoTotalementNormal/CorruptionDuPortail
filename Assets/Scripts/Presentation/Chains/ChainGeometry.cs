using System;
using System.Collections.Generic;
using UnityEngine;

namespace Presentation.Chains
{
    /// <summary>
    /// Chain links as a mesh (maquettes chaines v1/v2): each link is a stadium-shaped ring of round wire, laid one after
    /// another along a path, every other one turned a quarter turn about the path so they interlock. Sizes are shares of
    /// the cat's height, so a chain looks the same on any cat. No artist model yet: made here until one exists.
    /// </summary>
    public static class ChainGeometry
    {
        /// <summary>Points around a link's centreline, then around its wire.</summary>
        public const int RingSegments = 20;
        public const int TubeSegments = 6;
        public const int VerticesPerLink = RingSegments * TubeSegments;
        public const int IndicesPerLink = RingSegments * TubeSegments * 6;

        /// <summary>One link's shape: half its width (the round ends' radius), its straight sides, its wire's radius.</summary>
        public readonly struct LinkSize
        {
            public readonly float HalfWidth;
            public readonly float StraightLength;
            public readonly float WireRadius;

            public LinkSize(float _halfWidth, float _straightLength, float _wireRadius)
            {
                HalfWidth = _halfWidth;
                StraightLength = _straightLength;
                WireRadius = _wireRadius;
            }

            /// <summary>From one link's centre to the next one's: its inner length (the links hook into each other).</summary>
            public float Pitch => StraightLength + 2f * HalfWidth - 2f * WireRadius;

            /// <summary>The links of the mockups, for a cat <paramref name="_catHeight"/> high (ears included).</summary>
            public static LinkSize ForCatHeight(float _catHeight) => new(0.017f * _catHeight, 0.03f * _catHeight, 0.0072f * _catHeight);
        }

        /// <summary>Where a link sits: its centre, the path's direction, and the side its ring opens to.</summary>
        public readonly struct LinkFrame
        {
            public readonly Vector3 Centre;
            public readonly Vector3 Forward;
            public readonly Vector3 Side;

            public LinkFrame(Vector3 _centre, Vector3 _forward, Vector3 _side)
            {
                Centre = _centre;
                Forward = _forward;
                Side = _side;
            }
        }

        /// <summary>
        /// Lays links along <paramref name="_path"/>, one every <see cref="LinkSize.Pitch"/>, appended to
        /// <paramref name="_links"/> in the path's order. <paramref name="_normalAt"/> gives, for a point, the direction the
        /// first link's ring faces (out of the body it wraps); every other link is turned a quarter turn. Returns the count.
        /// </summary>
        public static int LayAlong(IReadOnlyList<Vector3> _path, Func<Vector3, Vector3> _normalAt, LinkSize _size, List<LinkFrame> _links)
        {
            if (_path == null || _path.Count < 2 || _size.Pitch <= 0f)
            {
                return 0;
            }
            var _cumulative = new float[_path.Count];
            for (int _i = 1; _i < _path.Count; _i++)
            {
                _cumulative[_i] = _cumulative[_i - 1] + Vector3.Distance(_path[_i - 1], _path[_i]);
            }
            float _total = _cumulative[_path.Count - 1];
            int _count = Mathf.FloorToInt(_total / _size.Pitch);
            int _segment = 0;
            for (int _l = 0; _l < _count; _l++)
            {
                float _s = (_l + 0.5f) * _size.Pitch;
                while (_segment < _path.Count - 2 && _cumulative[_segment + 1] < _s)
                {
                    _segment++;
                }
                float _span = Mathf.Max(1e-6f, _cumulative[_segment + 1] - _cumulative[_segment]);
                Vector3 _centre = Vector3.Lerp(_path[_segment], _path[_segment + 1], (_s - _cumulative[_segment]) / _span);
                Vector3 _forward = (_path[_segment + 1] - _path[_segment]).normalized;
                Vector3 _side = Vector3.Cross(_normalAt(_centre), _forward);
                if (_side.sqrMagnitude < 1e-8f)
                {
                    _side = Vector3.Cross(Vector3.up, _forward);
                }
                if (_side.sqrMagnitude < 1e-8f)
                {
                    _side = Vector3.Cross(Vector3.right, _forward);
                }
                _side.Normalize();
                if ((_l & 1) == 1)
                {
                    _side = Vector3.Cross(_forward, _side).normalized;
                }
                _links.Add(new LinkFrame(_centre, _forward, _side));
            }
            return _count;
        }

        /// <summary>Appends one link's vertices, normals and triangles (<see cref="VerticesPerLink"/>, <see cref="IndicesPerLink"/>).</summary>
        public static void AddLink(LinkFrame _link, LinkSize _size, List<Vector3> _vertices, List<Vector3> _normals, List<int> _indices)
        {
            Vector3 _forward = _link.Forward;
            Vector3 _plane = Vector3.Cross(_forward, _link.Side).normalized;   // the ring's own normal
            Vector3 _side = Vector3.Cross(_plane, _forward).normalized;
            float _a = _size.StraightLength, _c = _size.HalfWidth, _r = _size.WireRadius;
            float _perimeter = 2f * _a + 2f * Mathf.PI * _c;
            int _first = _vertices.Count;
            for (int _i = 0; _i < RingSegments; _i++)
            {
                // the stadium's centreline: right side up, top half circle, left side down, bottom half circle
                float _s = _i * _perimeter / RingSegments;
                float _x, _z, _tx, _tz;
                if (_s < _a)
                {
                    _x = _c; _z = -_a / 2f + _s; _tx = 0f; _tz = 1f;
                }
                else if (_s < _a + Mathf.PI * _c)
                {
                    float _angle = (_s - _a) / _c;
                    _x = _c * Mathf.Cos(_angle); _z = _a / 2f + _c * Mathf.Sin(_angle); _tx = -Mathf.Sin(_angle); _tz = Mathf.Cos(_angle);
                }
                else if (_s < 2f * _a + Mathf.PI * _c)
                {
                    _x = -_c; _z = _a / 2f - (_s - _a - Mathf.PI * _c); _tx = 0f; _tz = -1f;
                }
                else
                {
                    float _angle = (_s - 2f * _a - Mathf.PI * _c) / _c + Mathf.PI;
                    _x = _c * Mathf.Cos(_angle); _z = -_a / 2f + _c * Mathf.Sin(_angle); _tx = -Mathf.Sin(_angle); _tz = Mathf.Cos(_angle);
                }
                Vector3 _onLine = _side * _x + _forward * _z;
                Vector3 _outward = (_side * _tz - _forward * _tx).normalized;
                for (int _j = 0; _j < TubeSegments; _j++)
                {
                    float _angle = _j * Mathf.PI * 2f / TubeSegments;
                    Vector3 _n = _outward * Mathf.Cos(_angle) + _plane * Mathf.Sin(_angle);
                    _vertices.Add(_link.Centre + _onLine + _n * _r);
                    _normals.Add(_n);
                }
            }
            for (int _i = 0; _i < RingSegments; _i++)
            {
                int _i1 = (_i + 1) % RingSegments;
                for (int _j = 0; _j < TubeSegments; _j++)
                {
                    int _j1 = (_j + 1) % TubeSegments;
                    int _q00 = _first + _i * TubeSegments + _j, _q10 = _first + _i1 * TubeSegments + _j;
                    int _q01 = _first + _i * TubeSegments + _j1, _q11 = _first + _i1 * TubeSegments + _j1;
                    _indices.Add(_q00); _indices.Add(_q01); _indices.Add(_q10);
                    _indices.Add(_q10); _indices.Add(_q01); _indices.Add(_q11);
                }
            }
        }
    }
}
