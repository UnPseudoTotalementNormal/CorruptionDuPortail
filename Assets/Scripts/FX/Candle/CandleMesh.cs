using System.Collections.Generic;
using UnityEngine;

namespace FX.Candle
{
    /// <summary>
    /// Builds the vote candle's meshes (board task T19): a pillar candle that melts, its brass dish, its wick and the
    /// quads of the flame and its halo. The candle is a surface of revolution with irregularities: a slightly uneven
    /// body, a rim melted unevenly, a pool of liquid wax at the top, drips running down the side that lengthen as it
    /// melts, and a puddle of wax spreading on the dish. Same seed = same candle. Local space: the dish's floor is
    /// y = 0, the candle stands on it, y up.
    /// </summary>
    public static class CandleMesh
    {
        /// <summary>The look of one candle.</summary>
        [System.Serializable]
        public struct Shape
        {
            [Tooltip("Radius of the candle body.")]
            public float radius;
            [Tooltip("Height of the body before it starts melting.")]
            public float fullHeight;
            [Tooltip("Height left when it has melted all the way.")]
            public float meltedHeight;
            [Tooltip("Number of wax drips running down the side.")]
            public int drips;
            [Tooltip("Same seed = same candle (drips, rim, puddle).")]
            public int seed;

            public static Shape Default => new Shape { radius = 0.6f, fullHeight = 2.95f, meltedHeight = 0.55f, drips = 7, seed = 7 };
        }

        /// <summary>How far the dish goes below its floor (y = 0): raise the candle by this much to stand it on a table.</summary>
        public const float DishDepth = 0.16f;

        private const int Segments = 72;
        private const float RingStep = 0.05f;

        /// <summary>Height of the body (without the rim) for a melt amount in [0, 1].</summary>
        public static float BodyHeight(Shape _shape, float _melt) =>
            Mathf.Lerp(_shape.fullHeight, _shape.meltedHeight, Mathf.Clamp01(_melt));

        /// <summary>Height of the melted pool at the top, where the wick stands.</summary>
        public static float PoolHeight(Shape _shape, float _melt) => BodyHeight(_shape, _melt) - 0.08f;

        private struct Drip
        {
            public float angle;
            public float width;
            public float amplitude;
            public float lengthFactor;
        }

        private struct Ring
        {
            public float[] radius;
            public float[] height;
        }

