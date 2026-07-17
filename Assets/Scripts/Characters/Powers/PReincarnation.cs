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

        void ISetPassiveState.SetPassive(bool _value) => ChangeIsPassiveRpc(_value);

        void IGrantRolePowers.GrantRolePowers(int _ownerSlot, int _fromRoleSlot)
        {
            Character _fromRoleCharacter = characterManager.GetCharacter((ulong)_fromRoleSlot);

            // If THIS Réincarnation is itself a copy (stolen by Ugues/Luma), its grants are one-shot — a temporary
            // copy power must never mint permanent powers (balance). L'Incomplet's own Réincarnation grants stay
            // permanent, but still flagged as copies so no copier can re-copy them.
            bool _isCopiedReincarnation = isStolenCopy.Value;
            Action<Power> _onReady = _isCopiedReincarnation
                ? (Action<Power>)Power.ConfigureAsOneShotStolenCopy
                : Power.MarkAsPermanentCopy;

            foreach (var _rolePower in _fromRoleCharacter.role.powers)
            {
                // Never copy an already-copied power (of any provenance, passive or active) — no copy chains.
                if (_rolePower == null || _rolePower.isCopiedPower.Value)
                {
                    continue;
                }
                // A copied (one-shot) Réincarnation skips PASSIVE powers: a passive is never "spent", so the
                // one-shot config would never despawn it and it would persist forever — a temporary copy minting a
                // permanent power (Poyo, option A, 2026-07-17). The real Incomplet still grants passives normally.
                // Design choice, may be revisited later (e.g. passives lasting one awakening).
                if (_isCopiedReincarnation && _rolePower.BaseIsPassive)
                {
                    continue;
                }
                characterManager.GivePowerToCharacter((ulong)_ownerSlot, _rolePower, _onReady);
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
