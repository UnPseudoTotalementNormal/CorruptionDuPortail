using System.Linq;
using GameLogic;

namespace Characters.Powers.PowerComponents
{
    public class PCPowerUnlockWhenChain : PowerComponent //jsp honnetement j'avais compris autre chose pour l'heritage
    {
        public RoleID legacyRoleID;
        public string legacyRoleName => legacyRoleID.ToString();
        public string legacyStatus => hasLegacyBeenInherited ? "Hérité" : "Non hérité";
        
        public bool hasLegacyBeenInherited = false;
        
        private void Reset()
        {
            componentName = "Héritage";
            description = "Si un {var:legacyRoleName} est enchaîné, le pouvoir deviendra utilisable.\nÉtat actuel: {var:legacyStatus}.";
        }
        
        protected override void Awake()
        {
            base.Awake();
            if (!IsServer)
            {
                return;
            }
            
            power.onPowerGameStartedServerTriggered += OnGameStarted;
        }

        private void OnGameStarted()
        {
            var _legacyRoles = CharacterManager.instance.GetCharacters(false).Where(_c => _c.role.roleID == legacyRoleID).ToList();
            foreach (var _legacyRole in _legacyRoles)
            {
                _legacyRole.isChained.OnValueChanged += OnLegacyRoleChainChanged;
            }
        }

        private void OnLegacyRoleChainChanged(bool _previousValue, bool _newValue)
        {
            if (!_newValue || hasLegacyBeenInherited)
            {
                return;
            }
            
            hasLegacyBeenInherited = true;
        }

        protected override void Init()
        {
            if (!IsServer)
            {
                return;
            }
        }

        public override bool CanUsePower()
        {
            if (!base.CanUsePower())
            {
                return false;
            }

            return hasLegacyBeenInherited;
        }
    }
}