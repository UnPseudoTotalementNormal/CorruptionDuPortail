using System;
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

        // NET-08: the live passive flag is replicated state (Power.passiveOverride), not an Everyone-RPC event.
        void ISetPassiveState.SetPassive(bool _value) => SetPassiveServer(_value);

        void IGrantRolePowers.GrantRolePowers(int _ownerSlot, int _fromRoleSlot)
        {
            Character _fromRoleCharacter = characterManager.GetCharacter((ulong)_fromRoleSlot);

            // A copied (Ugues/Luma) Reincarnation grants ONE-SHOT copies (a temporary copy must never mint
            // permanent powers); l'Incomplet's own Reincarnation grants PERMANENT copies. Either way the grants
            // are hidden from the RoleCard (showing them leaks a real Incomplet + the copied role) via the onReady.
            bool _isCopiedReincarnation = isStolenCopy.Value;
            Action<Power> _onReady = _isCopiedReincarnation ? ConfigureOneShotGrant : ConfigurePermanentGrant;

            foreach (var _rolePower in _fromRoleCharacter.role.powers)
            {
                // Never copy an already-copied power (of any provenance, passive or active) -- no copy chains.
                if (_rolePower == null || _rolePower.isCopiedPower.Value)
                {
                    continue;
                }
                // A copied (one-shot) Reincarnation skips PASSIVE powers: a passive is never "spent", so the
                // one-shot config would never despawn it and it would persist forever -- a temporary copy minting a
                // permanent power (Poyo, option A, 2026-07-17). The real Incomplet still grants passives normally.
                if (_isCopiedReincarnation && _rolePower.BaseIsPassive)
                {
                    continue;
                }
                characterManager.GivePowerToCharacter((ulong)_ownerSlot, _rolePower, _onReady);
            }
        }

        // onReady for a PERMANENT grant (real Incomplet): mark it a copy (non-recopiable, permanent) + hide it
        // from the RoleCard. Server-only (runs from GivePowerToCharacter's server-side onReady hook).
        private static void ConfigurePermanentGrant(Power _granted)
        {
            Power.MarkAsPermanentCopy(_granted);
            HideGrantFromRoleCard(_granted);
        }

        // onReady for a ONE-SHOT grant (a copied Reincarnation): one-shot stolen-copy semantics (despawn-on-use,
        // non-recopiable) + hide it from the RoleCard.
        private static void ConfigureOneShotGrant(Power _granted)
        {
            Power.ConfigureAsOneShotStolenCopy(_granted);
            HideGrantFromRoleCard(_granted);
        }

        // Hide a runtime-granted power from the RoleCard so it doesn't leak the Incomplet identity / copied role.
        private static void HideGrantFromRoleCard(Power _granted)
        {
            if (_granted != null && _granted.IsServer)
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

        protected override void StopUse()
        {
            base.StopUse();
            selectionFlowService.CancelSelection();
        }
    }
}
