#region

using System;
using System.IO;
using System.Runtime.Serialization.Formatters.Binary;
using Unity.Netcode;

#endregion

namespace Network
{
    [Serializable]
    public struct NetworkSerializableObject : INetworkSerializable
    {
        private byte[] data;

        public NetworkSerializableObject(object obj)
        {
            using (MemoryStream ms = new MemoryStream())
            {
                BinaryFormatter formatter = new BinaryFormatter();
                formatter.Serialize(ms, obj);
                data = ms.ToArray();
            }
        }

        public T Deserialize<T>()
        {
            using (MemoryStream ms = new MemoryStream(data))
            {
                BinaryFormatter formatter = new BinaryFormatter();
                return (T)formatter.Deserialize(ms);
            }
        }

        // NEW: Non-generic method for dynamic deserialization
        public object DeserializeNonGeneric(Type type)
        {
            var method = typeof(NetworkSerializableObject).GetMethod("Deserialize").MakeGenericMethod(type);
            return method.Invoke(this, null);
        }

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref data);
        }
    }
}