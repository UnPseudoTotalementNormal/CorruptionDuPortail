using FX.Candle;
using NUnit.Framework;
using UnityEngine;

namespace Tests.Editor
{
    public class CandleMeshTests
    {
        private const float Foot = 1f;
        private static readonly Vector3[] Wax = { new Vector3(0.2f, 1f, 0.3f), new Vector3(-0.1f, 1.5f, 0f), new Vector3(0f, 2f, -0.2f) };

        private static Vector3[] Melted(float _stretch, float _drop)
        {
            var _target = new Vector3[Wax.Length];
            CandleMesh.MeltWax(Wax, _target, Foot, _stretch, _drop);
            return _target;
        }

        [Test]
        public void MeltWax_New_IsDrawnOutFromItsFoot()
        {
            Vector3[] _new = Melted(2f, 0f);
            Assert.AreEqual(1f, _new[0].y, 1e-5f, "the foot stays in the cup");
            Assert.AreEqual(2f, _new[1].y, 1e-5f);
            Assert.AreEqual(3f, _new[2].y, 1e-5f);
        }

        [Test]
        public void MeltWax_Melting_SinksAndKeepsTheTopsShape()
        {
            Vector3[] _melted = Melted(2f, 0.75f);
            Assert.AreEqual(1f, _melted[0].y, 1e-5f, "the foot does not go through the holder");
            Assert.AreEqual(1.25f, _melted[1].y, 1e-5f);
            Assert.AreEqual(2.25f, _melted[2].y, 1e-5f);
            Assert.AreEqual(1f, _melted[2].y - _melted[1].y, 1e-5f, "above the cup the wax sinks as it is");
        }

        [Test]
        public void MeltWax_KeepsEveryThingButTheHeight()
        {
            Vector3[] _melted = Melted(1.7f, 0.5f);
            for (int _i = 0; _i < Wax.Length; _i++)
            {
                Assert.AreEqual(Wax[_i].x, _melted[_i].x);
                Assert.AreEqual(Wax[_i].z, _melted[_i].z);
            }
        }

        [Test]
        public void Quad_SpansOneUnitFromItsBottom()
        {
            var _mesh = new Mesh();
            CandleMesh.BuildQuad(_mesh, -0.5f);
            Assert.AreEqual(4, _mesh.vertexCount);
            Assert.AreEqual(-0.5f, _mesh.vertices[0].y, 1e-5f);
            Assert.AreEqual(0.5f, _mesh.vertices[3].y, 1e-5f);
            Object.DestroyImmediate(_mesh);
        }
    }
}
