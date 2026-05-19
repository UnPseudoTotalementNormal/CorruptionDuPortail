using System.Linq;
using ChatSystem;
using GameLogic;
using Unity.Netcode;

namespace Characters.Powers.PowerComponents
{
    public class PCReparentOnChain : PowerComponent
    {
        public RoleID legacyRoleID;
        
        public string legacyRoleName => legacyRoleID.ToString();

        public Character currentParent;

        protected override void Init()
        {
            if (!NetworkManager.IsServer)
            {
                return;
            }
            
            currentParent = ownerCharacter;
            
            currentParent.isChained.OnValueChanged += OnCharacterChainChanged;
            power.onPowerReparented += OnPowerReparented;
        }

        private void OnPowerReparented()
        {
            currentParent.isChained.OnValueChanged -= OnCharacterChainChanged;
            currentParent = power.ownerCharacter;
        }

        private void OnCharacterChainChanged(bool _previousValue, bool _newValue)
        {
            if (!_newValue)
            {
                return;
            }

            var _potentialLegacyHolder = CharacterManager.instance.GetCharacters(false).Where(_c => _c.role.roleID == legacyRoleID)
                .Where(_c => !_c.role.powers.Any(_p => _p.IsTheSamePower(power)))
                .Where(_c => !_c.isChained.Value)
                .Where(_c => _c != currentParent)
                .OrderBy(_c => _c.isFake)
                .ToList();

            if (_potentialLegacyHolder.Count == 0)
            {
                ChatManager.instance.ReceiveChatMessageRpc(
                    new ChatMessage(ChatManager.SERVER_CLIENT_ID,
                        $"Aucun personnage n'est éligible pour hériter du pouvoir {power.powerName}.",
                        (int)ChatWindowIDs.Server),
                    RpcTarget.Single(ownerClientId, RpcTargetUse.Persistent)
                    );
                return;
            }
            
            PowerManager.instance.ReparentPowerToCharacterServer(power, _potentialLegacyHolder[0]);
        }

        private void Reset()
        {
            componentName = "Héritage";
            description = "Si ce personnage est enchaîné, {var:legacyRoleName} héritera de ce pouvoir.";
        }
    }
}