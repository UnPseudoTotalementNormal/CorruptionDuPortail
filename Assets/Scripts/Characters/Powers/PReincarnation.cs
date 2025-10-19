using Characters.Powers.Target;
using FocusSystem;
using GameLogic;
using Unity.Collections;
using Unity.Netcode;

namespace Characters.Powers
{
    public class PReincarnation : Power
    {
        public override void StartUse()
        {
            base.StartUse();
            
            GameManager.instance.charactersBar.onCharacterBarClicked += OnCharacterBarClicked;
            
            FocusManager.instance.SetFocusOnType(FocusType.Roles, targetIncludeFlags);
        }

        private void OnCharacterBarClicked(Character _characterClicked)
        {
            if (!TargetUtils.GetTargetsForRoles(targetIncludeFlags).Contains(_characterClicked.ownerClientId.Value))
            {
                return;
            }
            if (ownerCharacter.role.IsTheSameRole(_characterClicked.role))
            {
                return;
            }
            ReincarnatePlayerRpc(_characterClicked.ownerClientId.Value);
            OnUsed();
        }

        [Rpc(SendTo.Server)]
        private void ReincarnatePlayerRpc(ulong _characterClickedId)
        {
            ChangeIsPassiveRpc(true);
            Character _characterClicked = GameManager.instance.characterManager.GetCharacter(_characterClickedId);
            foreach (var _rolePower in _characterClicked.role.powers)
            {
                GameManager.instance.characterManager.GivePowerToCharacter(ownerClientId.Value, _rolePower);
            }
        }

        [Rpc(SendTo.Everyone)]
        private void ChangeIsPassiveRpc(bool _isPassive)
        {
            isPassive = _isPassive;
        }

        protected override void StopUse()
        {
            base.StopUse();
            
            GameManager.instance.charactersBar.onCharacterBarClicked -= OnCharacterBarClicked;
            FocusManager.instance.UnfocusAll();
        }
    }
}