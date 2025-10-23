#region

using System;
using Characters.Powers.Target;
using FocusSystem;
using GameLogic;
using RoleTarget;
using Unity.Netcode;

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
            TryCorruptCharacterServerRpc(clickedCharacter.ownerClientId.Value, _character.role);
            OnUsed();
        }
        [Rpc(SendTo.Server)]
        private void TryCorruptCharacterServerRpc(ulong _corruptingCharacterId, Role _compareRole)
        {
            Character _corruptingCharacter = GameManager.instance.characterManager.GetCharacter(_corruptingCharacterId, false);
            RoleTargetSystem.instance.NewTargeting(ownerClientId.Value, _corruptingCharacterId);
            if (_corruptingCharacter.role.IsTheSameRole(_compareRole))
            {
                GameManager.instance.gameInfoRevealer.SetRevealLevelRpc(
                    _corruptingCharacter.ownerClientId.Value, nameof(CharacterInfoReveal.isRoleRevealed), RevealLevel.Personal, true,
                    GameManager.instance.RpcTarget.Single(ownerClientId.Value, RpcTargetUse.Persistent));
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
            
            FocusManager.instance.SetFocusOnType(FocusType.Cards, targetIncludeFlags);

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
            GameManager.instance.charactersBar.onCharacterBarClicked -= OnCharacterBarClicked;
            FocusManager.instance.UnfocusAll();
        }

        public override void OnGameStartedServer()
        {
            base.OnGameStartedServer();
        }
    }
}