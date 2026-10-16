using FX.Candle;
using NUnit.Framework;
using UnityEngine;

namespace Tests.Editor
{
    /// <summary>
    /// Board task T19: the vote candle melts during the vote. Its mesh must shrink with the melt, stay within its dish,
    /// keep a closed, valid surface, and be the same candle each time (same seed).
    /// </summary>
    public class CandleMeshTests
    {
        private Mesh _mesh;

        [SetUp]
        public void SetUp() => _mesh = new Mesh();

        [TearDown]
        public void TearDown() => Object.DestroyImmediate(_mesh);

        [Test]
        public void Candle_ShrinksAsItMelts()
        {
            CandleMesh.Shape _shape = CandleMesh.Shape.Default;
            CandleMesh.BuildCandle(_mesh, _shape, 0f);
            float _new = _mesh.bounds.max.y;
            CandleMesh.BuildCandle(_mesh, _shape, 0.5f);
            float _half = _mesh.bounds.max.y;
            CandleMesh.BuildCandle(_mesh, _shape, 1f);
            float _burnt = _mesh.bounds.max.y;

            Assert.That(_new, Is.GreaterThan(_half));
            Assert.That(_half, Is.GreaterThan(_burnt));
            Assert.That(_new, Is.InRange(_shape.fullHeight, _shape.fullHeight + 0.25f), "a new candle: full height plus its rim");
            Assert.That(_burnt, Is.InRange(_shape.meltedHeight, _shape.meltedHeight + 0.25f), "burnt down: the stub plus its rim");
        }

        [Test]
        public void Candle_StaysInsideItsDish()
        {
            CandleMesh.BuildCandle(_mesh, CandleMesh.Shape.Default, 1f);
            Vector3 _extents = _mesh.bounds.extents;
            Mesh _holder = new Mesh();
            CandleMesh.BuildHolder(_holder);
            Assert.That(_extents.x, Is.LessThan(_holder.bounds.extents.x), "the spilled wax stays on the dish");
            Assert.That(_mesh.bounds.min.y, Is.GreaterThanOrEqualTo(-0.001f), "the candle stands on the dish's floor");
            Assert.That(_holder.bounds.min.y, Is.EqualTo(-CandleMesh.DishDepth).Within(1e-4f),
                "the dish's foot is DishDepth below its floor: VoteCandle raises it by that much to stand on the table");
            Object.DestroyImmediate(_holder);
        }

        [Test]
        public void Candle_IsAValidSurfaceAtEveryMelt()
        {
            for (float _melt = 0f; _melt <= 1f; _melt += 0.125f)
            {
                CandleMesh.BuildCandle(_mesh, CandleMesh.Shape.Default, _melt);
                Vector3[] _vertices = _mesh.vertices;
                Vector3[] _normals = _mesh.normals;
                Assert.That(_mesh.triangles.Length % 3, Is.EqualTo(0));
                Assert.That(_normals.Length, Is.EqualTo(_vertices.Length));
                foreach (Vector3 _v in _vertices)
                {
                    Assert.That(float.IsNaN(_v.x) || float.IsNaN(_v.y) || float.IsNaN(_v.z), Is.False, $"NaN vertex at melt {_melt}");
                }
                // The side faces out: the normal of a body vertex points away from the axis.
                int _probe = _vertices.Length / 2;
                Vector3 _radial = new Vector3(_vertices[_probe].x, 0f, _vertices[_probe].z);
                Assert.That(Vector3.Dot(_normals[_probe], _radial), Is.GreaterThan(0f), $"inside-out body at melt {_melt}");
            }
        }

        [Test]
        public void Candle_SameSeedSameCandle()
        {
            CandleMesh.BuildCandle(_mesh, CandleMesh.Shape.Default, 0.4f);
            Vector3[] _first = _mesh.vertices;
            CandleMesh.BuildCandle(_mesh, CandleMesh.Shape.Default, 0.4f);
            Assert.That(_mesh.vertices, Is.EqualTo(_first));
        }
    }
}
