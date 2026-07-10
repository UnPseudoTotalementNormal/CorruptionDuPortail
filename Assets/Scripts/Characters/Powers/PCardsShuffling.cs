using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Characters.Powers.Target;
using ChatSystem;
using CorruptionDuPortail.Domain.Powers;
using CorruptionDuPortail.Domain.Powers.Decisions;
using CorruptionDuPortail.Domain.Powers.State;
using GameLogic;
using RoleTarget;
using UI.BoardUI.Selection;
using Unity.Netcode;
using FocusType = FocusSystem.FocusType;

namespace Characters.Powers
{
    public class PCardsShuffling : Power, ICardsShufflingGuess, IDiscoveredAdd
    {
        public NetworkList<ulong> discoveredClientIds = new(); // List of client id role discovered or if discovered that it's not used
        private ulong currentRoleGuessClientId;

        // Powers-POCO v2: the guess-resolution logic lives in CardsShufflingDecision (pure). The guess
        // report (read) + the discovered-list write are power-local, exposed via ICardsShufflingGuess /
        // IDiscoveredAdd and reached through SelfState. The multi-step selection flow + fake-card branch stay
        // here. Behaviour-identical to the old inline GuessRoleRpc.
        private readonly CardsShufflingDecision _decision = new();
        private ulong _lastGuessClickedId;

        bool ICardsShufflingGuess.IsCorrect =>
            characterManager.GetCharacter(_lastGuessClickedId).role.roleID
            == characterManager.GetCharacter(currentRoleGuessClientId).role.roleID;

        string ICardsShufflingGuess.ClickedPseudo =>
            lobbyPlayerInfoHolder.GetPlayerInfo(_lastGuessClickedId).playerName.ToString();

        string ICardsShufflingGuess.GuessRoleName =>
            characterManager.GetCharacter(currentRoleGuessClientId).role.roleName.ToString();

        IReadOnlyList<string> ICardsShufflingGuess.TargetedRoleNames =>
            roleTargetSystem.GetAllTargetingDataForTargeter(currentRoleGuessClientId)
                .Select(_td => characterManager.GetCharacter(_td.targetId).role.roleName.ToString())
                .ToList();

        void IDiscoveredAdd.DiscoveredAdd(int _slot) => discoveredClientIds.Add((ulong)_slot);

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
            targetValidator.AddRule(ctx => TargetUtils.IsTargetValid(ctx.targetId, targetIncludeFlags, ctx.targetType));
            targetValidator.AddRule(ctx => !discoveredClientIds.Contains(ctx.targetId));
        }
        
        private void OnRolePicked(Role _role)
        {
            ulong _clientIdClicked = _role.ownerClientId;
            if (!CheckIsTargetValid(_clientIdClicked, TargetUtils.TargetType.Role))
            {
                return;
            }

            currentRoleGuessClientId = _clientIdClicked;
            
            OnCharacterBarObjectClickedRpc(_clientIdClicked);
        }

        [Rpc(SendTo.Server)]
        private void OnCharacterBarObjectClickedRpc(ulong _clientIdClicked)
        {
            // probably redundant check
            /*if (!IsTargetValid(_clientIdClicked))
            {
                return;
            }*/

            Character _character = characterManager.GetCharacter(_clientIdClicked);
            if (_character.isFake)
            {
                ChatMessage _fakeMessage = new ChatMessage
                {
                    message = $"Le rôle selectionné ({_character.role.roleName.ToString()}) était une fausse carte.",
                    senderClientId = ChatManager.SERVER_CLIENT_ID,
                    chatId = (int)ChatWindowIDs.Server
                };
                chatManager.ReceiveChatMessageRpc(_fakeMessage, characterManager.GetSafeRpcTarget(ownerClientId.Value));
                discoveredClientIds.Add(_clientIdClicked);
                OnUsed();
                return; //character was fake, do nothing else
            }
            
            currentRoleGuessClientId = _character.ownerClientId.Value;
            AskForGuessRoleRpc(characterManager.GetSafeRpcTarget(ownerClientId.Value));
        }
        
        private void OnGuessCharacterPicked(Character _character)
        {
            GuessRoleRpc(_character.ownerClientId.Value);
            OnUsed();
        }

        [Rpc(SendTo.Server)]
        private void GuessRoleRpc(ulong _clickedId)
        {
            _lastGuessClickedId = _clickedId;
            // State feeds BOTH the context (the decision READS ctx.State<ICardsShufflingGuess>()) and the
            // runtime (DiscoveredAdd WRITES the discovered list) — both resolve to this carrier via SelfState.
            var _selfState = SelfState;
            RunDecisionEffects(_decision, new PowerContext(
                ownerSlot: (int)ownerClientId.Value, targetSlot: (int)_clickedId, state: _selfState), _selfState);

            OnUsed();
        }

        [Rpc(SendTo.SpecifiedInParams)]
        public void AskForGuessRoleRpc(RpcParams _rpcParams)
        {
            Character _guessCharacter = characterManager.GetCharacter(currentRoleGuessClientId);

            selectionFlowService.StartCharacterSelection(null, OnGuessCharacterPicked,
                new SelectionFlowOptions
                {
                    focusType        = FocusType.Cards,
                    stepDescriptions = new[] { guessCharacterPickerDescription },
                    pinnedRole       = _guessCharacter.role
                });
        }

        [SerializeField] private string rolePickerDescription;
        [SerializeField] private string guessCharacterPickerDescription;

        public override void StartUse()
        {
            base.StartUse();
            selectionFlowService.StartRoleSelection(targetValidator, OnRolePicked,
                new SelectionFlowOptions { stepDescriptions = new[] { rolePickerDescription } });
        }

        protected override void StopUse()
        {
            base.StopUse();
            selectionFlowService.CancelSelection();
        }
    }
}
