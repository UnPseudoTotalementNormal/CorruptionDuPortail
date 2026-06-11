using Board;
using Network.Action;
using Unity.Netcode;
using UnityEngine;

namespace Characters.Powers.PowerObjects
{
    public class PersonalBeaconObject
    {
        public ulong ownerClientId;
        public ulong targetClientId;

        public bool isCorruptedValue = false;
        public NetworkAction<bool> onCorruptedBeaconChanged;
        
        // Story 7.1 lane B: CharacterManager is pushed in by the owning power (which resolved it in
        // OnNetworkSpawn) instead of this PowerObject reaching for the locator itself.
        public PersonalBeaconObject(Power _ownerPower, ulong _targetClientId, CharacterManager _characterManager)
        {
            ownerClientId = _ownerPower.ownerClientId.Value;
            targetClientId = _targetClientId;

            onCorruptedBeaconChanged = new NetworkAction<bool>($"onCorruptedBeaconChanged_{ownerClientId}_{targetClientId}", _ownerPower);

            Character _targetCharacter = _characterManager.GetCharacter(targetClientId, false);

            if (ownerClientId == _characterManager.GetLocalClientId())
            {
                CardEffectManager.instance.AddCardEffect(CardEffectID.TechnoBeacon, targetClientId, this);
            }

            if (!NetworkManager.Singleton.IsServer)
            {
                return;
            }
            
            if (_targetCharacter.role.factionType == FactionType.chosen || 
                _targetCharacter.role.roleID == RoleID.Robot)
            {
                isCorruptedValue = _targetCharacter.isCorrupted.Value;
                OnTargetCorruptedChanged(isCorruptedValue, isCorruptedValue);
                _targetCharacter.isCorrupted.OnValueChanged += OnTargetCorruptedChanged;
            }
        }

        private void OnTargetCorruptedChanged(bool _previousValue, bool _newValue)
        {
            Debug.Log("PersonalBeaconObject: OnTargetCorruptedChanged: " + targetClientId + " is now corrupted: " + _newValue);
            isCorruptedValue = _newValue;
            onCorruptedBeaconChanged.Invoke(_newValue);
        }
    }
}