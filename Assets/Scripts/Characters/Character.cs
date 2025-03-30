using System;
using Unity.Netcode;

namespace Characters
{
    [Serializable]
    public class Character : INetworkSerializable
    {
        public Role role;
        public ulong ownerClientId;
        
        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref ownerClientId);
            //serializer.SerializeValue(ref role);
        }
    }
}