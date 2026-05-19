using System;
using Network;
using NUnit.Framework;

namespace Tests.Editor
{
    public class NetworkSerializableObjectTests
    {
        [Serializable]
        private class TestData
        {
            public int id;
            public string name;
        }

        [Test]
        public void SerializeAndDeserialize_ReturnsEqualObject()
        {
            TestData original = new TestData { id = 42, name = "TestObj" };
            NetworkSerializableObject nso = new NetworkSerializableObject(original);
            
            TestData deserialized = nso.Deserialize<TestData>();
            
            Assert.IsNotNull(deserialized);
            Assert.AreEqual(original.id, deserialized.id);
            Assert.AreEqual(original.name, deserialized.name);
        }

        [Test]
        public void DeserializeNonGeneric_ReturnsCorrectType()
        {
            TestData original = new TestData { id = 99, name = "Dynamic" };
            NetworkSerializableObject nso = new NetworkSerializableObject(original);
            
            object deserialized = nso.DeserializeNonGeneric(typeof(TestData));
            
            Assert.IsInstanceOf<TestData>(deserialized);
            Assert.AreEqual(99, ((TestData)deserialized).id);
        }
    }
}
