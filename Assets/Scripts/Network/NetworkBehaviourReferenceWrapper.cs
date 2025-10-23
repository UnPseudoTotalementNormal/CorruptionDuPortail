/*
using System;
using Unity.Netcode;

namespace Networking
{
    [Serializable]
    public struct NetworkBehaviourReferenceWrapper : INetworkSerializable, IEquatable<NetworkBehaviourReferenceWrapper>
    {
        private ulong networkObjectId;
        private ushort networkBehaviourId;
        
        const ulong NULL_NETWORK_OBJECT_ID = ulong.MaxValue;
        const ushort NULL_NETWORK_BEHAVIOUR_ID = ushort.MaxValue;

        public NetworkBehaviourReferenceWrapper(NetworkBehaviour behaviour)
        {
            if (behaviour == null)
            {
                networkBehaviourId = NULL_NETWORK_BEHAVIOUR_ID;
                networkObjectId = NULL_NETWORK_OBJECT_ID;
                return;
            }
            
            networkObjectId = behaviour.NetworkObjectId;
            networkBehaviourId = behaviour.NetworkBehaviourId;
        }

        public bool TryGet<T>(out T behaviour) where T : NetworkBehaviour
        {
            behaviour = null;
            if (networkBehaviourId == NULL_NETWORK_BEHAVIOUR_ID || networkObjectId == NULL_NETWORK_OBJECT_ID)
            {
                return false;
            }
            
            if (NetworkManager.Singleton.SpawnManager.SpawnedObjects.TryGetValue(networkObjectId, out NetworkObject networkObject))
            {
                behaviour = networkObject.GetNetworkBehaviourAtOrderIndex(networkBehaviourId) as T;
                return behaviour != null;
            }
            return false;
        }

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref networkObjectId);
            serializer.SerializeValue(ref networkBehaviourId);
        }

        public bool Equals(NetworkBehaviourReferenceWrapper other)
        {
            return networkObjectId == other.networkObjectId && networkBehaviourId == other.networkBehaviourId;
        }

        public override bool Equals(object obj)
        {
            return obj is NetworkBehaviourReferenceWrapper other && Equals(other);
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(networkObjectId, networkBehaviourId);
        }
    }

}
*/