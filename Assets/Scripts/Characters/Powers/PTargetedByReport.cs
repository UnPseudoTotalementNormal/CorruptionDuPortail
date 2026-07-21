using System;
using System.Collections.Generic;
using System.Linq;
using CorruptionDuPortail.Domain.Powers;
using CorruptionDuPortail.Domain.Powers.Decisions;
using CorruptionDuPortail.Domain.Powers.State;
using Unity.Netcode;

namespace Characters.Powers
{
    [Serializable]
    public class PTargetedByReport : Power, ITargetedByReport
    {
        // Orpheline passive (spec Lot C.2). Shape mirrors PClandestineObservation (pure decision + report
        // port implemented by this adapter). Trigger is PER-TARGETING (RoleTargetSystem.onTargetingAddedServer):
        // the moment a role targets her, its icon lands on that role's thumbnail — no waiting for end of night.
        // This inherently captures late-awakening roles (Traqueuse/Robot/Croupière) too, since each targeting
        // fires when it happens whatever the layer order. Delivery is icon-only (private player-icon channel).
        private readonly TargetedByReportDecision _decision = new();

        // Whether we are currently subscribed to the targeting event, so OnGameStartedServer's idempotent
        // re-entry (PowerManager.OnPowerSpawned replay) never doubles the handler, and OnNetworkDespawn
        // unsubscribes exactly once. roleTargetSystem is a shared long-lived object: a leaked handler would
        // fire on a dead power.
        private bool _subscribedToTargeting;

        // Deduplicated upstream: GetAllTargetersForTarget returns a HashSet, so no targeter appears twice.
        IReadOnlyList<int> ITargetedByReport.TargeterSlots =>
            roleTargetSystem.GetAllTargetersForTarget(ownerClientId.Value).Select(_id => (int)_id).ToList();

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
            // Opaque icon identity: the receiving client resolves this NetworkObjectId back to this power's
            // bar sprite (same pattern as PCursedVision feeding its CardEffectId).
            _decision.IconId = NetworkObjectId;
        }

        public override void OnGameStartedServer()
        {
            base.OnGameStartedServer();
            SubscribeToTargeting();
        }

        public override void OnNetworkDespawn()
        {
            UnsubscribeFromTargeting();
            base.OnNetworkDespawn();
        }

        private void SubscribeToTargeting()
        {
            if (_subscribedToTargeting || roleTargetSystem == null)
            {
                return;
            }
            roleTargetSystem.onTargetingAddedServer += OnTargetingAddedServer;
            _subscribedToTargeting = true;
        }

        private void UnsubscribeFromTargeting()
        {
            if (!_subscribedToTargeting || roleTargetSystem == null)
            {
                return;
            }
            roleTargetSystem.onTargetingAddedServer -= OnTargetingAddedServer;
            _subscribedToTargeting = false;
        }

        private void OnTargetingAddedServer(RoleTarget.TargetingData _data)
        {
            // Only react when SHE is the one being targeted; every other targeting in the game is ignored.
            if (_data.targetId != ownerClientId.Value)
            {
                return;
            }
            // Re-run the whole decision: it reads every current targeter and emits one AddPlayerIcon each;
            // PlayerIconManager.AddIcon dedups, so re-emitting already-placed icons is a harmless no-op and
            // only the new targeter's icon is actually added.
            var _selfState = SelfState;
            RunDecisionEffects(_decision,
                new PowerContext(ownerSlot: (int)ownerClientId.Value, state: _selfState), _selfState);
        }
    }
}
