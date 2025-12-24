using Characters.Powers.Target;
using FocusSystem;
using GameLogic;
using Unity.Netcode;

namespace Characters.Powers
{
    public class PPersonalBeacons : Power
    {
        public override void StartUse()
        {
            base.StartUse();
            
            GameManager.instance.charactersBar.onCharacterBarClicked += OnCharacterBarClicked;
            
            FocusManager.instance.SetFocusOnType(FocusType.Roles, targetIncludeFlags);
        }

        private void OnCharacterBarClicked(Character _characterClicked)
        {
            OnCharacterClickedRpc(_characterClicked.ownerClientId.Value);
            
            OnUsed();
        }
        
        [Rpc(SendTo.Server)]
        private void OnCharacterClickedRpc(ulong _characterClickedId)
        {
            if (!TargetUtils.IsTargetValid(_characterClickedId, targetIncludeFlags))
            {
                return;
            }
            if (_characterClickedId == ownerClientId.Value)
            {
                return;
            }
            
            //TODO: spawn beacon logic here!
        }

        protected override void StopUse()
        {
            base.StopUse();
        }
    }
}