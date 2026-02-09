using System;
using Board;
using Characters.Powers.Target;
using ChatSystem;
using FocusSystem;
using GameLogic;
using RoleTarget;
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

        private void OnCardClicked(Card _clickedCard)
        {
            var _character = _clickedCard.characterInfo;
            if (!CheckIsTargetValid(_character.ownerClientId.Value, TargetUtils.TargetType.Character))
            {
                return;
            }
            RoleTargetSystem.instance.NewTargeting(ownerClientId.Value, _character.ownerClientId.Value);
            _character.CorruptPlayerServerRpc();
            GameManager.instance.gameInfoRevealer.SetRevealLevel(
                _character.ownerClientId.Value, nameof(CharacterInfoReveal.isCorruptRevealed), RevealLevel.Personal);
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
            GameManager.instance.gameInfoRevealer.SetRevealLevel(ownerClientId.Value, nameof(CharacterInfoReveal.isCorruptRevealed), RevealLevel.Personal);
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
            BoardManager.instance.onCardClicked += OnCardClicked;
            
            FocusManager.instance.SetFocusOnType(FocusType.Cards, id => CheckIsTargetValid(id, TargetUtils.TargetType.Character));
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
            
            FocusManager.instance.UnfocusAll();
        }
    }
}
