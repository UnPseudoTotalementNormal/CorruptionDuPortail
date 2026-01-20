#region

using System;
using System.Collections.Generic;
using System.Linq;
using AudioSystem;
using Characters.Powers.PowerComponents;
using Characters.Powers.Target;
using Extensions;
using FMODUnity;
using FocusSystem;
using GameLogic;
using Network.Action;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;
using static Characters.Powers.Target.TargetUtils;

#endregion

namespace Characters.Powers
{
    [Serializable]
    public class Power : NetworkBehaviour
    {
        public NetworkVariable<ulong> ownerClientId;

        public FixedString64Bytes powerName;
        public FixedString512Bytes powerDescription;
        public GameObject power3DObjectPrefab;
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
        public NetworkAction onPowerUsed;
        public event Action onPowerReparented;
        public event Action onPowerGameStartedServerTriggered;
        
        public delegate void CheckIsTargetValidDelegate(ulong _targetClientId, TargetType _targetType, ref bool _isValid);
        public event CheckIsTargetValidDelegate checkIsTargetValid;

        public List<PowerComponent> powerComponents = new();

        public Character ownerCharacter => GameManager.instance.characterManager.GetCharacter(ownerClientId.Value, false);
        
        [HideInInspector] public ulong idHolderServer;
        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
            if (IsServer)
            {
                ownerClientId.Value = idHolderServer;
                onPowerUsed = new NetworkAction("OnPowerUsed_" + powerName, this);
            }
            onPowerSpawned?.Invoke(this);
        }
        
        public bool IsTheSamePower(Power _isTheSamePower)
        {
            return powerName == _isTheSamePower.powerName;
        }
        
        public List<ulong> GetValidTargets(TargetType _targetType = TargetType.Character)
        {
            List<ulong> _validTargets = CharacterManager.instance.GetCharacters(false).Select(_c => _c.ownerClientId.Value).ToList();
            _validTargets = _validTargets.Where(_targetClientId => CheckIsTargetValid(_targetClientId, _targetType)).ToList();
            return _validTargets;
        }

        public bool CheckIsTargetValid(ulong _targetClientId, TargetType _targetType)
        {
            bool _isValid;
            _isValid = IsTargetValid(_targetClientId, targetIncludeFlags, _targetType);
            if (_isValid)
            {
                checkIsTargetValid?.Invoke(_targetClientId, _targetType, ref _isValid);
            }
            return _isValid;
        }
        
        public virtual bool CanUse(bool _ignoreCurrentlyUsed = false)
        {
            var _powerCharacter = ownerCharacter;
            if (!_powerCharacter)
            {
                Debug.LogWarning("power character is null in power " + powerName + " of " + ownerClientId);
                return false;
            }

            if (powerComponents.Any(_pc => !_pc.CanUsePower())) return false;
            if (isPassive) return false;
            if (isCurrentlyUsed && !_ignoreCurrentlyUsed) return false;
            if (_powerCharacter.isChained.Value || _powerCharacter.isEliminated.Value) return false;
            if (hasToBeAwakened && !_powerCharacter.isAwakened.Value) return false;
            if (needTargetSelection && GetValidTargets().Count <= 0) return false;
            if (powerUseLeft.Value <= 0) return false;

            return true;
        }

        public virtual void StartUse()
        {
            isCurrentlyUsed = true;
            GameAudioManager.instance.PlayEventInstance(canalisationSound.GetPath(), CANALISATION_SOUND_KEY);
        }
        
        [Rpc(SendTo.SpecifiedInParams)]
        public void OnUsedRpc(RpcParams _params)
        {
            OnUsed(false);
        }

        /// <summary>
        /// Triggers the use of the power. Calls the server if necessary, then executes server-side logic and notifies the owner client.
        /// </summary>
        /// <param name="_callToServer">IMPORTANT: If false, the method must be called directly on the server. if true, sends an RPC to the server to process the usage.</param>
        
        public void OnUsed(bool _callToServer = true) //NEEDS TO BE CALLED ON SERVER IF BOOL IS FALSE, it will call on owner client too after
        {
            StopUse();

            if (!IsServer)
            {
                if (_callToServer)
                {
                    OnUsedRpc(RpcTarget.Server);
                }
                return;
            }
            
            OnUsedServer();
            onPowerUsed?.Invoke();
            if (ownerClientId.Value != NetworkManager.ServerClientId) //notify owner client
            {
                OnUsedRpc(RpcTarget.Single(ownerClientId.Value, RpcTargetUse.Persistent));
            }
        }
        
        protected virtual void OnUsedServer()
        {
            powerUseLeft.Value -= 1;
            onPowerUsedServer?.Invoke();
            GameManager.instance.characterManager.AskForUpdateAllCharactersRpc();
        }

        public virtual void Cancel()
        {
            if (!isCurrentlyUsed && !isPassive)
            {
                return;
            }
            StopUse();
        }

        protected virtual void StopUse()
        {
            if (isCurrentlyUsed) //only play if currently being used (avoid double sound effects when stopped by client and then by server)
            {
                if (!string.IsNullOrEmpty(onUsedSound.GetPath()))
                {
                    RuntimeManager.PlayOneShot(onUsedSound);
                }
                FocusManager.instance.UnfocusAll();
            }
            GameAudioManager.instance.StopEventInstance(CANALISATION_SOUND_KEY);
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
            onPowerGameStartedServerTriggered?.Invoke();
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