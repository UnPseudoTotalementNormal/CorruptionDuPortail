#region

using System;
using Characters.Powers.Target;
using FocusSystem;
using GameLogic;
using RoleTarget;
using UI.BoardUI;
using UI.BoardUI.Selection;
using Unity.Netcode;
using Board;

#endregion

namespace Characters.Powers
{
    [Serializable]
    public class PChainedByTheShadows : Power
    {
        [NonSerialized] private Character clickedCharacter;

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
            targetValidator.AddRule(ctx => TargetUtils.IsTargetValid(ctx.targetId, targetIncludeFlags, ctx.targetType));
        }
        
        private void OnCardClicked(Card _clickedCard)
        {
            clickedCharacter = _clickedCard.characterInfo;
            if (!CheckIsTargetValid(clickedCharacter.ownerClientId.Value, TargetUtils.TargetType.Character))
            {
                return;
            }
            SelectionFlowService.instance.StartCharacterThenRoleSelection(_clickedCard, targetValidator, OnRolePicked);
        }
        private void OnRolePicked(Role _role)
        {
            if (!clickedCharacter ||
                !CheckIsTargetValid(_role.ownerClientId, TargetUtils.TargetType.Role))
            {
                return;
            }
            TryCorruptCharacterServerRpc(clickedCharacter.ownerClientId.Value, _role);
            OnUsed();
        }
        [Rpc(SendTo.Server)]
        private void TryCorruptCharacterServerRpc(ulong _corruptingCharacterId, Role _compareRole)
        {
            Character _corruptingCharacter = GameManager.instance.characterManager.GetCharacter(_corruptingCharacterId, false);
            RoleTargetSystem.instance.NewTargeting(ownerClientId.Value, _corruptingCharacterId);
            if (_corruptingCharacter.role.IsTheSameRole(_compareRole))
            {
                GameManager.instance.gameInfoRevealer.SendRevealLevelRpc(
                    _corruptingCharacter.ownerClientId.Value, nameof(CharacterInfoReveal.isRoleRevealed), RevealLevel.Personal, ownerClientId.Value, true);
                if (_corruptingCharacter.role.factionType == FactionType.chosen)
                {
                    ChainingManager.instance.AddCharacterToChainingList(_corruptingCharacterId);
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
            
            FocusManager.instance.SetFocusOnType(FocusType.Cards, id => CheckIsTargetValid(id, TargetUtils.TargetType.Character));

            clickedCharacter = null;
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
            SelectionFlowService.instance.CancelSelection();
        }

        public override void OnGameStartedServer()
        {
            base.OnGameStartedServer();
        }
    }
}