        /// <summary>(Re)builds the candle for a melt amount in [0, 1] (0 = new, 1 = burnt down).</summary>
        public static void BuildCandle(Mesh _mesh, Shape _shape, float _melt)
        {
            _melt = Mathf.Clamp01(_melt);
            System.Random _random = new System.Random(_shape.seed);
            float R = _shape.radius;
            float _h = BodyHeight(_shape, _melt);
            float _poolY = PoolHeight(_shape, _melt);

            Periodic _bodyNoise = new Periodic(_random, 6, 0.6f);
            Periodic _rimNoise = new Periodic(_random, 7, 1f);
            Periodic _puddleNoise = new Periodic(_random, 5, 1f);
            Drip[] _drips = new Drip[Mathf.Max(0, _shape.drips)];
            for (int _k = 0; _k < _drips.Length; _k++)
            {
                _drips[_k] = new Drip
                {
                    angle = (float)(_random.NextDouble() * Mathf.PI * 2.0),
                    width = Mathf.Lerp(0.09f, 0.17f, (float)_random.NextDouble()),
                    amplitude = Mathf.Lerp(0.035f, 0.065f, (float)_random.NextDouble()) * R / 0.6f,
                    lengthFactor = Mathf.Lerp(0.35f, 1.25f, (float)_random.NextDouble()),
                };
            }

            float[] _angles = new float[Segments];
            for (int _i = 0; _i < Segments; _i++)
            {
                _angles[_i] = _i * Mathf.PI * 2f / Segments;
            }

            var _rings = new List<Ring>();

            // Puddle of wax on the dish, spreading as the candle melts.
            float _puddleRadius = R * (1.12f + 0.6f * _melt);
            _rings.Add(MakeRing(_angles, _a => _puddleRadius * (1f + 0.13f * _puddleNoise.At(_a)), _a => 0.0f));
            _rings.Add(MakeRing(_angles, _a => _puddleRadius * (1f + 0.13f * _puddleNoise.At(_a)) - 0.03f, _a => 0.035f));
            _rings.Add(MakeRing(_angles, _a => Mathf.Lerp(_puddleRadius * (1f + 0.13f * _puddleNoise.At(_a)), R, 0.55f), _a => 0.05f + 0.02f * _melt));
            _rings.Add(MakeRing(_angles, _a => R * 1.05f, _a => 0.11f + 0.05f * _melt));

            // Body: a slightly uneven cylinder with drips running down from the rim.
            float _bodyStart = 0.17f + 0.05f * _melt;
            int _bodyRings = Mathf.Max(2, Mathf.CeilToInt((_h - _bodyStart) / RingStep) + 1);
            for (int _j = 0; _j < _bodyRings; _j++)
            {
                float _y = Mathf.Lerp(_bodyStart, _h, _j / (float)(_bodyRings - 1));
                _rings.Add(MakeRing(_angles,
                    _a => R * (1f + 0.012f * _bodyNoise.At(_a + _y * 0.9f)) + DripOffset(_drips, _a, _y, _h, _melt),
                    _a => _y));
            }

            // Rim: melted unevenly (some parts stand higher), lower where a drip ran over it.
            float Lip(float _a) => Mathf.Max(0.02f, 0.05f + 0.11f * Mathf.Max(0f, _rimNoise.At(_a)) - 0.05f * DripAcross(_drips, _a));
            _rings.Add(MakeRing(_angles, _a => R * 0.995f + 0.6f * DripOffset(_drips, _a, _h, _h, _melt), _a => _h + Lip(_a) * 0.7f));
            _rings.Add(MakeRing(_angles, _a => R * 0.955f, _a => _h + Lip(_a)));
            _rings.Add(MakeRing(_angles, _a => R * 0.9f, _a => _h + Lip(_a) * 0.55f));
            _rings.Add(MakeRing(_angles, _a => R * 0.85f, _a => _poolY + 0.015f));
            // The pool: flat liquid wax around the wick.
            _rings.Add(MakeRing(_angles, _a => R * 0.78f, _a => _poolY));
            _rings.Add(MakeRing(_angles, _a => R * 0.5f, _a => _poolY));
            _rings.Add(MakeRing(_angles, _a => R * 0.22f, _a => _poolY));

            Assemble(_mesh, _angles, _rings, new Vector3(0f, _poolY, 0f));
        }

        /// <summary>The brass dish the candle stands on (floor at y = 0, rim around).</summary>
        public static void BuildHolder(Mesh _mesh, float _scale = 1f)
        {
            // Profile (radius, height): from under the centre, out along the bottom, up the curved rim, back in over
            // the floor.
            Vector2[] _profile =
            {
                new Vector2(0.95f, -DishDepth), new Vector2(1.2f, -0.13f), new Vector2(1.38f, -0.04f),
                new Vector2(1.5f, 0.1f), new Vector2(1.57f, 0.19f), new Vector2(1.55f, 0.24f), new Vector2(1.47f, 0.22f),
                new Vector2(1.36f, 0.11f), new Vector2(1.18f, 0.025f), new Vector2(0.95f, 0f), new Vector2(0.5f, 0f),
            };
            float[] _angles = new float[Segments];
            for (int _i = 0; _i < Segments; _i++)
            {
                _angles[_i] = _i * Mathf.PI * 2f / Segments;
            }
            var _rings = new List<Ring> { MakeRing(_angles, _a => 0.3f * _scale, _a => -DishDepth * _scale) };
            foreach (Vector2 _p in _profile)
            {
                _rings.Add(MakeRing(_angles, _a => _p.x * _scale, _a => _p.y * _scale));
            }
            // Bottom first then around: close the underside with a fan at the centre, the top with one too.
            Assemble(_mesh, _angles, _rings, new Vector3(0f, 0f, 0f), new Vector3(0f, -DishDepth * _scale, 0f));
        }

