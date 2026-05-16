using System;
using Extensions;
using NUnit.Framework;

namespace Tests.Editor
{
    public class ArrayExtensionsTests
    {
        [Test]
        public void IndexOf_ItemExists_ReturnsCorrectIndex()
        {
            int[] array = new int[] { 10, 20, 30 };
            int index = array.IndexOf(20);
            Assert.AreEqual(1, index);
        }

        [Test]
        public void IndexOf_ItemDoesNotExist_ReturnsMinusOne()
        {
            int[] array = new int[] { 10, 20, 30 };
            int index = array.IndexOf(40);
            Assert.AreEqual(-1, index);
        }

        [Test]
        public void IndexOf_NullArray_ThrowsArgumentNullException()
        {
            int[] array = null;
            Assert.Throws<ArgumentNullException>(() => array.IndexOf(10));
        }

        [Test]
        public void CountUsed_WithElementsNotMatchingDefault_ReturnsCount()
        {
            int[] array = new int[] { 0, 10, 20, 0, 30 };
            int count = array.CountUsed(0);
            Assert.AreEqual(3, count); // 10, 20, 30 are not default (0)
        }

        [Test]
        public void CountUsed_AllElementsMatchDefault_ReturnsZero()
        {
            int[] array = new int[] { 0, 0, 0 };
            int count = array.CountUsed(0);
            Assert.AreEqual(0, count);
        }

        [Test]
        public void CountUsed_NullArray_ThrowsArgumentNullException()
        {
            int[] array = null;
            Assert.Throws<ArgumentNullException>(() => array.CountUsed(0));
        }
    }
}
