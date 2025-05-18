using System;
using GameLogic;
using Network;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

namespace Characters.Powers
{
    [Serializable]
    public class Power : INetworkSerializable, ICloneable
    {
        [HideInInspector] public ulong ownerClientId;
        
        public FixedString64Bytes powerName;
        public float maxWaitTime;
        
        public bool isPassive = false;
        public bool hasToBeAwakened = true;
        [NonSerialized] public bool isCurrentlyUsed = false;

        public int powerUseLeft;
        
        
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
            
            if (powerUseLeft <= 0)
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
            StopUse();
            powerUseLeft -= 1;
            GameManager.instance.DoPowerMethodRpc(ownerClientId, this, nameof(OnUsedServer), 
                new NetworkSerializableObject[] {}, new CustomRpcParams(CustomRpcParams.RpcTargetType.server));
        }
        
        public virtual void OnUsedServer()
        {
            powerUseLeft -= 1;
            GameManager.instance.AskForUpdateAllCharactersRpc();
        }

        public virtual void Cancel()
        {
            StopUse();
        }

        protected virtual void StopUse()
        {
            isCurrentlyUsed = false;
        }

        public virtual void UsingPowerUpdate() //note: please make it visuals only
        {
            
        }
        
        public virtual void PassivePowerUpdate()
        {
            
        }

        public virtual void OnGameStartedServer()
        {
            
        }
        
        public virtual void NetworkSerialize<T>(BufferSerializer<T> _serializer) where T : IReaderWriter
        {
            _serializer.SerializeValue(ref maxWaitTime);
            _serializer.SerializeValue(ref powerName);
            _serializer.SerializeValue(ref hasToBeAwakened);
            _serializer.SerializeValue(ref powerUseLeft);
        }
        
        public virtual object Clone()
        {
            return MemberwiseClone();
        }
    }
}
