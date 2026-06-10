using System;
using UnityEngine;
using Board;
using Characters.Powers.Target;
using ChatSystem;
using CorruptionDuPortail.Domain;
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
            PowerEffectTrace.Record(new NewTargeting((int)ownerClientId.Value, (int)_character.ownerClientId.Value));
            RoleTargetSystem.instance.NewTargeting(ownerClientId.Value, _character.ownerClientId.Value);
            PowerEffectTrace.Record(new CorruptPlayer((int)_character.ownerClientId.Value));
            _character.CorruptPlayerServerRpc();
            PowerEffectTrace.Record(new RevealInfo((int)_character.ownerClientId.Value, RevealField.CorruptRevealed,
                RevealVisibility.Personal, (int)ownerClientId.Value, false));
            GameManager.instance.gameInfoRevealer.SetRevealLevel(
                _character.ownerClientId.Value, nameof(CharacterInfoReveal.isCorruptRevealed), RevealLevel.Personal, ownerClientId.Value);
            if (_character.role.factionType == FactionType.chosen)
            {
                PowerEffectTrace.Record(new AddCardEffect((int)CardEffectID.CursedVision, (int)_character.ownerClientId.Value, false));
                CardEffectManager.instance.AddCardEffect(CardEffectID.CursedVision, _character.ownerClientId.Value, false);
                PowerEffectTrace.Record(new ChatLocal($"{_character.GetOwnerPseudo()} est un élu.", (int)ChatWindowIDs.Server));
                ChatManager.instance.AddMessageLocal($"{_character.GetOwnerPseudo()} est un élu.", GameValues.CHAT_SERVER_CLIENT_ID, (int)ChatWindowIDs.Server);
            }
            else
            {
                PowerEffectTrace.Record(new AddCardEffect((int)CardEffectID.CursedVision, (int)_character.ownerClientId.Value, true));
                CardEffectManager.instance.AddCardEffect(CardEffectID.CursedVision, _character.ownerClientId.Value, true);
                PowerEffectTrace.Record(new ChatLocal($"{_character.GetOwnerPseudo()} n'est pas un élu.", (int)ChatWindowIDs.Server));
                ChatManager.instance.AddMessageLocal($"{_character.GetOwnerPseudo()} n'est pas un élu.", GameValues.CHAT_SERVER_CLIENT_ID, (int)ChatWindowIDs.Server);
            }
            PowerEffectTrace.Record(new CorruptPlayer((int)ownerClientId.Value));
            GameManager.instance.characterManager.GetCharacter(ownerClientId.Value).CorruptPlayerServerRpc();
            PowerEffectTrace.Record(new RevealInfo((int)ownerClientId.Value, RevealField.CorruptRevealed,
                RevealVisibility.Personal, (int)ownerClientId.Value, false));
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

        [SerializeField] private string pickerDescription;

        public override void StartUse()
        {
            base.StartUse();
            SelectionFlowService.instance.StartCharacterSelection(targetValidator, OnCharacterPicked,
                new SelectionFlowOptions { stepDescriptions = new[] { pickerDescription } });
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
