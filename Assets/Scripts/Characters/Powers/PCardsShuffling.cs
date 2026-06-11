using System;
using System.Collections.Generic;
using UnityEngine;
using Characters.Powers.Target;
using ChatSystem;
using FocusSystem;
using GameLogic;
using Network;
using RoleTarget;
using UI.BoardUI.Selection;
using Unity.Netcode;
using FocusType = FocusSystem.FocusType;

namespace Characters.Powers
{
    public class PCardsShuffling : Power
    {
        public NetworkList<ulong> discoveredClientIds = new(); // List of client id role discovered or if discovered that it's not used
        private ulong currentRoleGuessClientId;

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

            Character _character = CharacterManager.For(NetworkManager).GetCharacter(_clientIdClicked);
            if (_character.isFake)
            {
                ChatMessage _fakeMessage = new ChatMessage
                {
                    message = $"Le rôle selectionné ({_character.role.roleName.ToString()}) était une fausse carte.",
                    senderClientId = ChatManager.SERVER_CLIENT_ID,
                    chatId = (int)ChatWindowIDs.Server
                };
                ChatManager.instance.ReceiveChatMessageRpc(_fakeMessage, CharacterManager.For(NetworkManager).GetSafeRpcTarget(ownerClientId.Value));
                discoveredClientIds.Add(_clientIdClicked);
                OnUsed();
                return; //character was fake, do nothing else
            }
            
            currentRoleGuessClientId = _character.ownerClientId.Value;
            AskForGuessRoleRpc(CharacterManager.For(NetworkManager).GetSafeRpcTarget(ownerClientId.Value));
        }
        
        private void OnGuessCharacterPicked(Character _character)
        {
            GuessRoleRpc(_character.ownerClientId.Value);
            OnUsed();
        }

        [Rpc(SendTo.Server)]
        private void GuessRoleRpc(ulong _clickedId)
        {
            Character _clickedCharacter = CharacterManager.For(NetworkManager).GetCharacter(_clickedId);
            Character _guessCharacter = CharacterManager.For(NetworkManager).GetCharacter(currentRoleGuessClientId);
            bool _isCorrectGuess = _clickedCharacter.role.roleID == _guessCharacter.role.roleID;
            
            RoleTargetSystem.instance.NewTargeting(ownerClientId.Value, _clickedCharacter.ownerClientId.Value);
            
            ChatMessage _resultMessage = new ChatMessage
            {
                senderClientId = ChatManager.SERVER_CLIENT_ID,
                chatId = (int)ChatWindowIDs.Server,
                message = _isCorrectGuess
                    ? $"Vous avez correctement deviné que {LobbyPlayerInfoHolder.instance.GetPlayerInfo(_clickedId).playerName} est {_guessCharacter.role.roleName}."
                    : $"Votre supposition était incorrecte, {LobbyPlayerInfoHolder.instance.GetPlayerInfo(_clickedId).playerName} n'est pas {_guessCharacter.role.roleName}."
            };
            
            
            if (_isCorrectGuess)
            {
                discoveredClientIds.Add(_clickedId);
                GameManager.For(NetworkManager).gameInfoRevealer.SendRevealLevelRpc(_clickedId, nameof(CharacterInfoReveal.isRoleRevealed), RevealLevel.Personal, ownerClientId.Value, true);
            }
            else
            {
                List<TargetingData> _targetedClientIds = RoleTargetSystem.instance.GetAllTargetingDataForTargeter(currentRoleGuessClientId);
                if (_targetedClientIds.Count == 0)
                {
                    _resultMessage.message += $"\nLe role {_guessCharacter.role.roleName} n'a ciblé aucun rôle.";
                }
                else
                {
                    _resultMessage.message += $"\nLe role {_guessCharacter.role.roleName} a ciblé ces rôles:";
                    foreach (var _targetData in _targetedClientIds)
                    {
                        Character _targetedCharacter = CharacterManager.For(NetworkManager).GetCharacter(_targetData.targetId);
                        _resultMessage.message += $"\n- {_targetedCharacter.role.roleName}";
                    }
                }
            }
            
            ChatManager.instance.ReceiveChatMessageRpc(_resultMessage, CharacterManager.For(NetworkManager).GetSafeRpcTarget(ownerClientId.Value));
            
            OnUsed();
        }

        [Rpc(SendTo.SpecifiedInParams)]
        public void AskForGuessRoleRpc(RpcParams _rpcParams)
        {
            Character _guessCharacter = CharacterManager.For(NetworkManager).GetCharacter(currentRoleGuessClientId);

            SelectionFlowService.instance.StartCharacterSelection(null, OnGuessCharacterPicked,
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
            SelectionFlowService.instance.StartRoleSelection(targetValidator, OnRolePicked,
                new SelectionFlowOptions { stepDescriptions = new[] { rolePickerDescription } });
        }

        protected override void StopUse()
        {
            base.StopUse();
            SelectionFlowService.instance.CancelSelection();
        }
    }
}
