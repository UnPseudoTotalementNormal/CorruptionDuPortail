#region

using System.Collections.Generic;
using CorruptionDuPortail.Domain;
using CorruptionDuPortail.Domain.Powers;
using CorruptionDuPortail.Domain.Powers.Decisions;
using CorruptionDuPortail.Domain.Powers.State;
using GameLogic;
using Unity.Netcode;
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

        // Seed seam (story d'archi, catalogue 246-252): the random distinct-pick goes through an INJECTABLE
        // provider instead of a hard-wired new UnityRandomProvider(). Prod default = UnityRandomProvider; tests
        // seed it (SeededRandomProvider) via reflection to make the boundary pick deterministic. This runs on the
        // SERVER only (OnGameStartedServer / IsServer guard), so there is no client-side draw that could diverge.
        private IRandomProvider _randomProvider = new UnityRandomProvider();

        // OnGameStartedServer can be reached twice for a late-spawned power (OnPowerSpawned + OnGameStarted);
        // steal exactly once.
        private bool _hasStolen;

        // Shared budget of the copies this Marque granted (each copy points back here through
        // Power.marqueSourceId): locked the night of the theft, unlocked at each new day, locked again as soon as
        // one copy is used — "à partir du second tour, une fois par nuit". Replicated so every peer's CanUse
        // (power bar, bots) agrees with the server.
        public NetworkVariable<bool> copiesLocked = new();

        public bool CopiesLocked => copiesLocked.Value;

        private IGameLoop _gameLoop;

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
            if (IsServer)
            {
                _gameLoop = CompositionRoot.For(NetworkManager).GameLoop;
                if (_gameLoop != null)
                {
                    _gameLoop.onNewDayPassed += UnlockCopiesServer;
                }
            }
        }

        public override void OnNetworkDespawn()
        {
            if (IsServer && _gameLoop != null)
            {
                _gameLoop.onNewDayPassed -= UnlockCopiesServer;
            }
            base.OnNetworkDespawn();
        }

        private void UnlockCopiesServer()
        {
            if (IsServer && IsSpawned)
            {
                copiesLocked.Value = false;
            }
        }

        /// <summary>Server: one of this Marque's copies was just used — the others wait for the next night.</summary>
        public void OnCopyUsedServer()
        {
            if (IsServer)
            {
                copiesLocked.Value = true;
            }
        }

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
            List<int> _picks = StolenPowerSelector.SelectStealable(_candidates, POWERS_TO_STEAL, _randomProvider);
            if (DevStealPreference != null && DevStealPreference.Length > 0)
            {
                _picks = PreferForDev(_candidates, _powers, _picks);
            }
            if (_picks.Count == 0)
            {
                Debug.Log("[UGUES] Marque d'Hurluberluges: no eligible chosen active power to steal.");
                return;
            }
            // Not usable the night of the theft (game start = night 1 for Ugës; the night of the copy for an
            // Incomplet who took the Marque): the next onNewDayPassed unlocks them.
            copiesLocked.Value = true;
            foreach (int _index in _picks)
            {
                // onReady = shared one-shot config (Power.ConfigureAsOneShotStolenCopy): spent copies despawn.
                characterManager.GivePowerToCharacter((ulong)_ownerSlot, _powers[_index], ConfigureMarqueCopy);
            }
        }

        /// <summary>
        /// Dev seam (autoplay only, null otherwise): power-name fragments stolen FIRST when an eligible power matches
        /// (in this order), the rest of the draw is kept. Lets a test cover a given stolen power without re-rolling.
        /// </summary>
        public static string[] DevStealPreference;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetDevSeams() => DevStealPreference = null;

        private static List<int> PreferForDev(List<PowerCandidate> _candidates, List<Power> _powers, List<int> _drawn)
        {
            var _picks = new List<int>();
            foreach (string _fragment in DevStealPreference)
            {
                for (int _i = 0; _i < _candidates.Count; _i++)
                {
                    if (_candidates[_i].IsEligible && !_picks.Contains(_i) &&
                        _powers[_i].powerName.ToString().IndexOf(_fragment, System.StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        _picks.Add(_i);
                        break;
                    }
                }
            }
            foreach (int _index in _drawn)
            {
                if (!_picks.Contains(_index))
                {
                    _picks.Add(_index);
                }
            }
            if (_picks.Count > POWERS_TO_STEAL)
            {
                _picks.RemoveRange(POWERS_TO_STEAL, _picks.Count - POWERS_TO_STEAL);
            }
            Debug.Log("[UGUES] dev steal preference: " + string.Join(", ", _picks.ConvertAll(_i => _powers[_i].powerName.ToString())));
            return _picks;
        }

        // Server, onReady of each stolen copy: one-shot copy, tied to this Marque's per-night budget.
        private void ConfigureMarqueCopy(Power _copy)
        {
            Power.ConfigureAsOneShotStolenCopy(_copy);
            if (_copy != null && _copy.IsServer && IsSpawned)
            {
                _copy.marqueSourceId.Value = NetworkObjectId;
            }
        }
    }
}