        /// <summary>The wick: a thin bent strand standing in the pool, charred.</summary>
        public static void BuildWick(Mesh _mesh, float _height, float _lean)
        {
            const int Sides = 6, Steps = 6;
            const float Radius = 0.032f;
            var _vertices = new List<Vector3>();
            var _triangles = new List<int>();
            for (int _s = 0; _s <= Steps; _s++)
            {
                float _t = _s / (float)Steps;
                Vector3 _c = new Vector3(_lean * 0.14f * _t * _t, -0.05f + _height * _t, 0f);
                float _r = Radius * (1f - 0.35f * _t);
                for (int _k = 0; _k < Sides; _k++)
                {
                    float _a = _k * Mathf.PI * 2f / Sides;
                    _vertices.Add(_c + new Vector3(Mathf.Cos(_a) * _r, 0f, Mathf.Sin(_a) * _r));
                }
            }
            for (int _s = 0; _s < Steps; _s++)
            {
                for (int _k = 0; _k < Sides; _k++)
                {
                    int _a = _s * Sides + _k, _b = _s * Sides + (_k + 1) % Sides;
                    int _c = _a + Sides, _d = _b + Sides;
                    _triangles.AddRange(new[] { _a, _c, _b, _b, _c, _d });
                }
            }
            int _tip = _vertices.Count;
            _vertices.Add(new Vector3(_lean * 0.14f, -0.05f + _height + 0.02f, 0f));
            for (int _k = 0; _k < Sides; _k++)
            {
                _triangles.AddRange(new[] { Steps * Sides + _k, _tip, Steps * Sides + (_k + 1) % Sides });
            }
            _mesh.Clear();
            _mesh.SetVertices(_vertices);
            _mesh.SetTriangles(_triangles, 0);
            _mesh.RecalculateNormals();
            _mesh.RecalculateBounds();
        }

        /// <summary>A quad x in [-0.5, 0.5], y in [<paramref name="_bottom"/>, <paramref name="_bottom"/> + 1].</summary>
        public static void BuildQuad(Mesh _mesh, float _bottom)
        {
            _mesh.Clear();
            _mesh.SetVertices(new List<Vector3>
            {
                new Vector3(-0.5f, _bottom, 0f), new Vector3(0.5f, _bottom, 0f),
                new Vector3(-0.5f, _bottom + 1f, 0f), new Vector3(0.5f, _bottom + 1f, 0f),
            });
            _mesh.SetTriangles(new[] { 0, 2, 1, 1, 2, 3 }, 0);
            _mesh.RecalculateNormals();
            // The flame and its halo are turned to the camera in the shader: keep them from being culled early.
            _mesh.bounds = new Bounds(new Vector3(0f, _bottom + 0.5f, 0f), Vector3.one * 2f);
        }

        // ---- drips

        private static float Wrap(float _a)
        {
            _a = Mathf.Repeat(_a + Mathf.PI, Mathf.PI * 2f) - Mathf.PI;
            return _a;
        }

        private static float DripAcross(Drip[] _drips, float _angle)
        {
            float _max = 0f;
            foreach (Drip _d in _drips)
            {
                float _u = Wrap(_angle - _d.angle) / _d.width;
                _max = Mathf.Max(_max, Mathf.Exp(-_u * _u));
            }
            return _max;
        }

        private static float DripOffset(Drip[] _drips, float _angle, float _y, float _top, float _melt)
        {
            float _sum = 0f;
            foreach (Drip _d in _drips)
            {
                float _length = Mathf.Min(_top - 0.25f, (0.12f + 1.7f * _melt) * _d.lengthFactor);
                if (_length <= 0f)
                {
                    continue;
                }
                float _end = _top - _length;
                float _u = Wrap(_angle - _d.angle) / _d.width;
                float _across = Mathf.Exp(-_u * _u);
                float _ub = Wrap(_angle - _d.angle) / (_d.width * 1.45f);
                float _acrossBlob = Mathf.Exp(-_ub * _ub);
                float _along;
                if (_y >= _end)
                {
                    _along = 0.75f + 0.25f * (_y - _end) / _length;
                }
                else
                {
                    float _v = (_end - _y) / 0.05f;
                    _along = Mathf.Exp(-_v * _v);
                }
                float _bv = (_y - _end) / 0.09f;
                float _blob = Mathf.Exp(-_bv * _bv);
                _sum += _d.amplitude * (_across * _along + 0.9f * _acrossBlob * _blob);
            }
            return _sum;
        }

