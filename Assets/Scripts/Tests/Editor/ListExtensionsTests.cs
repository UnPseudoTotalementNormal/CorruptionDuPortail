using System;
using System.Collections.Generic;
using Extensions;
using NUnit.Framework;

namespace Tests.Editor
{
    public class ListExtensionsTests
    {
        [Test]
        public void ChangeIndex_ValidIndices_MovesItemCorrectly()
        {
            List<int> list = new List<int> { 10, 20, 30, 40 };
            list.ChangeIndex(1, 3); // Move 20 to the end

            Assert.AreEqual(10, list[0]);
            Assert.AreEqual(30, list[1]);
            Assert.AreEqual(40, list[2]);
            Assert.AreEqual(20, list[3]);
        }

        [Test]
        public void ChangeIndex_ToSameIndex_ListRemainsUnchanged()
        {
            List<int> list = new List<int> { 10, 20, 30 };
            list.ChangeIndex(1, 1);

            Assert.AreEqual(10, list[0]);
            Assert.AreEqual(20, list[1]);
            Assert.AreEqual(30, list[2]);
        }

        [Test]
        public void ChangeIndex_NegativeOldIndex_ThrowsArgumentOutOfRangeException()
        {
            List<int> list = new List<int> { 10, 20 };
            Assert.Throws<ArgumentOutOfRangeException>(() => list.ChangeIndex(-1, 1));
        }

        [Test]
        public void ChangeIndex_OldIndexGreaterThanCount_ThrowsArgumentOutOfRangeException()
        {
            List<int> list = new List<int> { 10, 20 };
            Assert.Throws<ArgumentOutOfRangeException>(() => list.ChangeIndex(2, 0));
        }

        [Test]
        public void ChangeIndex_NegativeNewIndex_ThrowsArgumentOutOfRangeException()
        {
            List<int> list = new List<int> { 10, 20 };
            Assert.Throws<ArgumentOutOfRangeException>(() => list.ChangeIndex(0, -1));
        }

        [Test]
        public void ChangeIndex_NewIndexGreaterThanCount_ThrowsArgumentOutOfRangeException()
        {
            List<int> list = new List<int> { 10, 20 };
            Assert.Throws<ArgumentOutOfRangeException>(() => list.ChangeIndex(0, 2));
        }
    }
}
