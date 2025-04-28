using System;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

namespace Characters.Powers
{
    [Serializable]
    public abstract class Power : INetworkSerializable, ICloneable
    {
        public FixedString64Bytes powerName;
        public float maxWaitTime;
        
        public bool isCurrentlyUsed = false;

        public virtual bool CanUse()
        {
            if (isCurrentlyUsed)
            {
                return false;
            }

            return true;
        }

        public virtual void Use()
        {
            isCurrentlyUsed = true;
        }

        public virtual void OnUsed()
        {
            isCurrentlyUsed = false;
        }

        public virtual void Cancel()
        {
            isCurrentlyUsed = false;
        }
        
        public virtual void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref maxWaitTime);
            serializer.SerializeValue(ref powerName);
        }
        
        public object Clone()
        {
            return MemberwiseClone();
        }
    }
}
