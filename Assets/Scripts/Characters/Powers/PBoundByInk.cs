using System.Collections.Generic;
using System;
using Characters.Powers.Target;
using ChatSystem;
using CorruptionDuPortail.Domain;
using GameLogic;
using GameLogic.GameStates;
using RoleTarget;
using UI.BoardUI.Selection;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

namespace Characters.Powers
{
    public class PBoundByInk : Power
    {
        private const int PRIMORDIAL_CHAT_ID_BEGIN = 515100;
        private static List<int> usedBoundByInkIds = new();
        
        private NetworkVariable<int> powerChatId = new(-1);
        
        private List<ulong> currentTargets = new();
        private NetworkList<ulong> alreadyTargetedClients = new();
        
        private bool isChatAttributed => powerChatId.Value != -1;

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
            targetValidator.AddRule(ctx => TargetUtils.IsTargetValid(ctx.targetId, targetIncludeFlags, ctx.targetType));
            targetValidator.AddRule(ctx => !alreadyTargetedClients.Contains(ctx.targetId));
        }
        
        [SerializeField] private string pickerDescription;

        public override void StartUse()
        {
            base.StartUse();
            SelectionFlowService.instance.StartCharacterSelection(targetValidator, OnCharacterPicked,
                new SelectionFlowOptions { stepDescriptions = new[] { pickerDescription } });
        }

        public override bool CanUse(bool _ignoreCurrentlyUsed = false)
        {
            // La vérification des cibles valides est déjà faite par base.CanUse()
            // via GetValidTargets() qui utilise notre targetValidator
            return base.CanUse(_ignoreCurrentlyUsed);
        }

        private void OnCharacterPicked(Character _character)
        {
            if (!CheckIsTargetValid(_character.ownerClientId.Value, TargetUtils.TargetType.Character))
            {
                return;
            }

            OnCardClickedRpc(_character.ownerClientId.Value);
            OnUsed();
        }

        private readonly PowerResolver _resolver = new();

        [Rpc(SendTo.Server)]
        private void OnCardClickedRpc(ulong _characterId)
        {
            if (alreadyTargetedClients.Contains(_characterId) || currentTargets.Contains(_characterId))
            {
                return;
            }

            // Story 4.2: decision-only resolution in Domain; the adapter dispatches the bricks.
            // RegisterInkTarget is power-LOCAL (NetworkList writes) → handled via ApplyLocalEffect.
            var _effects = _resolver.ResolveBoundByInkClick((int)ownerClientId.Value, (int)_characterId, powerChatId.Value);
            foreach (var _effect in _effects)
            {
                PowerEffectDispatcher.Dispatch(_effect, ApplyLocalEffect);
            }
        }

        private void ApplyLocalEffect(EffectDescriptor _effect)
        {
            switch (_effect)
            {
                case RegisterInkTarget _reg:
                    currentTargets.Add((ulong)_reg.TargetSlot);
                    alreadyTargetedClients.Add((ulong)_reg.TargetSlot);
                    break;
                default:
                    throw new NotSupportedException($"PBoundByInk: unexpected local brick {_effect}");
            }
        }

        public override void OnGameStartedServer()
        {
            base.OnGameStartedServer();
            AttributeBoundByInkChat();
            var _gameManager = GameManager.instance;
            foreach (var _awakeningState in _gameManager.GetGameStates(typeof(AwakeningState)))
            {
                _awakeningState.onStateStartServer += () =>
                {
                    foreach (var _targetClientId in currentTargets)
                    {
                        ChatManager.instance.UndiscoverChatRpc(powerChatId.Value, CharacterManager.instance.GetSafeRpcTarget(_targetClientId));
                    }
                    
                    currentTargets.Clear();
                };
            }
        }

        private void AttributeBoundByInkChat()
        {
            if (!IsServer)
            {
                Debug.LogError("BoundByInk should be called on server only.");
                return;
            }
            
            if (isChatAttributed)
            {
                Debug.LogError("BoundByInk chat already attributed.");
                return;
            }
            
            int _chatId = PRIMORDIAL_CHAT_ID_BEGIN;
            while (usedBoundByInkIds.Contains(_chatId))
            {
                _chatId++;
            }
            
            powerChatId.Value = _chatId;
            usedBoundByInkIds.Add(_chatId);
            ChatManager.instance.DiscoverChatRpc(_chatId, new FixedString64Bytes("Lié par l'encre"), CharacterManager.instance.GetSafeRpcTarget(ownerClientId.Value));
        }

        protected override void StopUse()
        {
            base.StopUse();
            SelectionFlowService.instance.CancelSelection();
        }
    }
}