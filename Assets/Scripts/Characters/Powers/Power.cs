using Unity.Netcode;
using UnityEngine;

namespace Characters.Powers
{
    //[CreateAssetMenu(fileName = "NewPower", menuName = "Characters/Power")]
    public abstract class Power : ScriptableObject, INetworkSerializable
    {
        public float maxWaitTime;
        
        public abstract bool CanUse();
        public abstract void Use();
        
        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref maxWaitTime);
        }
    }
}