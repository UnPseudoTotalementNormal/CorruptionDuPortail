#region

using System;
using System.Collections.Generic;
using AudioSystem;
using Characters.Powers.PowerComponents;
using Characters.Powers.Target;
using Extensions;
using FMODUnity;
using FocusSystem;
using GameLogic;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

#endregion

namespace Characters.Powers
{
    [Serializable]
    public class Power : NetworkBehaviour
    {
        public NetworkVariable<ulong> ownerClientId;

        public FixedString64Bytes powerName;
        public FixedString512Bytes powerDescription;
        public float maxWaitTime;

        public bool isPassive;
        public bool hasToBeAwakened = true;
        
        public TargetIncludeFlags targetIncludeFlags;
        public bool needTargetSelection => targetIncludeFlags != 0;
        
        public NetworkVariable<int> powerUseLeft;
        public int maxPowerUse = 1;
        [Tooltip("-1 == maxUse")] public int powerUseRegenPerAwakening = -1;
        
        [Header("Sounds")] 
        public EventReference canalisationSound;
        public EventReference onUsedSound;
        
        public const string CANALISATION_SOUND_KEY = "PowerCanalisationSound";
        [NonSerialized] public bool isCurrentlyUsed;

        public static event Action<Power> onPowerSpawned;
        public event Action onPowerUsedServer;
        public event Action onPowerUsed;
        public event Action onPowerReparented;

        public List<PowerComponent> powerComponents = new();
        
        public Character ownerCharacter => GameManager.instance.characterManager.GetCharacter(ownerClientId.Value, false);
        
        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
            onPowerSpawned?.Invoke(this);
        }
        
        public bool IsTheSamePower(Power _isTheSamePower)
        {
            return powerName == _isTheSamePower.powerName && powerDescription == _isTheSamePower.powerDescription;
        }
        public virtual bool CanUse(bool _ignoreCurrentlyUsed = false)
        {
            var _powerCharacter = ownerCharacter;
            if (!_powerCharacter)
            {
                Debug.LogWarning("power character is null in power " + powerName + " of " + ownerClientId);
                return false;
            }

            if (isPassive) return false;
            if (isCurrentlyUsed && !_ignoreCurrentlyUsed) return false;
            if (_powerCharacter.isChained.Value || _powerCharacter.isEliminated.Value) return false;
            if (hasToBeAwakened && !_powerCharacter.isAwakened.Value) return false;
            if (needTargetSelection && TargetUtils.GetTargetsForCharacters(targetIncludeFlags).Count <= 0) return false;
            if (powerUseLeft.Value <= 0) return false;

            return true;
        }

        public virtual void StartUse()
        {
            isCurrentlyUsed = true;
            GameAudioManager.instance.PlayEventInstance(canalisationSound.GetPath(), CANALISATION_SOUND_KEY);
        }

        public void OnUsed()
        {
            StopUse();
            OnUsedOwnerClientRpc(NetworkManager.RpcTarget.Single(ownerClientId.Value, RpcTargetUse.Persistent));
            OnUsedServerRpc();
            OnUsedRpc();
        }

        [Rpc(SendTo.Everyone)]
        protected virtual void OnUsedRpc()
        {
            onPowerUsed?.Invoke();
        }

        [Rpc(SendTo.SpecifiedInParams)]
        protected virtual void OnUsedOwnerClientRpc(RpcParams _params = default)
        {
            if (!string.IsNullOrEmpty(onUsedSound.GetPath())) RuntimeManager.PlayOneShot(onUsedSound);
            FocusManager.instance.UnfocusAll();
        }
        
        [Rpc(SendTo.Server)]
        protected virtual void OnUsedServerRpc()
        {
            powerUseLeft.Value -= 1;
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

        public void OnReparentedServer()
        {
            if (!IsServer)
            {
                return;
            }
            ulong _oldOwnerId = ownerClientId.Value;
            ownerClientId.Value = GetComponentInParent<Character>().ownerClientId.Value;
            OnReparentedClientRpc(_oldOwnerId, ownerClientId.Value);
        }

        [Rpc(SendTo.Everyone)]
        public virtual void OnReparentedClientRpc(ulong _oldParentId, ulong _newParentId)
        {
            var _oldParentCharacter = GameManager.instance.characterManager.GetCharacter(_oldParentId, false);
            var _newParentCharacter = GameManager.instance.characterManager.GetCharacter(_newParentId, false);
            if (_oldParentCharacter)
            {
                _oldParentCharacter.role.powers.Remove(this);
            }

            if (!_newParentCharacter.role.powers.Contains(this))
            {
                _newParentCharacter.role.powers.Add(this);
            }
            
            _oldParentCharacter.InvokeOnPowersUpdated();
            _newParentCharacter.InvokeOnPowersUpdated();
            
            onPowerReparented?.Invoke();
        }
    }
}