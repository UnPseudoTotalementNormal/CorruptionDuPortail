using System;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

namespace Characters.Powers
{
    [Serializable]
    public abstract class Power : INetworkSerializable
    {
        public FixedString64Bytes powerName;
        public float maxWaitTime;
        
        
        public abstract bool CanUse();
        public abstract void Use();
        
        public virtual void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref maxWaitTime);
            serializer.SerializeValue(ref powerName);
        }
        
        public abstract Power CopyPower();
    }
}
