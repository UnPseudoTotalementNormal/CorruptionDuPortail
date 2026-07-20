#region

using System;
using UnityEngine;
using Characters.Powers.Target;
using CorruptionDuPortail.Domain.Powers;
using CorruptionDuPortail.Domain.Powers.Decisions;
using CorruptionDuPortail.Domain.Powers.State;
using RoleTarget;
using UI.BoardUI.Selection;
using Unity.Netcode;

#endregion

namespace Characters.Powers
{
    [Serializable]
    public class PChainedByTheShadows : Power, IExtraUseState, IExtraUseGrant
    {
        // Powers-POCO v2: server logic in ChainedByShadowsDecision (pure). The char+role selection flow
        // stays here; the RPC body triggers the decision. The picked role's owner is the secondary slot the
        // decision compares roles against.
        private readonly ChainedByShadowsDecision _decision = new();

        // Lot B (Abyss): once-per-night guard for the sole-anomaly extra use. Server-only — the decision
        // reads it (via IExtraUseState) and the executor sets it (via IExtraUseGrant), both server-side, so
        // no NetworkVariable is needed. Reset to false at each awakening start so the bonus is available again
        // the next night.
        private bool _bonusConsumedThisNight;

        // The owner Character we hooked for the nightly flag reset, cached so OnNetworkDespawn unsubscribes
        // from the same reference. We reset when the owner's isAwakened NetworkVariable flips to true — a
        // direct server write that fires reliably each night (reached through the injected characterManager,
        // the migrated-consumer-safe path — a migrated power must not touch GameManager.For). We deliberately
        // do NOT use Character.onCharacterAwakened: that event is dead project-wide (AwakenCharacterServerRpc
        // mistakenly calls SleepCharacterClientRpc, so it never raises) — see deferred-work.md.
        private Character _subscribedOwnerCharacter;

        bool IExtraUseState.BonusConsumedThisNight => _bonusConsumedThisNight;

        void IExtraUseGrant.GrantExtraUse()
        {
            if (!IsServer)
            {
                return;
            }
            powerUseLeft.Value += 1;
            _bonusConsumedThisNight = true;
        }

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
            targetValidator.AddRule(ctx => TargetUtils.IsTargetValid(ctx.targetId, targetIncludeFlags, ctx.targetType));
        }

        private void OnCharacterAndRolePicked(Character _character, Role _role)
        {
            if (!_character ||
                !CheckIsTargetValid(_character.ownerClientId.Value, TargetUtils.TargetType.Character) ||
                !CheckIsTargetValid(_role.ownerClientId, TargetUtils.TargetType.Role))
            {
                return;
            }
            TryCorruptCharacterServerRpc(_character.ownerClientId.Value, _role);
            OnUsed();
        }
        [Rpc(SendTo.Server)]
        private void TryCorruptCharacterServerRpc(ulong _corruptingCharacterId, Role _compareRole)
        {
            // SelfState feeds BOTH the context (the decision reads IExtraUseState) and the runtime (the
            // GrantExtraUse executor resolves IExtraUseGrant back to this power).
            var _selfState = SelfState;
            RunDecisionEffects(_decision, new PowerContext(
                ownerSlot: (int)ownerClientId.Value,
                targetSlot: (int)_corruptingCharacterId,
                secondaryTargetSlot: (int)_compareRole.ownerClientId,
                roster: Roster,
                state: _selfState), _selfState);
        }

        public override bool CanUse(bool _ignoreCurrentlyUsed = false)
        {
            bool _baseValue = base.CanUse(_ignoreCurrentlyUsed);
            if (!_baseValue)
            {
                return false;
            }
            return true;
        }

        [SerializeField] private string[] pickerStepDescriptions;

        public override void StartUse()
        {
            base.StartUse();
            selectionFlowService.StartCharacterThenRoleSelection(targetValidator, OnCharacterAndRolePicked,
                new SelectionFlowOptions { stepDescriptions = pickerStepDescriptions });
        }

        public override void Cancel()
        {
            if (!isCurrentlyUsed)
            {
                return;
            }
            base.Cancel();
        }

        protected override void StopUse()
        {
            base.StopUse();
            selectionFlowService.CancelSelection();
        }

        public override void OnGameStartedServer()
        {
            base.OnGameStartedServer();
            // Reset the once-per-night bonus flag each time the owner awakens. Idempotent: OnGameStartedServer
            // can be replayed at spawn (PowerManager.OnPowerSpawned), so drop any prior subscription first.
            UnsubscribeOwnerAwaken();
            var _owner = characterManager.GetCharacter(ownerClientId.Value, false);
            if (_owner != null)
            {
                _owner.isAwakened.OnValueChanged += OnOwnerAwakenedChanged;
                _subscribedOwnerCharacter = _owner;
            }
        }

        public override void OnNetworkDespawn()
        {
            UnsubscribeOwnerAwaken();
            base.OnNetworkDespawn();
        }

        private void UnsubscribeOwnerAwaken()
        {
            if (_subscribedOwnerCharacter != null)
            {
                _subscribedOwnerCharacter.isAwakened.OnValueChanged -= OnOwnerAwakenedChanged;
                _subscribedOwnerCharacter = null;
            }
        }

        // A new night begins for the Abyss the moment her isAwakened flips true; that is when the bonus
        // becomes available again. The flip to false (sleep) leaves the flag untouched.
        private void OnOwnerAwakenedChanged(bool _previous, bool _awakened)
        {
            if (_awakened)
            {
                _bonusConsumedThisNight = false;
            }
        }
    }
}
