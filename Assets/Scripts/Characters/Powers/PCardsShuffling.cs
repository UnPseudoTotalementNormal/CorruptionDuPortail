using System.Collections.Generic;
using System.Linq;
using Characters.Powers.Target;
using FocusSystem;
using Unity.Netcode;

namespace Characters.Powers
{
    public class PCardsShuffling : Power
    {
        public NetworkList<ulong> discoveredClientIds; // List of client id role discovered or if discovered that it's not used
        
        private void OnCardClicked(Card _cardClicked)
        {
            OnCardClickedRpc(_cardClicked.roleInfo.ownerClientId);
        }

        [Rpc(SendTo.Server)]
        private void OnCardClickedRpc(ulong _clientIdClicked)
        {
            if (!IsTargetValid(_clientIdClicked))
            {
                return;
            }

            Character _character = CharacterManager.instance.GetCharacter(_clientIdClicked);
            if (_character.isFake)
            {
                discoveredClientIds.Add(_clientIdClicked);
                OnUsed();
            }
        }
        
        public override void StartUse()
        {
            base.StartUse();
            FocusManager.instance.SetFocusOnType(FocusType.Roles, IsTargetValid);
            BoardManager.instance.onCardClicked += OnCardClicked;
        }

        protected override void StopUse()
        {
            base.StopUse();
        }
        
        private bool IsTargetValid(ulong _targetId)
        {
            List<ulong> _discoveredIds = new();
            foreach (var _discoveredClientId in discoveredClientIds)
            {
                _discoveredIds.Add(_discoveredClientId);
            }
            return TargetUtils.IsTargetValid(_targetId, targetIncludeFlags) && _discoveredIds.All(_p => _p != _targetId);
        }
    }
}