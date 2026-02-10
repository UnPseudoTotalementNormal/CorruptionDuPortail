using System.Collections.Generic;
using System.Linq;
using Characters.Powers.Target;
using ChatSystem;
using FocusSystem;
using GameLogic;
using GameLogic.GameStates;
using RoleTarget;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;
using FocusType = FocusSystem.FocusType;
using Board;

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
        
        public override void StartUse()
        {
            base.StartUse();
            BoardManager.instance.onCardClicked += OnCardClicked;
            FocusManager.instance.SetFocusOnType(FocusType.Cards, id => CheckIsTargetValid(id, TargetUtils.TargetType.Character));
            
            foreach (var _clientId in alreadyTargetedClients)
            {
                var _card = BoardManager.instance.visibleCards.FirstOrDefault(_c => _c.characterInfo.ownerClientId.Value == _clientId);
                if (_card != null)
                {
                    FocusManager.instance.UnfocusObject(_card.gameObject);
                }
            }
        }

        public override bool CanUse(bool _ignoreCurrentlyUsed = false)
        {
            // La vérification des cibles valides est déjà faite par base.CanUse()
            // via GetValidTargets() qui utilise notre targetValidator
            return base.CanUse(_ignoreCurrentlyUsed);
        }

        private void OnCardClicked(Card _clickedCard)
        {
            if (!CheckIsTargetValid(_clickedCard.characterInfo.ownerClientId.Value, TargetUtils.TargetType.Character))
            {
                return;
            }

            OnCardClickedRpc(_clickedCard.characterInfo.ownerClientId.Value);
            OnUsed();
        }

        [Rpc(SendTo.Server)]
        private void OnCardClickedRpc(ulong _characterId)
        {
            if (alreadyTargetedClients.Contains(_characterId) || currentTargets.Contains(_characterId))
            {
                return;
            }
            
            RoleTargetSystem.instance.NewTargeting(ownerClientId.Value, _characterId);
            ChatManager.instance.DiscoverChatRpc(powerChatId.Value, new FixedString64Bytes("Lié par l'encre"), 
                RpcTarget.Single(_characterId, RpcTargetUse.Persistent));
            
            currentTargets.Add(_characterId);
            alreadyTargetedClients.Add(_characterId);
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
                        ChatManager.instance.UndiscoverChatRpc(powerChatId.Value, RpcTarget.Single(_targetClientId, RpcTargetUse.Persistent));
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
            ChatManager.instance.DiscoverChatRpc(_chatId, new FixedString64Bytes("Lié par l'encre"), RpcTarget.Single(ownerClientId.Value, RpcTargetUse.Persistent));
        }

        protected override void StopUse()
        {
            base.StopUse();
            BoardManager.instance.onCardClicked -= OnCardClicked;
        }
    }
}