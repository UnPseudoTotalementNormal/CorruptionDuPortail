#region

using System;
using System.Collections.Generic;
using CorruptionDuPortail.Domain;
using CorruptionDuPortail.Domain.Powers;
using CorruptionDuPortail.Domain.Powers.Decisions;
using CorruptionDuPortail.Domain.Powers.State;
using Diagnostics.Logs;
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
                if (copiesLocked.Value)
                {
                    GameLog.Info("power", "power.uges.copies.unlock", "owner", ownerClientId.Value);
                }
                copiesLocked.Value = false;
            }
        }

        /// <summary>Server: one of this Marque's copies was just used — the others wait for the next night.</summary>
        public void OnCopyUsedServer()
        {
            if (IsServer)
            {
                GameLog.Info("power", "power.uges.copies.lock", "owner", ownerClientId.Value, "reason", "copy-used");
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
                GameLog.Warn("power", "power.uges.steal", "thief", (ulong)_ownerSlot, "count", 0, "reason", "no-owner");
                return;
            }

            List<Power> _stolen = PickCopiableServer(characterManager, _ownerSlot, POWERS_TO_STEAL, _randomProvider, out int _candidateCount);
            if (_stolen.Count == 0)
            {
                Debug.Log("[UGUES] Marque d'Hurluberluges: no eligible chosen active power to steal.");
                GameLog.Info("power", "power.uges.steal", "thief", (ulong)_ownerSlot, "count", 0, "candidates", _candidateCount,
                    "reason", "no-eligible-power");
                return;
            }
            GameLog.Guarded(() => GameLog.Info("power", "power.uges.steal", "thief", (ulong)_ownerSlot, "count", _stolen.Count,
                "powers", _stolen.ConvertAll(_p => _p.powerName.ToString()), "from", _stolen.ConvertAll(_p => _p.ownerClientId.Value),
                "devPreference", DevStealPreference != null && DevStealPreference.Length > 0));
            // Not usable the night of the theft (game start = night 1 for Ugës; the night of the copy for an
            // Incomplet who took the Marque): the next onNewDayPassed unlocks them.
            GameLog.Info("power", "power.uges.copies.lock", "owner", (ulong)_ownerSlot, "reason", "theft-night");
            copiesLocked.Value = true;
            foreach (Power _power in _stolen)
            {
                // onReady = shared one-shot config (Power.ConfigureAsOneShotStolenCopy): spent copies despawn.
                characterManager.GivePowerToCharacter((ulong)_ownerSlot, _power, ConfigureMarqueCopy);
            }
        }

        /// <summary>
        /// Server, board T26: a Marque copied by anything but l'Incomplet's own Réincarnation (Luma's Mélange on a factice
        /// Ugës, a copied Réincarnation on Ugës) gives ONE random copy instead of three, drawn like Ugës's (active powers of
        /// the élus in the game, real or factice, never the thief's own nor a copy). The copy is configured by
        /// <paramref name="_onReady"/> and joins no Marque budget: usable at once. Returns its name, null when nothing is
        /// eligible.
        /// </summary>
        public static string GrantSingleCopyServer(CharacterManager _characterManager, int _thiefSlot, Action<Power> _onReady,
            string _via)
        {
            List<Power> _picked = PickCopiableServer(_characterManager, _thiefSlot, 1, SingleCopyRandom, out int _candidateCount);
            if (_picked.Count == 0)
            {
                GameLog.Info("power", "power.uges.single", "thief", (ulong)_thiefSlot, "via", _via, "count", 0,
                    "candidates", _candidateCount, "reason", "no-eligible-power");
                return null;
            }
            Power _power = _picked[0];
            GameLog.Guarded(() => GameLog.Info("power", "power.uges.single", "thief", (ulong)_thiefSlot, "via", _via, "count", 1,
                "power", _power.powerName.ToString(), "from", _power.ownerClientId.Value));
            _characterManager.GivePowerToCharacter((ulong)_thiefSlot, _power, _onReady);
            return _power.powerName.ToString();
        }

        // Seed seam for the single copy (static: the copy comes from a Réincarnation / Mélange, not from a Marque instance).
        private static IRandomProvider SingleCopyRandom = new UnityRandomProvider();

        // Walk the live roster, build one engine-free descriptor per live power (across every present character, real or
        // factice) in a list parallel to _powers, so the pure kernel owns the eligibility rule (chosen faction, not the
        // thief, active, not an already-copied power): that filter is EditMode-tested, this loop is not.
        private static List<Power> PickCopiableServer(CharacterManager _characterManager, int _thiefSlot, int _count,
            IRandomProvider _random, out int _candidateCount)
        {
            var _powers = new List<Power>();
            var _candidates = new List<PowerCandidate>();
            foreach (Character _c in _characterManager.GetCharacters(false))
            {
                if (_c == null || _c.role == null)
                {
                    continue;
                }
                bool _ownerIsChosen = _c.role.factionType == FactionType.chosen;
                bool _ownerIsThief = (int)_c.ownerClientId.Value == _thiefSlot;
                foreach (Power _p in _c.role.powers)
                {
                    if (_p == null)
                    {
                        continue;
                    }
                    _powers.Add(_p);
                    _candidates.Add(new PowerCandidate(_ownerIsChosen, _ownerIsThief, _p.BaseIsPassive, _p.isCopiedPower.Value));
                }
            }
            _candidateCount = _candidates.Count;

            // Deterministic-source draw (mirrors RoleAttributionState's UnityRandomProvider); the filter +
            // distinct-pick + cap-at-what-exists mechanic lives in the EditMode-tested Domain kernel.
            List<int> _picks = StolenPowerSelector.SelectStealable(_candidates, _count, _random);
            if (DevStealPreference != null && DevStealPreference.Length > 0)
            {
                _picks = PreferForDev(_candidates, _powers, _picks, _count);
            }
            return _picks.ConvertAll(_index => _powers[_index]);
        }

        /// <summary>
        /// Dev seam (autoplay only, null otherwise): power-name fragments stolen FIRST when an eligible power matches
        /// (in this order), the rest of the draw is kept. Lets a test cover a given stolen power without re-rolling.
        /// </summary>
        public static string[] DevStealPreference;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetDevSeams()
        {
            DevStealPreference = null;
            SingleCopyRandom = new UnityRandomProvider();
        }

        private static List<int> PreferForDev(List<PowerCandidate> _candidates, List<Power> _powers, List<int> _drawn, int _count)
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
            if (_picks.Count > _count)
            {
                _picks.RemoveRange(_count, _picks.Count - _count);
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
