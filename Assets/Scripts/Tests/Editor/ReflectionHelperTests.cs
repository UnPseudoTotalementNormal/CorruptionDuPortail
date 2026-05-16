using System;
using System.Linq;
using NUnit.Framework;

namespace Tests.Editor
{
    public class ReflectionHelperTests
    {
        private class BaseClass {}
        private class ChildClassA : BaseClass {}
        private class ChildClassB : BaseClass {}
        private class GrandChildClass : ChildClassA {}
        private class UnrelatedClass {}

        [Test]
        public void GetSubclassesOf_ReturnsDirectAndIndirectSubclasses()
        {
            var subclasses = ReflectionHelper.GetSubclassesOf(typeof(BaseClass)).ToList();

            Assert.Contains(typeof(ChildClassA), subclasses);
            Assert.Contains(typeof(ChildClassB), subclasses);
            Assert.Contains(typeof(GrandChildClass), subclasses);
        }

        [Test]
        public void GetSubclassesOf_DoesNotReturnBaseClassOrUnrelatedClasses()
        {
            var subclasses = ReflectionHelper.GetSubclassesOf(typeof(BaseClass)).ToList();

            Assert.IsFalse(subclasses.Contains(typeof(BaseClass)));
            Assert.IsFalse(subclasses.Contains(typeof(UnrelatedClass)));
        }

        [Test]
        public void GetSubclassesOf_ClassWithNoSubclasses_ReturnsEmpty()
        {
            var subclasses = ReflectionHelper.GetSubclassesOf(typeof(GrandChildClass)).ToList();

            Assert.IsEmpty(subclasses);
        }
    }
}
