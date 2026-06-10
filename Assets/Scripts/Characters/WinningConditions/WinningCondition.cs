#region

using System;
using CorruptionDuPortail.Domain;
using Unity.Netcode;

#endregion

namespace Characters.WinningConditions
{
    [Serializable]
    public abstract class WinningCondition : INetworkSerializable, IWinningCondition
    {
        public ulong ownerClientId;

        public abstract WinningTeam GetWinningTeam();

        /// <summary>
        /// LEGACY live pull (Story 2.7): no longer the production path — the victory loop evaluates off the
        /// snapshot via <see cref="CheckCondition(GameSnapshot)"/>. Retained ONLY as the differential oracle until
        /// Story 2.7b swaps the oracle to the frozen Epic-1 goldens, after which it is removed. Not feature-shipped.
        /// </summary>
        public abstract bool CheckCondition();

        /// <summary>
        /// Snapshot-based evaluation (Story 2.2; overridden per condition in 2.3–2.6). This is the PRODUCTION path
        /// as of Story 2.7. The snapshot is passed by argument and never stored — conditions stay independent.
        /// </summary>
        public virtual bool CheckCondition(GameSnapshot snapshot) => CheckCondition();
        
        public virtual void NetworkSerialize<T>(BufferSerializer<T> _serializer) where T : IReaderWriter
        {
            _serializer.SerializeValue(ref ownerClientId);
        }
    }
}