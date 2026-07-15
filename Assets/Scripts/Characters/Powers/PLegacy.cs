using System.Linq;
using CorruptionDuPortail.Domain.Powers;
using CorruptionDuPortail.Domain.Powers.Decisions;
using CorruptionDuPortail.Domain.Powers.State;
using GameLogic;

namespace Characters.Powers
{
    public class PLegacy : Power, ILegacyGrant
    {
        public Power legacyPower;
        public RoleID roleForLegacy;

        public bool isLegacyInherited = false;

        // Powers-POCO v2: the "grant on chain" logic is LegacyDecision (pure). The engine Power ref
        // (legacyPower) is power-local, so the carrier realises the grant via ILegacyGrant. The chain-watch
        // trigger stays here (it observes an NGO NetworkVariable). Behaviour-identical to the old inline grant.
        private readonly LegacyDecision _decision = new();

        public string effectDescription =>
            $"Si {roleForLegacy.ToString()} est enchainé, {ownerCharacter.role.roleName} hérite de \"{legacyPower.powerName}\".";

        public override void OnGameStartedServer()
        {
            base.OnGameStartedServer();
            var _charactersForLegacy = characterManager.GetCharacters(false).Where(_c => _c.role.roleID == roleForLegacy).ToList();
            foreach (var _character in _charactersForLegacy)
            {
                // Check if already chained
                if (_character.isChained.Value)
                {
                    OnCharacterChainChanged(false, true);
                }

                _character.isChained.OnValueChanged += OnCharacterChainChanged;
            }
        }

        private void OnCharacterChainChanged(bool _previousValue, bool _newValue)
        {
            if (!_newValue)
            {
                return;
            }

            RunDecisionEffects(_decision, new PowerContext(ownerSlot: (int)ownerClientId.Value), SelfState);
        }

        void ILegacyGrant.GrantLegacy(int _ownerSlot)
        {
            isLegacyInherited = true;
            characterManager.GivePowerToCharacter((ulong)_ownerSlot, legacyPower);
        }
        
        private void Reset()
        {
            powerName = "Héritage";
            powerDescription = "{var:effectDescription}";
        }
    }
}