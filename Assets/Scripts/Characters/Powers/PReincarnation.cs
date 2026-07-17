using UnityEngine;
using Characters.Powers.Target;
using CorruptionDuPortail.Domain.Powers;
using CorruptionDuPortail.Domain.Powers.Decisions;
using CorruptionDuPortail.Domain.Powers.State;
using GameLogic;
using UI.BoardUI.Selection;
using Unity.Netcode;

namespace Characters.Powers
{
    public class PReincarnation : Power, ISetPassiveState, IGrantRolePowers
    {
        // Powers-POCO v2: server logic in ReincarnationDecision (pure) — target, broadcast isPassive=true,
        // grant the owner every power of the picked role. The isPassive broadcast (an Everyone-RPC) and the
        // engine power grants are power-local, reached via SelfState. Behaviour-identical to the old inline.
        private readonly ReincarnationDecision _decision = new();

        void ISetPassiveState.SetPassive(bool _value) => ChangeIsPassiveRpc(_value);

        void IGrantRolePowers.GrantRolePowers(int _ownerSlot, int _fromRoleSlot)
        {
            Character _fromRoleCharacter = characterManager.GetCharacter((ulong)_fromRoleSlot);
            foreach (var _rolePower in _fromRoleCharacter.role.powers)
            {
                // Hide the granted powers from the RoleCard: showing them would leak that this is a real
                // L'Incomplet (a factice never reincarnates) and which role it reincarnated into. Flag only —
                // NOT isStolenCopy, since these are permanent, non-one-shot powers.
                characterManager.GivePowerToCharacter((ulong)_ownerSlot, _rolePower, ConfigureGrantedPower);
            }
        }

        // Runs on the server once the granted power is spawned + reparented under the owner (GivePowerToCharacter
        // onReady). Marks it hidden from the RoleCard without touching its usage semantics.
        private static void ConfigureGrantedPower(Power _granted)
        {
            if (_granted != null)
            {
                _granted.hideFromRoleCardRuntime.Value = true;
            }
        }

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
            selectionFlowService.StartRoleSelection(targetValidator, OnRolePicked,
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
            RunDecisionEffects(_decision, new PowerContext(
                ownerSlot: (int)ownerClientId.Value, targetSlot: (int)_characterClickedId), SelfState);
        }

        [Rpc(SendTo.Everyone)]
        private void ChangeIsPassiveRpc(bool _isPassive)
        {
            isPassive = _isPassive;
        }

        protected override void StopUse()
        {
            base.StopUse();
            selectionFlowService.CancelSelection();
        }
    }
}
