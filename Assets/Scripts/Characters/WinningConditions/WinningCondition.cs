#region

using System;
using CorruptionDuPortail.Domain;
using Unity.Netcode;

#endregion

namespace Characters.WinningConditions
{
    [Serializable]
    public abstract class WinningCondition : INetworkSerializable
    {
        public ulong ownerClientId;

        public abstract WinningTeam GetWinningTeam();

        public abstract bool CheckCondition();

        /// <summary>
        /// Snapshot-based evaluation (Story 2.2). Default delegates to the live pull so prod behavior and cost stay
        /// byte-identical until a concrete condition overrides it to read the immutable snapshot (Stories 2.3–2.6).
        /// The snapshot is passed by argument and never stored — migrations stay independent.
        /// </summary>
        public virtual bool CheckCondition(GameSnapshot snapshot) => CheckCondition();
        
        public virtual void NetworkSerialize<T>(BufferSerializer<T> _serializer) where T : IReaderWriter
        {
            _serializer.SerializeValue(ref ownerClientId);
        }
    }
}