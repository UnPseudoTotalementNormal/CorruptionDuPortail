using System.Linq;
using GameLogic;

namespace Characters.Powers
{
    public class PLegacy : Power
    {
        public Power legacyPower;
        public RoleID roleForLegacy;

        public bool isLegacyInherited = false;

        public string effectDescription =>
            $"Si {roleForLegacy.ToString()} est enchainé, {ownerCharacter.role.roleName} hérite de \"{legacyPower.powerName}\".";
        
        public override void OnGameStartedServer()
        {
            base.OnGameStartedServer();
            var _charactersForLegacy = CharacterManager.instance.GetCharacters(false).Where(_c => _c.role.roleID == roleForLegacy).ToList();
            foreach (var _character in _charactersForLegacy)
            {
                _character.isChained.OnValueChanged += OnCharacterChainChanged;
            }
        }

        private void OnCharacterChainChanged(bool _previousValue, bool _newValue)
        {
            if (!_newValue)
            {
                return;
            }
            
            isLegacyInherited = true;
            CharacterManager.instance.GivePowerToCharacter(ownerClientId.Value, legacyPower);
        }
        
        private void Reset()
        {
            powerName = "Héritage";
            powerDescription = "{var:effectDescription}";
        }
    }
}