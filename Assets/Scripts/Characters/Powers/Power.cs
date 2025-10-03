#region

using System;
using AudioSystem;
using Characters.Powers.Target;
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
    public class Power : NetworkBehaviour
    {
        [ReadOnly] public ulong ownerClientId;
        [ReadOnly] public ulong powerGameId;

        public FixedString64Bytes powerName;
        public FixedString512Bytes powerDescription;
        public float maxWaitTime;

        public bool isPassive;
        public bool hasToBeAwakened = true;
        
        public TargetIncludeFlags targetIncludeFlags;
        public bool needTargetSelection => targetIncludeFlags != 0;
        
        public int powerUseLeft;
        public int maxPowerUse = 1; 
        
        [Header("Sounds")] 
        public EventReference canalisationSound;
        public EventReference onUsedSound;
        
        public const string CANALISATION_SOUND_KEY = "PowerCanalisationSound";
        [NonSerialized] public bool isCurrentlyUsed;
        
        public event Action onPowerUsedServer;


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
            _serializer.SerializeValue(ref targetIncludeFlags);
            _serializer.SerializeValue(ref maxPowerUse);

            canalisationSound.NetworkSerialize(_serializer);
            onUsedSound.NetworkSerialize(_serializer);
        }


        public bool IsTheSamePower(Power _isTheSamePower)
        {
            return powerName == _isTheSamePower.powerName && powerDescription == _isTheSamePower.powerDescription;
        }

        public virtual bool CanUse(bool _ignoreCurrentlyUsed = false)
        {
            var _powerCharacter = GameManager.instance.characterManager.GetCharacter(ownerClientId, false);
            if (_powerCharacter == null)
            {
                Debug.LogWarning("power character is null in power " + powerName + " of " + ownerClientId);
                return false;
            }

            if (isCurrentlyUsed && !_ignoreCurrentlyUsed) return false;

            if (_powerCharacter.isChained.Value || _powerCharacter.isEliminated.Value) return false;

            if (hasToBeAwakened && !_powerCharacter.role.isAwakened) return false;
            
            if (needTargetSelection && TargetUtils.GetTargetsForCharacters(targetIncludeFlags).Count <= 0) return false;

            if (powerUseLeft <= 0) return false;

            return true;
        }

        public virtual void StartUse()
        {
            isCurrentlyUsed = true;
            GameAudioManager.instance.PlayEventInstance(canalisationSound.GetPath(), CANALISATION_SOUND_KEY);
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
            onPowerUsedServer?.Invoke();
            GameManager.instance.characterManager.AskForUpdateAllCharactersRpc();
        }

        public virtual void Cancel()
        {
            StopUse();
        }

        protected virtual void StopUse()
        {
            isCurrentlyUsed = false;
            GameAudioManager.instance.StopEventInstance(CANALISATION_SOUND_KEY);
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