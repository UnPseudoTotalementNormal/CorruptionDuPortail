using System;
using System.Collections.Generic;
using System.Linq;
using CorruptionDuPortail.Domain.Powers;
using CorruptionDuPortail.Domain.Powers.Decisions;
using CorruptionDuPortail.Domain.Powers.State;
using GameLogic;
using GameLogic.GameStates;
using Unity.Netcode;
using UnityEngine;

namespace Characters.Powers
{
    [Serializable]
    public class PTargetedByReport : Power, ITargetedByReport
    {
        // Orpheline passive (spec Lot C.2). Shape mirrors PClandestineObservation (pure decision + report
        // port implemented by this adapter); the end-of-night trigger is borrowed from PDroolyHealing
        // (AwakeningState.onStateEndServer) so late-awakening roles (Traqueuse/Robot/Croupière) that target
        // her AFTER her own awakening layer are still captured. Delivery is icon-only (private player-icon
        // channel) — no chat.
        private readonly TargetedByReportDecision _decision = new();

        // The exact AwakeningState references we subscribed to, cached so OnNetworkDespawn unsubscribes from
        // the SAME objects. GameState is a persistent ScriptableObject: a leaked handler outlives this power
        // and would fire on a dead instance. PDroolyHealing omits this cleanup — deliberately not copied.
        private readonly List<GameState> _subscribedAwakeningStates = new();

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

            // Idempotent: OnGameStartedServer can be replayed at spawn when the game has already started
            // (PowerManager.OnPowerSpawned). Drop any prior subscriptions before re-subscribing so a replay
            // never doubles the handler.
            UnsubscribeAll();

            var _gameManager = GameManager.For(NetworkManager);
            foreach (var _awakeningState in _gameManager.GetGameStates(typeof(AwakeningState)))
            {
                _awakeningState.onStateEndServer += OnNightEndedServer;
                _subscribedAwakeningStates.Add(_awakeningState);
            }
        }

        public override void OnNetworkDespawn()
        {
            UnsubscribeAll();
            base.OnNetworkDespawn();
        }

        private void UnsubscribeAll()
        {
            foreach (var _awakeningState in _subscribedAwakeningStates)
            {
                _awakeningState.onStateEndServer -= OnNightEndedServer;
            }
            _subscribedAwakeningStates.Clear();
        }

        private void OnNightEndedServer()
        {
            // State goes into BOTH the context (the decision READS ctx.State<ITargetedByReport>(), which
            // resolves back to this power) and the runtime. RunDecisionEffects is server-guarded.
            var _selfState = SelfState;
            RunDecisionEffects(_decision,
                new PowerContext(ownerSlot: (int)ownerClientId.Value, state: _selfState), _selfState);
        }
    }
}
