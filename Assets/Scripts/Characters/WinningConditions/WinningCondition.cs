using System;
using Unity.Netcode;

namespace Characters.WinningConditions
{
    [Serializable]
    public abstract class WinningCondition : INetworkSerializable
    {
        public ulong ownerClientId;
        
        public abstract WinningTeam GetWinningTeam();
        
        public abstract bool CheckCondition();
        
        public virtual void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            
        }
    }
}