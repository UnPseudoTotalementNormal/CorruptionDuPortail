#region

using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Characters.Powers.Target;
using ChatSystem;
using FocusSystem;
using GameLogic;
using RoleTarget;
using UI.BoardUI.Selection;
using Unity.Netcode;
using UnityEngine.Assertions;
using FocusType = FocusSystem.FocusType;

#endregion

namespace Characters.Powers
{
    [Serializable]
    public class PVisionOfTheImpossible : Power
    {
        public int charactersToSelect = 2;
        public int rolesToSelect = 2;
        
        [NonSerialized] private List<Character> clickedCharacters = new();
        [NonSerialized] private List<Role> clickedRoles = new();

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
            targetValidator.AddRule(ctx => TargetUtils.IsTargetValid(ctx.targetId, targetIncludeFlags, ctx.targetType));
            targetValidator.AddRule(ctx => 
            {
                // Pour les personnages: ne pas inclure ceux déjà cliqués
                if (ctx.targetType == TargetUtils.TargetType.Character)
                {
                    return !clickedCharacters.Any(c => c.ownerClientId.Value == ctx.targetId);
                }
                // Pour les rôles: ne pas inclure ceux déjà cliqués
                else
                {
                    var character = characterManager.GetCharacter(ctx.targetId, false);
                    return character == null || !clickedRoles.Any(_r => _r.IsTheSameRole(character.role));
                }
            });
        }
        
        private void OnCharacterPicked(Character _clickedCharacter)
        {
            if (!CheckIsTargetValid(_clickedCharacter.ownerClientId.Value, TargetUtils.TargetType.Character))
            {
                return;
            }
            clickedCharacters.Add(_clickedCharacter);
            if (clickedCharacters.Count >= charactersToSelect)
            {
                StartRoleSelection();
                return;
            }
            StartCharacterSelection();
        }

        [SerializeField] private string characterPickerDescription;
        [SerializeField] private string rolePickerDescription;

        private void StartCharacterSelection()
        {
            SelectionFlowService.instance.StartCharacterSelection(targetValidator, OnCharacterPicked,
                new SelectionFlowOptions
                {
                    focusType        = FocusType.Cards,
                    stepDescriptions = new[] { characterPickerDescription },
                });
        }

        private void OnRolePicked(Role _roleClicked)
        {
            if (!CheckIsTargetValid(_roleClicked.ownerClientId, TargetUtils.TargetType.Role))
            {
                return;
            }
            clickedRoles.Add(_roleClicked);
            if (clickedRoles.Count >= rolesToSelect)
            {
                FocusManager.instance.UnfocusAll();
                
                OnUsed();
                
                OnVisionGuessServerRpc(
                    clickedCharacters.Select(_c => _c.ownerClientId.Value).ToArray(),
                    clickedRoles.ToArray());
                return;
            }

            StartRoleSelection();
        }

        private void StartRoleSelection()
        {
            SelectionFlowService.instance.StartRoleSelection(targetValidator, OnRolePicked,
                new SelectionFlowOptions { stepDescriptions = new[] { rolePickerDescription } });
        }

        [Rpc(SendTo.Server)]
        private void OnVisionGuessServerRpc(ulong[] _guessedCharacterIds, Role[] _guessedRoles)
        {
            Assert.IsTrue(NetworkManager.IsServer, "OnVisionGuessServerRpc should only be called on server");
            string _message = string.Empty;
            foreach (var _guessedCharacterId in _guessedCharacterIds)
            {
                var _guessedCharacter = characterManager.GetCharacter(_guessedCharacterId, false);
                RoleTargetSystem.instance.NewTargeting(ownerClientId.Value, _guessedCharacter.ownerClientId.Value);
                if (_guessedRoles.Any(_r => _r.IsTheSameRole(_guessedCharacter.role)))
                {
                    if (_message != String.Empty)
                    {
                        _message += "\n";
                    }
                    _message += $"{_guessedCharacter.GetOwnerPseudo()} est l'un de ces personnages.";
                    break;
                }
            }
            if (_message == String.Empty)
            {
                _message = "Aucun personnage n'a été trouvé.";
            }
            ChatManager.instance.ReceiveChatMessageRpc(new ChatMessage(GameValues.FAKE_CLIENT_ID, _message, (int)ChatWindowIDs.Server),
                _rpcParams:characterManager.GetSafeRpcTarget(ownerClientId.Value));
        }   

        public override bool CanUse(bool _ignoreCurrentlyUsed = false)
        {
            bool _baseValue = base.CanUse(_ignoreCurrentlyUsed);
            if (!_baseValue)
            {
                return false;
            }
            
            return true;
        }

        public override void StartUse()
        {
            base.StartUse();

            clickedCharacters.Clear();
            clickedRoles.Clear();

            StartCharacterSelection();
        }

        public override void Cancel()
        {
            if (!isCurrentlyUsed)
            {
                return;
            }
            base.Cancel();
        }
        
        protected override void StopUse()
        {
            base.StopUse();
            SelectionFlowService.instance.CancelSelection();
            FocusManager.instance.UnfocusAll();
        }
    }
}
