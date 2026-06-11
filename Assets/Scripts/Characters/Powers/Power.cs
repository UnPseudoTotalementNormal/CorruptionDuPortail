#region

using System;
using System.Collections.Generic;
using System.Linq;
using AudioSystem;
using Characters.Powers.PowerComponents;
using Characters.Powers.Target;
using CorruptionDuPortail.Domain;
using Extensions;
using FMODUnity;
using FocusSystem;
using GameLogic;
using GameLogic.Validation;
using Network.Action;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.Assertions;
using static Characters.Powers.Target.TargetUtils;

#endregion

namespace Characters.Powers
{
    [Serializable]
    public class Power : NetworkBehaviour
    {
        public NetworkVariable<ulong> ownerClientId = new();

        public FixedString64Bytes powerName;
        public FixedString512Bytes powerDescription;
        public GameObject power3DObjectPrefab;
        public float maxWaitTime;

        public bool isPassive;
        public bool hasToBeAwakened = true;
        
        public TargetIncludeFlags targetIncludeFlags;
        public bool needTargetSelection => targetIncludeFlags != 0;
        
        public NetworkVariable<int> powerUseLeft = new();
        public int maxPowerUse = 1;
        [Tooltip("-1 == maxUse")] public int powerUseRegenPerAwakening = -1;
        
        /// <summary>
        /// A generic validator for target selection. Add your rules here in Awake/Start/OnNetworkSpawn().
        /// Example: targetValidator.AddRule(ctx => ctx.targetId != ownerClientId.Value);
        /// </summary>
        protected Validator<(ulong targetId, TargetType targetType)> targetValidator = new();

        // Story 7.1 lane C: CharacterManager resolved ONCE in OnNetworkSpawn (via the composition
        // root) and consumed by this base AND every concrete power, replacing the GameManager hub-hop.
        protected CharacterManager characterManager;
        // Story 7.3 lane C: GameInfoRevealer, same seam. Null-tolerant (no Assert) — not every power
        // uses it, and minimal harnesses spawn bare powers with no revealer; reveal-using powers
        // always have it in production (scene root) and in their own harnesses.
        protected GameInfoRevealer gameInfoRevealer;

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
        public event Action onStartUse;
        public event Action onStopUse;

        public List<PowerComponent> powerComponents = new();

        public Character ownerCharacter => characterManager.GetCharacter(ownerClientId.Value, false);
        
        [HideInInspector] public ulong idHolderServer;
        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
            characterManager = CompositionRoot.For(NetworkManager).CharacterManager;
            Assert.IsNotNull(characterManager,
                "Power.characterManager unresolved — CompositionRoot.For(NetworkManager) returned no CharacterManager. " +
                "Did a subclass override OnNetworkSpawn without calling base.OnNetworkSpawn()?");
            gameInfoRevealer = CompositionRoot.For(NetworkManager).GameInfoRevealer;
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
            List<ulong> _validTargets = characterManager.GetCharacters(false).Select(_c => _c.ownerClientId.Value).ToList();
            _validTargets = _validTargets.Where(_targetClientId => CheckIsTargetValid(_targetClientId, _targetType)).ToList();
            return _validTargets;
        }
        public bool CheckIsTargetValid(ulong _targetClientId, TargetType _targetType)
        {
            return targetValidator.Evaluate((_targetClientId, _targetType));
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
            PowerEffectTrace.Record(new PlayLoopingSound(canalisationSound.GetPath(), CANALISATION_SOUND_KEY));
            GameAudioManager.instance.PlayEventInstance(canalisationSound.GetPath(), CANALISATION_SOUND_KEY);
            onStartUse?.Invoke();
        }
        
        [Rpc(SendTo.Server)]
        public void OnUsedServerRpc()
        {
            OnUsed(false);
        }

        [Rpc(SendTo.SpecifiedInParams)]
        public void OnUsedClientRpc(RpcParams _params)
        {
            StopUse();
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
                    OnUsedServerRpc();
                }
                return;
            }
            
            OnUsedServer();
            onPowerUsed?.Invoke();
            if (ownerClientId.Value != NetworkManager.ServerClientId) //notify owner client
            {
                PowerEffectTrace.Record(new NotifyOwnerUsed((int)ownerClientId.Value));
                OnUsedClientRpc(characterManager.GetSafeRpcTarget(ownerClientId.Value));
            }
        }
        
        protected virtual void OnUsedServer()
        {
            PowerEffectTrace.Record(DecrementUses.Instance);
            powerUseLeft.Value -= 1;
            onPowerUsedServer?.Invoke();
            PowerEffectTrace.Record(RequestCharacterRefresh.Instance);
            characterManager.AskForUpdateAllCharactersRpc();
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
                    PowerEffectTrace.Record(new PlayOneShotSound(onUsedSound.GetPath()));
                    RuntimeManager.PlayOneShot(onUsedSound);
                }
                if (FocusManager.instance != null)
                {
                    PowerEffectTrace.Record(UnfocusAll.Instance);
                    FocusManager.instance.UnfocusAll();
                }
            }
            if (GameAudioManager.instance != null)
            {
                PowerEffectTrace.Record(new StopLoopingSound(CANALISATION_SOUND_KEY));
                GameAudioManager.instance.StopEventInstance(CANALISATION_SOUND_KEY);
            }
            isCurrentlyUsed = false;
            onStopUse?.Invoke();
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
            var _oldParentCharacter = characterManager.GetCharacter(_oldParentId, false);
            var _newParentCharacter = characterManager.GetCharacter(_newParentId, false);
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

