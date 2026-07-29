using System;
using Unity.Netcode;

namespace MessageSystem
{
    // Per-turn aggregate recorded server-side at each awakening's end, replicated for the message journal.
    // corruptedCount/nonAnomalyTotal feed the "n/total corrompu(s)" line; robotTargetCount feeds
    // "n joueurs ont ciblé le Robot" (robotTargetCount < 0 == no Robot in the game → clause omitted).
    [Serializable]
    public struct TurnStat : INetworkSerializable, IEquatable<TurnStat>
    {
        public int day;
        public int corruptedCount;
        public int nonAnomalyTotal;
        public int robotTargetCount;

        public TurnStat(int _day, int _corruptedCount, int _nonAnomalyTotal, int _robotTargetCount)
        {
            day = _day;
            corruptedCount = _corruptedCount;
            nonAnomalyTotal = _nonAnomalyTotal;
            robotTargetCount = _robotTargetCount;
        }

        public bool hasRobot => robotTargetCount >= 0;

        public void NetworkSerialize<T>(BufferSerializer<T> _serializer) where T : IReaderWriter
        {
            _serializer.SerializeValue(ref day);
            _serializer.SerializeValue(ref corruptedCount);
            _serializer.SerializeValue(ref nonAnomalyTotal);
            _serializer.SerializeValue(ref robotTargetCount);
        }

        public bool Equals(TurnStat _other)
        {
            return day == _other.day
                && corruptedCount == _other.corruptedCount
                && nonAnomalyTotal == _other.nonAnomalyTotal
                && robotTargetCount == _other.robotTargetCount;
        }
    }
}
