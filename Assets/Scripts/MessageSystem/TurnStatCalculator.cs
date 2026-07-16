using System.Collections.Generic;
using Characters;

namespace MessageSystem
{
    // Pure per-turn stat computation (no NGO, no MonoBehaviour) — the testable seam behind the journal
    // recorder in MessageManager. Consolidates the live counts today duplicated in CorruptionBoardInfo
    // (corrupted non-anomaly reals) and RobotBoardInfo (Robot targeter count).
    public static class TurnStatCalculator
    {
        public static TurnStat Compute(
            int _day,
            IEnumerable<CharacterFactionState> _characters,
            bool _hasRobot,
            int _robotTargeterCount)
        {
            int _nonAnomalyTotal = 0;
            int _corruptedCount = 0;
            foreach (CharacterFactionState _character in _characters)
            {
                if (_character.isFake || _character.faction == FactionType.anomaly)
                {
                    continue;
                }
                _nonAnomalyTotal += 1;
                if (_character.isCorrupted)
                {
                    _corruptedCount += 1;
                }
            }

            int _robotTargetCount = _hasRobot ? _robotTargeterCount : -1;
            return new TurnStat(_day, _corruptedCount, _nonAnomalyTotal, _robotTargetCount);
        }

        // NetworkList replays a same-tick-added entry twice for late joiners (project has no upstream fix;
        // cf. the CharacterManager [CHARLIST] guard). A turn is recorded exactly once, so day is a unique
        // key: last-wins dedup by day lets the journal read the replicated list defensively.
        public static List<TurnStat> DedupByDay(IEnumerable<TurnStat> _stats)
        {
            List<TurnStat> _result = new();
            Dictionary<int, int> _indexByDay = new();
            foreach (TurnStat _stat in _stats)
            {
                if (_indexByDay.TryGetValue(_stat.day, out int _existingIndex))
                {
                    _result[_existingIndex] = _stat;
                }
                else
                {
                    _indexByDay[_stat.day] = _result.Count;
                    _result.Add(_stat);
                }
            }
            return _result;
        }
    }

    // Minimal projection of a Character for the pure calculator (keeps NGO/MonoBehaviour out of the seam).
    public readonly struct CharacterFactionState
    {
        public readonly FactionType faction;
        public readonly bool isFake;
        public readonly bool isCorrupted;

        public CharacterFactionState(FactionType _faction, bool _isFake, bool _isCorrupted)
        {
            faction = _faction;
            isFake = _isFake;
            isCorrupted = _isCorrupted;
        }
    }
}
