using System.Collections.Generic;
using NUnit.Framework;
using Presentation.Chains;
using UnityEngine;

namespace Tests.Editor
{
    public class ChainGeometryTests
    {
        private static readonly ChainGeometry.LinkSize Size = ChainGeometry.LinkSize.ForCatHeight(10f);

        private static List<Vector3> Line(float _length) => new() { Vector3.zero, Vector3.right * _length };

        [Test]
        public void Pitch_IsTheLinksInnerLength()
        {
            Assert.AreEqual(Size.StraightLength + 2f * Size.HalfWidth - 2f * Size.WireRadius, Size.Pitch, 1e-5f);
            Assert.Greater(Size.Pitch, 0f);
        }

        [Test]
        public void LayAlong_PutsOneLinkPerPitch()
        {
            var _links = new List<ChainGeometry.LinkFrame>();
            int _count = ChainGeometry.LayAlong(Line(10.5f * Size.Pitch), _ => Vector3.up, Size, _links);

            Assert.AreEqual(10, _count);
            Assert.AreEqual(10, _links.Count);
            Assert.AreEqual(0.5f * Size.Pitch, _links[0].Centre.x, 1e-4f);
            Assert.AreEqual(Size.Pitch, _links[1].Centre.x - _links[0].Centre.x, 1e-4f);
        }

        [Test]
        public void LayAlong_TurnsEveryOtherLinkAQuarterTurn()
        {
            var _links = new List<ChainGeometry.LinkFrame>();
            ChainGeometry.LayAlong(Line(4f * Size.Pitch), _ => Vector3.up, Size, _links);

            Assert.AreEqual(0f, Vector3.Dot(_links[0].Side, _links[1].Side), 1e-4f);
            Assert.AreEqual(1f, Mathf.Abs(Vector3.Dot(_links[0].Side, _links[2].Side)), 1e-4f);
            foreach (ChainGeometry.LinkFrame _link in _links)
            {
                Assert.AreEqual(0f, Vector3.Dot(_link.Side, _link.Forward), 1e-4f);
            }
        }

        [Test]
        public void LayAlong_TooShortOrEmpty_LaysNothing()
        {
            var _links = new List<ChainGeometry.LinkFrame>();
            Assert.AreEqual(0, ChainGeometry.LayAlong(Line(0.5f * Size.Pitch), _ => Vector3.up, Size, _links));
            Assert.AreEqual(0, ChainGeometry.LayAlong(new List<Vector3> { Vector3.zero }, _ => Vector3.up, Size, _links));
            Assert.AreEqual(0, _links.Count);
        }

        // A flat box like the artist's link: 3 long along y, 1 wide along x, 0.2 thick along z, off centre.
        private static Mesh LinkMesh()
        {
            var _mesh = new Mesh();
            var _vertices = new List<Vector3>();
            for (int _i = 0; _i < 8; _i++)
            {
                _vertices.Add(new Vector3(5f + ((_i & 1) == 0 ? -0.5f : 0.5f), (_i & 2) == 0 ? -1.5f : 1.5f, (_i & 4) == 0 ? -0.1f : 0.1f));
            }
            _mesh.SetVertices(_vertices);
            _mesh.SetUVs(0, new List<Vector2>(new Vector2[8]));
            _mesh.SetTriangles(new[] { 0, 2, 1, 1, 2, 3, 4, 5, 6, 5, 7, 6 }, 0);
            _mesh.RecalculateNormals();
            _mesh.RecalculateBounds();
            return _mesh;
        }

        [Test]
        public void LinkTemplate_LaysTheLinkAlongThePathAtThePitch()
        {
            Mesh _mesh = LinkMesh();
            var _template = new ChainGeometry.LinkTemplate(_mesh);
            var _vertices = new List<Vector3>();
            var _normals = new List<Vector3>();
            var _uvs = new List<Vector2>();
            var _indices = new List<int>();
            var _link = new ChainGeometry.LinkFrame(new Vector3(1f, 2f, 3f), Vector3.forward, Vector3.right);
            _template.Add(_link, Size, _vertices, _normals, _uvs, _indices);

            Assert.AreEqual(_template.VertexCount, _vertices.Count);
            Assert.AreEqual(_template.VertexCount, _uvs.Count);
            Assert.AreEqual(_template.IndexCount, _indices.Count);
            Vector3 _min = _vertices[0], _max = _vertices[0];
            foreach (Vector3 _v in _vertices)
            {
                _min = Vector3.Min(_min, _v);
                _max = Vector3.Max(_max, _v);
            }
            float _scale = Size.Pitch / (3f - 2f * 0.2f);
            Assert.AreEqual(3f * _scale, _max.z - _min.z, 1e-4f, "its length runs along the path");
            Assert.AreEqual(1f * _scale, _max.x - _min.x, 1e-4f, "its width runs along the side");
            Assert.AreEqual(0.2f * _scale, _max.y - _min.y, 1e-4f);
            Assert.AreEqual(_link.Centre.x, (_min.x + _max.x) / 2f, 1e-4f, "centred on the frame, wherever it was in the file");
            Assert.AreEqual(_link.Centre.z, (_min.z + _max.z) / 2f, 1e-4f);
            Object.DestroyImmediate(_mesh);
        }

        [Test]
        public void LinkTemplate_KeepsTheFacesOutward()
        {
            Mesh _mesh = LinkMesh();
            var _template = new ChainGeometry.LinkTemplate(_mesh);
            var _vertices = new List<Vector3>();
            var _normals = new List<Vector3>();
            var _indices = new List<int>();
            _template.Add(new ChainGeometry.LinkFrame(Vector3.zero, Vector3.forward, Vector3.right), Size, _vertices, _normals, new List<Vector2>(), _indices);
            for (int _t = 0; _t < _indices.Count; _t += 3)
            {
                Vector3 _a = _vertices[_indices[_t]], _b = _vertices[_indices[_t + 1]], _c = _vertices[_indices[_t + 2]];
                Vector3 _face = Vector3.Cross(_b - _a, _c - _a);
                Assert.Greater(Vector3.Dot(_face, _normals[_indices[_t]]), 0f, "the winding agrees with the normals");
            }
            Object.DestroyImmediate(_mesh);
        }
    }
}