        // ---- rings → mesh

        private static Ring MakeRing(float[] _angles, System.Func<float, float> _radius, System.Func<float, float> _height)
        {
            var _ring = new Ring { radius = new float[_angles.Length], height = new float[_angles.Length] };
            for (int _i = 0; _i < _angles.Length; _i++)
            {
                _ring.radius[_i] = _radius(_angles[_i]);
                _ring.height[_i] = _height(_angles[_i]);
            }
            return _ring;
        }

        private static void Assemble(Mesh _mesh, float[] _angles, List<Ring> _rings, Vector3 _topCentre, Vector3? _bottomCentre = null)
        {
            int _n = _angles.Length;
            var _vertices = new List<Vector3>(_rings.Count * _n + 2);
            var _triangles = new List<int>(_rings.Count * _n * 6 + _n * 6);
            foreach (Ring _ring in _rings)
            {
                for (int _i = 0; _i < _n; _i++)
                {
                    _vertices.Add(new Vector3(Mathf.Cos(_angles[_i]) * _ring.radius[_i], _ring.height[_i],
                        Mathf.Sin(_angles[_i]) * _ring.radius[_i]));
                }
            }
            for (int _r = 0; _r < _rings.Count - 1; _r++)
            {
                for (int _i = 0; _i < _n; _i++)
                {
                    int _a = _r * _n + _i, _b = _r * _n + (_i + 1) % _n;
                    int _c = _a + _n, _d = _b + _n;
                    // Wound so the outside faces out when the rings go up then inward (Unity: clockwise = front).
                    _triangles.AddRange(new[] { _a, _c, _b, _b, _c, _d });
                }
            }
            int _top = _vertices.Count;
            _vertices.Add(_topCentre);
            int _last = (_rings.Count - 1) * _n;
            for (int _i = 0; _i < _n; _i++)
            {
                _triangles.AddRange(new[] { _last + _i, _top, _last + (_i + 1) % _n });
            }
            if (_bottomCentre.HasValue)
            {
                int _bottom = _vertices.Count;
                _vertices.Add(_bottomCentre.Value);
                for (int _i = 0; _i < _n; _i++)
                {
                    _triangles.AddRange(new[] { _i, (_i + 1) % _n, _bottom });
                }
            }
            _mesh.Clear();
            if (_vertices.Count > 65000)
            {
                _mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            }
            _mesh.SetVertices(_vertices);
            _mesh.SetTriangles(_triangles, 0);
            _mesh.RecalculateNormals();
            _mesh.RecalculateBounds();
        }

        /// <summary>A smooth periodic noise around the candle: a few sines with random phases, in [-1, 1].</summary>
        private readonly struct Periodic
        {
            private readonly float[] _phases;
            private readonly float[] _weights;

            public Periodic(System.Random _random, int _harmonics, float _falloff)
            {
                _phases = new float[_harmonics];
                _weights = new float[_harmonics];
                float _total = 0f;
                for (int _k = 0; _k < _harmonics; _k++)
                {
                    _phases[_k] = (float)(_random.NextDouble() * Mathf.PI * 2.0);
                    _weights[_k] = Mathf.Pow(1f / (_k + 1), _falloff) * Mathf.Lerp(0.6f, 1.2f, (float)_random.NextDouble());
                    _total += _weights[_k];
                }
                for (int _k = 0; _k < _harmonics; _k++)
                {
                    _weights[_k] /= _total;
                }
            }

            public float At(float _angle)
            {
                float _sum = 0f;
                for (int _k = 0; _k < _phases.Length; _k++)
                {
                    _sum += _weights[_k] * Mathf.Sin((_k + 2) * _angle + _phases[_k]);
                }
                return _sum;
            }
        }
    }
}
