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

        [Test]
        public void AddLink_BuildsAClosedRingOfWireAroundItsCentre()
        {
            var _vertices = new List<Vector3>();
            var _normals = new List<Vector3>();
            var _indices = new List<int>();
            var _link = new ChainGeometry.LinkFrame(new Vector3(1f, 2f, 3f), Vector3.forward, Vector3.right);
            ChainGeometry.AddLink(_link, Size, _vertices, _normals, _indices);

            Assert.AreEqual(ChainGeometry.VerticesPerLink, _vertices.Count);
            Assert.AreEqual(ChainGeometry.VerticesPerLink, _normals.Count);
            Assert.AreEqual(ChainGeometry.IndicesPerLink, _indices.Count);
            foreach (int _i in _indices)
            {
                Assert.That(_i, Is.InRange(0, _vertices.Count - 1));
            }
            // the ring spans its length along the path and its width to the side, and is flat (wire thick)
            Vector3 _min = _vertices[0], _max = _vertices[0];
            foreach (Vector3 _v in _vertices)
            {
                _min = Vector3.Min(_min, _v);
                _max = Vector3.Max(_max, _v);
            }
            Vector3 _span = _max - _min;
            Assert.AreEqual(Size.StraightLength + 2f * Size.HalfWidth + 2f * Size.WireRadius, _span.z, 0.05f * _span.z);
            Assert.AreEqual(2f * Size.HalfWidth + 2f * Size.WireRadius, _span.x, 0.05f * _span.x);
            // the wire is a hexagon: across its flats it is 2r cos 30°
            Assert.AreEqual(2f * Size.WireRadius * Mathf.Cos(Mathf.PI / ChainGeometry.TubeSegments), _span.y, 1e-4f);
            Assert.AreEqual(_link.Centre.x, (_min.x + _max.x) / 2f, 1e-3f);
            Assert.AreEqual(_link.Centre.z, (_min.z + _max.z) / 2f, 1e-3f);
        }

        [Test]
        public void AddLink_NormalsPointAwayFromTheWire()
        {
            var _vertices = new List<Vector3>();
            var _normals = new List<Vector3>();
            var _indices = new List<int>();
            ChainGeometry.AddLink(new ChainGeometry.LinkFrame(Vector3.zero, Vector3.forward, Vector3.right), Size, _vertices, _normals, _indices);
            for (int _i = 0; _i < _normals.Count; _i++)
            {
                Assert.AreEqual(1f, _normals[_i].magnitude, 1e-4f);
            }
        }
    }
}
