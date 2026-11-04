using CorruptionDuPortail.Domain;
using NUnit.Framework;
using Presentation;
using UnityEngine;

namespace Tests.Editor
{
    public class DaySkipTableTests
    {
        [Test]
        public void TableHeight_VotedRender()
        {
            Assert.AreEqual(3.6f, DaySkipTableLayout.TableHeight(11.3f, 37.1f), 0.001f);
        }

        [Test]
        public void TableHeight_StaysAtTheSamePlaceOnScreen()
        {
            // A closer circle lifts the table, a deeper floor lengthens its legs: same angle under the eye both ways.
            float _voted = (11.3f - DaySkipTableLayout.TableHeight(11.3f, 37.1f)) / 37.1f;
            Assert.AreEqual(_voted, (11.3f - DaySkipTableLayout.TableHeight(11.3f, 20f)) / 20f, 0.001f);
            Assert.AreEqual(_voted, (21.3f - DaySkipTableLayout.TableHeight(21.3f, 37.1f)) / 37.1f, 0.001f);
        }

        [Test]
        public void TableHeight_NeverUnderTheTurnedShape()
        {
            Assert.AreEqual(DaySkipTableLayout.MinTableHeight, DaySkipTableLayout.TableHeight(1f, 37.1f));
        }

        [Test]
        public void RecapText_JustAboveTheEye()
        {
            Assert.AreEqual(12.06f, DaySkipTableLayout.RecapTextHeight(11.3f, 37.1f), 0.001f);
        }

        [TestCase(3.6f)]
        [TestCase(13.6f)]
        public void TableMesh_ReachesItsHeight(float _height)
        {
            Mesh _mesh = DaySkipTable.TableMesh(_height);
            Assert.AreEqual(_height, _mesh.bounds.max.y, 0.001f);
            Assert.AreEqual(0f, _mesh.bounds.min.y, 0.001f);
            Assert.AreEqual(2, _mesh.subMeshCount);
            Object.DestroyImmediate(_mesh);
        }

        [Test]
        public void ButtonMesh_CarvingOverThePuck()
        {
            Mesh _mesh = DaySkipTable.ButtonMesh();
            Assert.AreEqual(3, _mesh.subMeshCount);
            Assert.AreEqual(0.83f, _mesh.bounds.max.y, 0.001f);
            Object.DestroyImmediate(_mesh);
        }
    }
}
