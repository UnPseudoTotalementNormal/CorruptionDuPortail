using System;
using FMOD.Studio;
using FMODUnity;
using GameLogic;
using Network;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;
using STOP_MODE = FMOD.Studio.STOP_MODE;

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
        
        [Header("Sounds")]
        public EventReference canalisationSound;
        public EventReference onUsedSound;
        
        [NonSerialized] public EventInstance canalisationSoundInstance;
        
        
        public bool IsTheSamePower(Power _isTheSamePower)
        {
            return powerName == _isTheSamePower.powerName;
        }

        public virtual bool CanUse(bool _ignoreCurrentlyUsed = false)
        {
            var _powerCharacter = GameManager.instance.GetCharacter(ownerClientId, false);
            if (_powerCharacter == null)
            {
                Debug.LogWarning("power character is null in power " + powerName + " of " + ownerClientId);
                return false;
            }
            
            if (isCurrentlyUsed && !_ignoreCurrentlyUsed)
            {
                return false;
            }

            if (_powerCharacter.isChained)
            {
                return false;
            }
            
            if (hasToBeAwakened && !_powerCharacter.role.isAwakened)
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
            if (!string.IsNullOrEmpty(canalisationSound.Path))
            {
                canalisationSoundInstance = RuntimeManager.CreateInstance(canalisationSound);
                canalisationSoundInstance.start();
            }
        }

        public virtual void OnUsed()
        {
            StopUse();
            powerUseLeft -= 1;
            GameManager.instance.DoPowerMethodRpc(ownerClientId, this, nameof(OnUsedServer), 
                new NetworkSerializableObject[] {}, new CustomRpcParams(CustomRpcParams.RpcTargetType.server));
            
            if (!string.IsNullOrEmpty(onUsedSound.Path))
            {
                RuntimeManager.PlayOneShot(onUsedSound);
            }
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
            if (canalisationSoundInstance.isValid())
            {
                canalisationSoundInstance.stop(STOP_MODE.ALLOWFADEOUT);
                canalisationSoundInstance.release();
            }
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
            
            string _eventPath = canalisationSound.Path ?? string.Empty;
            _serializer.SerializeValue(ref _eventPath);
            if (_serializer.IsReader && !string.IsNullOrEmpty(_eventPath))
            {
                canalisationSound = EventReference.Find(_eventPath);
            }
        }
        
        public virtual object Clone()
        {
            return MemberwiseClone();
        }
    }
}
