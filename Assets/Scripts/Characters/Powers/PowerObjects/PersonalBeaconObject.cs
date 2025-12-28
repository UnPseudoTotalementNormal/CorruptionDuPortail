using Network.Action;
using Unity.Netcode;

namespace Characters.Powers.PowerObjects
{
    public class PersonalBeaconObject
    {
        public ulong ownerClientId;
        public ulong targetClientId;

        public NetworkAction<bool> onCorruptedBeaconChanged;
        
        public PersonalBeaconObject(NetworkBehaviour _ownerBehaviour, ulong _targetClientId)
        {
            ownerClientId = _ownerBehaviour.OwnerClientId;
            targetClientId = _targetClientId;
            
            onCorruptedBeaconChanged = new NetworkAction<bool>($"onCorruptedBeaconChanged_{ownerClientId}_{targetClientId}", _ownerBehaviour);
            
            CharacterManager.instance.GetCharacter(targetClientId, false).isCorrupted.OnValueChanged += OnTargetCorruptedChanged;
        }

        private void OnTargetCorruptedChanged(bool _previousValue, bool _newValue)
        {
            onCorruptedBeaconChanged.Invoke(_newValue);
        }
    }
}