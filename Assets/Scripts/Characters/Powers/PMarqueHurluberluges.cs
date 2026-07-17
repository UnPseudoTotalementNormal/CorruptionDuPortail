#region

using System.Collections.Generic;
using CorruptionDuPortail.Domain;
using CorruptionDuPortail.Domain.Powers;
using CorruptionDuPortail.Domain.Powers.Decisions;
using CorruptionDuPortail.Domain.Powers.State;
using GameLogic;
using UnityEngine;

#endregion

namespace Characters.Powers
{
    /// <summary>
    /// Ugues' signature power — "Marque d'Hurluberluges" (story role-ugues-marque-hurluberluges).
    ///
    /// At game start Ugues copies <see cref="POWERS_TO_STEAL"/> random ACTIVE (non-passive) powers drawn from
    /// the CHOSEN-faction roles present in the game (real players AND factice decoys, but never Ugues himself).
    /// Each copy is a one-shot: it appears in Ugues' power bar, is usable once as if it were his own, then is
    /// spent for good ("une fois utilisé, le pouvoir est perdu"). The original owners keep their powers — this
    /// steals a COPY, not the instance.
    ///
    /// Powers-POCO v2: this power is passive; the "grant Ugues his stolen copies" decision is
    /// <see cref="MarqueHurluberlugesDecision"/> (pure), which emits a single GrantStolenPowers intention. The
    /// engine-coupled work — walking the LIVE roster, filtering + the random-distinct pick, and the
    /// spawn/reparent (GivePowerToCharacter) — is power-local, so the carrier realises it via
    /// <see cref="IStolenPowerGrant"/> (same shape as PLegacy/ILegacyGrant). The pure pick mechanic itself
    /// lives in the EditMode-tested <see cref="StolenPowerSelector"/>.
    ///
    /// The steal runs inside <see cref="OnGameStartedServer"/>, which fires from PowerManager.OnGameStarted —
    /// a point where every character's <c>role.powers</c> is already populated (that method iterates them), so
    /// the eligible pool is complete and there is no attribution race.
    /// </summary>
    public class PMarqueHurluberluges : Power, IStolenPowerGrant
    {
        private const int POWERS_TO_STEAL = 3;

        private readonly MarqueHurluberlugesDecision _decision = new();

        // OnGameStartedServer can be reached twice for a late-spawned power (OnPowerSpawned + OnGameStarted);
        // steal exactly once.
        private bool _hasStolen;

        public override void OnGameStartedServer()
        {
            base.OnGameStartedServer();
            if (!IsServer)
            {
                return;
            }
            if (_hasStolen)
            {
                return;
            }
            _hasStolen = true;
            RunDecisionEffects(_decision, new PowerContext(ownerSlot: (int)ownerClientId.Value), SelfState);
        }

        // Power-local grant port (IStolenPowerGrant), reached from GrantStolenPowersExecutor via SelfState.
        // Holds the engine-coupled theft: walk the live roster, build a candidate per live power, let the pure
        // StolenPowerSelector filter + pick, then give one-shot copies to Ugues.
        void IStolenPowerGrant.GrantStolen(int _ownerSlot)
        {
            if (!IsServer)
            {
                return;
            }
            if (ownerCharacter == null)
            {
                Debug.LogWarning("[UGUES] Marque d'Hurluberluges: owner character unresolved, nothing stolen.");
                return;
            }

            // Build one engine-free descriptor per live power (across every present character, real or factice)
            // in a list parallel to _powers, so the pure kernel owns the eligibility rule (chosen faction, not
            // Ugues, active, not an already-stolen copy) — that filter is EditMode-tested, this loop is not.
            var _powers = new List<Power>();
            var _candidates = new List<PowerCandidate>();
            foreach (Character _c in characterManager.GetCharacters(false))
            {
                if (_c == null || _c.role == null)
                {
                    continue;
                }
                bool _ownerIsChosen = _c.role.factionType == FactionType.chosen;
                bool _ownerIsUgues = (int)_c.ownerClientId.Value == _ownerSlot;
                foreach (Power _p in _c.role.powers)
                {
                    if (_p == null)
                    {
                        continue;
                    }
                    _powers.Add(_p);
                    _candidates.Add(new PowerCandidate(_ownerIsChosen, _ownerIsUgues, _p.BaseIsPassive, _p.isCopiedPower.Value));
                }
            }

            // Deterministic-source draw (mirrors RoleAttributionState's UnityRandomProvider); the filter +
            // distinct-pick + cap-at-what-exists mechanic lives in the EditMode-tested Domain kernel.
            List<int> _picks = StolenPowerSelector.SelectStealable(_candidates, POWERS_TO_STEAL, new UnityRandomProvider());
            if (_picks.Count == 0)
            {
                Debug.Log("[UGUES] Marque d'Hurluberluges: no eligible chosen active power to steal.");
                return;
            }
            foreach (int _index in _picks)
            {
                // onReady = shared one-shot config (Power.ConfigureAsOneShotStolenCopy): spent copies despawn.
                characterManager.GivePowerToCharacter((ulong)_ownerSlot, _powers[_index], Power.ConfigureAsOneShotStolenCopy);
            }
        }
    }
}
