using UnityEngine;
using Characters.Powers.Target;
using FocusSystem;
using GameLogic;
using RoleTarget;
using UI.BoardUI;
using UI.BoardUI.Selection;
using Unity.Collections;
using Unity.Netcode;

namespace Characters.Powers
{
    public class PReincarnation : Power
    {
        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
            targetValidator.AddRule(ctx => TargetUtils.IsTargetValid(ctx.targetId, targetIncludeFlags, ctx.targetType));
            targetValidator.AddRule(ctx => 
            {
                // Ne pas pouvoir se réincarner en son propre rôle
                var targetCharacter = characterManager.GetCharacter(ctx.targetId, false);
                return targetCharacter == null || !ownerCharacter.role.IsTheSameRole(targetCharacter.role);
            });
        }

        [SerializeField] private string pickerDescription;

        public override void StartUse()
        {
            base.StartUse();
            SelectionFlowService.instance.StartRoleSelection(targetValidator, OnRolePicked,
                new SelectionFlowOptions { stepDescriptions = new[] { pickerDescription } });
        }

        private void OnRolePicked(Role _roleClicked)
        {
            if (!CheckIsTargetValid(_roleClicked.ownerClientId, TargetUtils.TargetType.Role))
            {
                return;
            }
            ReincarnatePlayerRpc(_roleClicked.ownerClientId);
            OnUsed();
        }

        [Rpc(SendTo.Server)]
        private void ReincarnatePlayerRpc(ulong _characterClickedId)
        {
            RoleTargetSystem.instance.NewTargeting(ownerClientId.Value, _characterClickedId);
            
            ChangeIsPassiveRpc(true);
            Character _characterClicked = characterManager.GetCharacter(_characterClickedId);
            foreach (var _rolePower in _characterClicked.role.powers)
            {
                characterManager.GivePowerToCharacter(ownerClientId.Value, _rolePower);
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
            SelectionFlowService.instance.CancelSelection();
        }
    }
}
