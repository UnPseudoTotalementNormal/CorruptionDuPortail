using System.Collections.Generic;
using System;
using Characters.Powers.Target;
using ChatSystem;
using CorruptionDuPortail.Domain.Powers;
using CorruptionDuPortail.Domain.Powers.Decisions;
using CorruptionDuPortail.Domain.Powers.State;
using GameLogic;
using GameLogic.GameStates;
using UI.BoardUI.Selection;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

namespace Characters.Powers
{
    public class PBoundByInk : Power, IInkChatState, IInkTargetRegister
    {
        // Powers-POCO v2: click logic in BoundByInkDecision (pure). The power's chat id (read) and its ink
        // target lists (write) are power-local NGO state, exposed via IInkChatState / IInkTargetRegister and
        // reached through SelfState. Behaviour-identical to the old resolver path.
        private readonly BoundByInkDecision _decision = new();

        int IInkChatState.ChatId => powerChatId.Value;

        void IInkTargetRegister.RegisterInkTarget(int _slot)
        {
            currentTargets.Add((ulong)_slot);
            alreadyTargetedClients.Add((ulong)_slot);
        }

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
            selectionFlowService.StartCharacterSelection(targetValidator, OnCharacterPicked,
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

        [Rpc(SendTo.Server)]
        private void OnCardClickedRpc(ulong _characterId, RpcParams _params = default)
        {
            if (!ServerAuthorizeEffect(_params, _characterId)) return; // NET-09: server-side use authorization
            if (alreadyTargetedClients.Contains(_characterId) || currentTargets.Contains(_characterId))
            {
                return;
            }

            // State goes into the context (the decision READS ctx.State<IInkChatState>().ChatId) and the
            // runtime (RegisterInkTarget WRITES the ink lists) — both resolve to this carrier via SelfState.
            var _selfState = SelfState;
            RunDecisionEffects(_decision, new PowerContext(
                ownerSlot: (int)ownerClientId.Value, targetSlot: (int)_characterId, state: _selfState), _selfState);
        }

        public override void OnGameStartedServer()
        {
            base.OnGameStartedServer();
            AttributeBoundByInkChat();
            var _gameManager = GameManager.For(NetworkManager);
            foreach (var _awakeningState in _gameManager.GetGameStates(typeof(AwakeningState)))
            {
                _awakeningState.onStateStartServer += RevokeInkChannelServer;
                _subscribedNights.Add(_awakeningState);
            }
        }

        // A new night closes last night's ink channel (NET-11: server membership).
        private void RevokeInkChannelServer()
        {
            if (!IsServer || !isChatAttributed)
            {
                currentTargets.Clear();
                return;
            }
            foreach (var _targetClientId in currentTargets)
            {
                chatManager.RevokeChannelServer(powerChatId.Value, _targetClientId);
            }
            currentTargets.Clear();
        }

        private readonly List<GameState> _subscribedNights = new();

        // A copy of this power (stolen by Ugës, copied by Luma) is despawned once spent. Its night hook used to stay
        // subscribed: at the next night it read the despawned object's chat id (0 = the GENERAL channel) and revoked
        // the general chat of its targets. Close its own channel now and unsubscribe.
        public override void OnNetworkDespawn()
        {
            if (IsServer)
            {
                RevokeInkChannelServer();
                foreach (GameState _night in _subscribedNights)
                {
                    if (_night != null)
                    {
                        _night.onStateStartServer -= RevokeInkChannelServer;
                    }
                }
                _subscribedNights.Clear();
            }
            base.OnNetworkDespawn();
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
            chatManager.GrantChannelServer(_chatId, "Lié par l'encre", ownerClientId.Value); // NET-11: server membership
        }

        protected override void StopUse()
        {
            base.StopUse();
            selectionFlowService.CancelSelection();
        }
    }
}