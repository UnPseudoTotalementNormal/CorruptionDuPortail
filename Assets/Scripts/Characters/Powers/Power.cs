#region

using System;
using Extensions;
using FMOD.Studio;
using FMODUnity;
using GameLogic;
using Network;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;
using STOP_MODE = FMOD.Studio.STOP_MODE;

#endregion

namespace Characters.Powers
{
    [Serializable]
    public class Power : INetworkSerializable, ICloneable
    {
        [ReadOnly] public ulong ownerClientId;
        [ReadOnly] public ulong powerGameId;

        public FixedString64Bytes powerName;
        public FixedString512Bytes powerDescription;
        public float maxWaitTime;

        public bool isPassive;
        public bool hasToBeAwakened = true;

        public int powerUseLeft;

        [Header("Sounds")] 
        public EventReference canalisationSound;

        public EventReference onUsedSound;

        [NonSerialized] public EventInstance canalisationSoundInstance;
        [NonSerialized] public bool isCurrentlyUsed;

        public virtual object Clone()
        {
            return MemberwiseClone();
        }

        public virtual void NetworkSerialize<T>(BufferSerializer<T> _serializer) where T : IReaderWriter
        {
            _serializer.SerializeValue(ref ownerClientId);
            _serializer.SerializeValue(ref maxWaitTime);
            _serializer.SerializeValue(ref powerName);
            _serializer.SerializeValue(ref powerDescription);
            _serializer.SerializeValue(ref isPassive);
            _serializer.SerializeValue(ref hasToBeAwakened);
            _serializer.SerializeValue(ref powerUseLeft);
            _serializer.SerializeValue(ref powerGameId);

            var _eventPath = canalisationSound.GetPath() ?? string.Empty;
            _serializer.SerializeValue(ref _eventPath);
            if (_serializer.IsReader && !string.IsNullOrEmpty(_eventPath))
                canalisationSound = RuntimeManager.PathToEventReference(_eventPath);
            
            _eventPath = onUsedSound.GetPath() ?? string.Empty;
            _serializer.SerializeValue(ref _eventPath);
            if (_serializer.IsReader && !string.IsNullOrEmpty(_eventPath))
                onUsedSound = RuntimeManager.PathToEventReference(_eventPath);
        }


        public bool IsTheSamePower(Power _isTheSamePower)
        {
            return powerName == _isTheSamePower.powerName && powerDescription == _isTheSamePower.powerDescription;
        }

        public virtual bool CanUse(bool _ignoreCurrentlyUsed = false)
        {
            var _powerCharacter = GameManager.instance.GetCharacter(ownerClientId, false);
            if (_powerCharacter == null)
            {
                Debug.LogWarning("power character is null in power " + powerName + " of " + ownerClientId);
                return false;
            }

            if (isCurrentlyUsed && !_ignoreCurrentlyUsed) return false;

            if (_powerCharacter.isChained || _powerCharacter.isEliminated) return false;

            if (hasToBeAwakened && !_powerCharacter.role.isAwakened) return false;

            if (powerUseLeft <= 0) return false;

            return true;
        }

        public virtual void StartUse()
        {
            isCurrentlyUsed = true;
            if (!string.IsNullOrEmpty(canalisationSound.GetPath()))
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
                new NetworkSerializableObject[] { }, new CustomRpcParams(CustomRpcParams.RpcTargetType.server));

            if (!string.IsNullOrEmpty(onUsedSound.GetPath())) RuntimeManager.PlayOneShot(onUsedSound);
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
    }
}