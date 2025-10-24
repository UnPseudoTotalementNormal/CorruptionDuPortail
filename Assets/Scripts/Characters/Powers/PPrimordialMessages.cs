using System.Collections.Generic;
using System.Linq;
using Characters.Powers.Target;
using ChatSystem;
using FocusSystem;
using GameLogic;
using GameLogic.GameStates;
using Network;
using Unity.Netcode;
using UnityEngine;
using FocusType = FocusSystem.FocusType;

namespace Characters.Powers
{
    public class PPrimordialMessages : Power
    {
        private const int PRIMORDIAL_CHAT_ID_BEGIN = 515100;
        private static List<int> usedPrimordialChatIds = new();
        
        private NetworkVariable<int> primordialChatId = new(-1);
        
        private List<ulong> currentTargets = new();
        private NetworkList<ulong> alreadyTargetedClients = new();
        
        public override void StartUse()
        {
            base.StartUse();
            BoardManager.instance.onCardClicked += OnCardClicked;
            FocusManager.instance.SetFocusOnType(FocusType.Cards, targetIncludeFlags);
            
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
            if (!base.CanUse(_ignoreCurrentlyUsed))
            {
                return false;
            }
            
            List<ulong> _possibleTargets = TargetUtils.GetTargetsForCharacters(targetIncludeFlags)
                .Where(_id => !alreadyTargetedClients.Contains(_id)).ToList();
            return _possibleTargets.Count > 0;
        }

        private void OnCardClicked(Card _clickedCard)
        {
            if (!TargetUtils.GetTargetsForCharacters(targetIncludeFlags).Contains(_clickedCard.characterInfo.ownerClientId.Value) ||
                alreadyTargetedClients.Contains(_clickedCard.characterInfo.ownerClientId.Value))
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
            
            ChatManager.instance.DiscoverChatRpc(primordialChatId.Value, RpcTarget.Single(_characterId, RpcTargetUse.Persistent));
            
            currentTargets.Add(_characterId);
            alreadyTargetedClients.Add(_characterId);
        }

        public override void OnGameStartedServer()
        {
            base.OnGameStartedServer();
            AttributePrimordialChat();
            var _gameManager = GameManager.instance;
            foreach (var _awakeningState in _gameManager.GetGameStates(typeof(AwakeningState)))
            {
                _awakeningState.onStateEndServer += () =>
                {
                    foreach (var _targetClientId in currentTargets)
                    {
                        ChatManager.instance.UndiscoverChatRpc(primordialChatId.Value, RpcTarget.Single(_targetClientId, RpcTargetUse.Persistent));
                    }
                    
                    currentTargets.Clear();
                };
            }
        }

        private void AttributePrimordialChat()
        {
            if (!IsServer)
            {
                Debug.LogError("AttributePrimordialChat should be called on server only.");
                return;
            }
            
            int _chatId = PRIMORDIAL_CHAT_ID_BEGIN;
            while (usedPrimordialChatIds.Contains(_chatId))
            {
                _chatId++;
            }
            
            primordialChatId.Value = _chatId;
            usedPrimordialChatIds.Add(_chatId);
            ChatManager.instance.DiscoverChatRpc(_chatId, RpcTarget.Single(ownerClientId.Value, RpcTargetUse.Persistent));
        }

        protected override void StopUse()
        {
            base.StopUse();
            BoardManager.instance.onCardClicked -= OnCardClicked;
        }
    }
}