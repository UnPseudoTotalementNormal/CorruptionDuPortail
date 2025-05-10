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
        
        public bool hasToBeAwakened = true;
        [NonSerialized] public bool isCurrentlyUsed = false;
        
        public bool IsTheSamePower(Power _isTheSamePower)
        {
            return powerName == _isTheSamePower.powerName;
        }

        public virtual bool CanUse(bool _ignoreCurrentlyUsed = false)
        {
            if (isCurrentlyUsed && !_ignoreCurrentlyUsed)
            {
                return false;
            }
            
            if (hasToBeAwakened && !GameManager.instance.GetLocalCharacter(false).role.isAwakened)
            {
                return false;
            }

            return true;
        }

        public virtual void StartUse()
        {
            isCurrentlyUsed = true;
        }

        public virtual void OnUsed()
        {
            isCurrentlyUsed = false;
            StopUse();
        }

        public virtual void Cancel()
        {
            isCurrentlyUsed = false;
            StopUse();
        }

        protected virtual void StopUse()
        {
            
        }

        public virtual void UsingPowerUpdate() //note: please make it visuals only
        {
            
        }
        
        public virtual void NetworkSerialize<T>(BufferSerializer<T> _serializer) where T : IReaderWriter
        {
            _serializer.SerializeValue(ref maxWaitTime);
            _serializer.SerializeValue(ref powerName);
            _serializer.SerializeValue(ref hasToBeAwakened);
        }
        
        public object Clone()
        {
            return MemberwiseClone();
        }
    }
}
