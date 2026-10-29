using System;
using UnityEngine;
using Characters.Powers.Target;
using CorruptionDuPortail.Domain.Powers;
using CorruptionDuPortail.Domain.Powers.Decisions;
using CorruptionDuPortail.Domain.Powers.State;
using Diagnostics.Logs;
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
            // Board T26: l'Incomplet, original or copy, never copies l'Incomplet (the picker already refuses it; this
            // is the server's word on it).
            if (IsIncomplet(_fromRoleCharacter))
            {
                GameLog.Info("power", "power.reincarnation.refused", "owner", (ulong)_ownerSlot, "target", (ulong)_fromRoleSlot,
                    "reason", "incomplet");
                return;
            }

            // A copied (Ugues/Luma) Reincarnation grants ONE-SHOT copies (a temporary copy must never mint
            // permanent powers); l'Incomplet's own Reincarnation grants PERMANENT copies. Either way the grants
            // are hidden from the RoleCard (showing them leaks a real Incomplet + the copied role) via the onReady.
            // Board T26: the grants of a copied Réincarnation join no Marque budget, Ugës can use them the same night.
            bool _isCopiedReincarnation = isStolenCopy.Value;
            Action<Power> _onReady = _isCopiedReincarnation ? ConfigureOneShotGrant : ConfigurePermanentGrant;
            GameLog.Guarded(() => GameLog.Info("power", "power.reincarnation.grant", "owner", (ulong)_ownerSlot,
                "target", (ulong)_fromRoleSlot, "role", _fromRoleCharacter.role.roleName.ToString(), "copied", _isCopiedReincarnation));

            foreach (var _rolePower in _fromRoleCharacter.role.powers)
            {
                // Never copy an already-copied power (of any provenance, passive or active) -- no copy chains.
                if (_rolePower == null || _rolePower.isCopiedPower.Value)
                {
                    continue;
                }
                // Board T26: only l'Incomplet's own Réincarnation copies the whole Marque (three powers, Ugës's budget).
                // A copied one gets a single random copy instead, usable at once.
                if (_isCopiedReincarnation && _rolePower is PMarqueHurluberluges)
                {
                    PMarqueHurluberluges.GrantSingleCopyServer(characterManager, _ownerSlot, ConfigureOneShotGrant,
                        "copied-reincarnation");
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
                // Ne pas pouvoir se réincarner en son propre rôle, ni en l'Incomplet (T26 : vrai ou copie)
                var targetCharacter = characterManager.GetCharacter(ctx.targetId, false);
                return targetCharacter == null
                       || (!ownerCharacter.role.IsTheSameRole(targetCharacter.role) && !IsIncomplet(targetCharacter));
            });
        }

        private static bool IsIncomplet(Character _character) =>
            _character != null && _character.role != null && _character.role.roleID == RoleID.Incomplet;

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
                LogClickRefused(_roleClicked.ownerClientId);
                return;
            }
            ReincarnatePlayerRpc(_roleClicked.ownerClientId);
            OnUsed();
        }

        [Rpc(SendTo.Server)]
        private void ReincarnatePlayerRpc(ulong _characterClickedId, RpcParams _params = default)
        {
            if (!ServerAuthorizeEffect(_params, _characterClickedId)) return; // NET-09: server-side use authorization
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
