#region

using System;
using System.Collections.Generic;
using System.Linq;
using Board.UI.CharacterBar;
using Characters.Powers.Target;
using FocusSystem;
using GameLogic;
using GameLogic.GameStates;
using Network;
using RoleTarget;
using Unity.Netcode;
using UnityEngine.Assertions;

#endregion

namespace Characters.Powers
{
    [Serializable]
    public class PChainedByTheShadows : Power
    {
        [NonSerialized] private Character clickedCharacter;
        
        private void OnCardClicked(Card _clickedCard)
        {
            clickedCharacter = _clickedCard.characterInfo;

            if (!TargetUtils.GetTargetsForCharacters(targetIncludeFlags).Contains(clickedCharacter.ownerClientId.Value))
            {
                return;
            }
            
            GameManager.instance.charactersBar.onCharacterBarClicked += OnCharacterBarClicked;
            
            FocusManager.instance.SetFocusOnType(FocusType.Roles, targetIncludeFlags);
            FocusManager.instance.FocusObject(_clickedCard.gameObject);
        }
        
        private void OnCharacterBarClicked(Character _character)
        {
            var _roleClicked = _character.role;

            if (!TargetUtils.GetTargetsForRoles(targetIncludeFlags).Contains(clickedCharacter.ownerClientId.Value))
            {
                return;
            }
            
            TryCorruptCharacterServerRpc(NetworkManager.Singleton.LocalClientId, clickedCharacter.ownerClientId.Value, _character.role);
            OnUsed();
        }

        [Rpc(SendTo.Server)]
        private void TryCorruptCharacterServerRpc(ulong senderId, ulong corruptingCharacterId, Role compareRole)
        {
            Character corruptingCharacter = GameManager.instance.characterManager.GetCharacter(corruptingCharacterId, false);
            RoleTargetSystem.instance.NewTargeting(senderId, corruptingCharacterId);
            
            if (corruptingCharacter.role.IsTheSameRole(compareRole))
            {
                GameManager.instance.gameInfoRevealer.SetRevealLevelRpc(
                    corruptingCharacter.ownerClientId.Value, nameof(CharacterInfoReveal.isRoleRevealed), RevealLevel.Personal, true,
                    GameManager.instance.RpcTarget.Single(senderId, RpcTargetUse.Persistent));
                if (corruptingCharacter.role.factionType == FactionType.chosen)
                {
                    GameManager.instance.chainingManager.chainingPlayers.Add(corruptingCharacterId);
                }
            }
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
            BoardManager.instance.onCardClicked += OnCardClicked;
            
            FocusManager.instance.SetFocusOnType(FocusType.Cards, targetIncludeFlags);

            clickedCharacter = null;
        }

        public override void OnUsed()
        {
            base.OnUsed();
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
            BoardManager.instance.onCardClicked -= OnCardClicked;
            GameManager.instance.charactersBar.onCharacterBarClicked -= OnCharacterBarClicked;
            FocusManager.instance.UnfocusAll();
        }

        public override void OnGameStartedServer()
        {
            base.OnGameStartedServer();
        }
    }
}