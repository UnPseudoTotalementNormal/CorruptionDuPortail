using System;
using Board;
using Characters.Powers.Target;
using ChatSystem;
using GameLogic;
using RoleTarget;
using UI.BoardUI.Selection;
using Unity.Netcode;

namespace Characters.Powers
{
    [Serializable]
    public class PCursedVision : Power
    {
        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
            targetValidator.AddRule(ctx => TargetUtils.IsTargetValid(ctx.targetId, targetIncludeFlags, ctx.targetType));
        }

        private void OnCharacterPicked(Character _character)
        {
            if (!CheckIsTargetValid(_character.ownerClientId.Value, TargetUtils.TargetType.Character))
            {
                return;
            }
            RoleTargetSystem.instance.NewTargeting(ownerClientId.Value, _character.ownerClientId.Value);
            _character.CorruptPlayerServerRpc();
            GameManager.instance.gameInfoRevealer.SetRevealLevel(
                _character.ownerClientId.Value, nameof(CharacterInfoReveal.isCorruptRevealed), RevealLevel.Personal, ownerClientId.Value);
            if (_character.role.factionType == FactionType.chosen)
            {
                CardEffectManager.instance.AddCardEffect(CardEffectID.CursedVision, _character.ownerClientId.Value, false);
                ChatManager.instance.AddMessageLocal($"{_character.GetOwnerPseudo()} est un élu.", GameValues.CHAT_SERVER_CLIENT_ID, (int)ChatWindowIDs.Server);
            }
            else
            {
                CardEffectManager.instance.AddCardEffect(CardEffectID.CursedVision, _character.ownerClientId.Value, true);
                ChatManager.instance.AddMessageLocal($"{_character.GetOwnerPseudo()} n'est pas un élu.", GameValues.CHAT_SERVER_CLIENT_ID, (int)ChatWindowIDs.Server);
            }
            GameManager.instance.characterManager.GetCharacter(ownerClientId.Value).CorruptPlayerServerRpc();   
            GameManager.instance.gameInfoRevealer.SetRevealLevel(ownerClientId.Value, nameof(CharacterInfoReveal.isCorruptRevealed), RevealLevel.Personal, ownerClientId.Value);
            OnUsed();
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
            SelectionFlowService.instance.StartCharacterSelection(targetValidator, OnCharacterPicked);
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
        }
    }
}
