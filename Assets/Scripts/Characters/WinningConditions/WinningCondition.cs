using System;
using Unity.Netcode;

namespace Characters.WinningConditions
{
    [Serializable]
    public abstract class WinningCondition : INetworkSerializable
    {
        public abstract bool CheckCondition();
        public abstract void OnWin();
        
        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            
        }
    }
}