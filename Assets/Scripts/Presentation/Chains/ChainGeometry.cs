using System;
using System.Collections.Generic;
using UnityEngine;

namespace Presentation.Chains
{
    /// <summary>
    /// Chain links as a mesh (maquettes chaines v1/v2): the artist's link (objets-alex v1 E1), laid one after another
    /// along a path, every other one turned a quarter turn about the path so they interlock. Sizes are shares of the
    /// cat's height, so a chain looks the same on any cat.
    /// </summary>
    public static class ChainGeometry
    {
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

        /// <summary>
        /// The artist's link, read once from its mesh: centred, longest side = the link's length, shortest = its thickness
        /// (one wire), so it can be laid along any path whatever the axes of the file.
        /// </summary>
        public sealed class LinkTemplate
        {
            private readonly Vector3[] _vertices;
            private readonly Vector3[] _normals;
            private readonly Vector2[] _uvs;
            private readonly int[] _indices;
            private readonly int _lengthAxis, _widthAxis, _thicknessAxis;
            private readonly float _handedness;
            private readonly float _innerLength;

            public int IndexCount => _indices.Length;
            public int VertexCount => _vertices.Length;

            public LinkTemplate(Mesh _mesh)
            {
                _vertices = _mesh.vertices;
                _normals = _mesh.normals;
                _uvs = _mesh.uv;
                _indices = _mesh.triangles;
                Bounds _bounds = _mesh.bounds;
                for (int _v = 0; _v < _vertices.Length; _v++)
                {
                    _vertices[_v] -= _bounds.center;
                }
                Vector3 _size = _bounds.size;
                _lengthAxis = _size.x >= _size.y && _size.x >= _size.z ? 0 : _size.y >= _size.z ? 1 : 2;
                _thicknessAxis = _size.x <= _size.y && _size.x <= _size.z ? 0 : _size.y <= _size.z ? 1 : 2;
                _widthAxis = 3 - _lengthAxis - _thicknessAxis;
                // An odd swap of the axes would turn the link inside out: its thickness then goes the other way.
                _handedness = (_widthAxis - _lengthAxis + 3) % 3 == 1 ? 1f : -1f;
                _innerLength = _size[_lengthAxis] - 2f * _size[_thicknessAxis];
            }

            /// <summary>Appends one link: its inner length made equal to the pitch, so that the links hook into each other.</summary>
            public void Add(LinkFrame _link, LinkSize _size, List<Vector3> _outVertices, List<Vector3> _outNormals, List<Vector2> _outUvs, List<int> _outIndices)
            {
                float _scale = _size.Pitch / _innerLength;
                Vector3 _forward = _link.Forward;
                Vector3 _side = _link.Side;
                Vector3 _thickness = Vector3.Cross(_forward, _side).normalized * _handedness;
                int _first = _outVertices.Count;
                for (int _v = 0; _v < _vertices.Length; _v++)
                {
                    Vector3 _p = _vertices[_v], _n = _normals[_v];
                    _outVertices.Add(_link.Centre + (_forward * _p[_lengthAxis] + _side * _p[_widthAxis] + _thickness * _p[_thicknessAxis]) * _scale);
                    _outNormals.Add(_forward * _n[_lengthAxis] + _side * _n[_widthAxis] + _thickness * _n[_thicknessAxis]);
                    _outUvs.Add(_uvs[_v]);
                }
                foreach (int _i in _indices)
                {
                    _outIndices.Add(_first + _i);
                }
            }
        }
    }
}
