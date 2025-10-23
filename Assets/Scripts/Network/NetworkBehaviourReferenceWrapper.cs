using System;
using Unity.Netcode;

namespace Network
{
    [Serializable]
    public struct NetworkBehaviourReferenceWrapper : INetworkSerializable, IEquatable<NetworkBehaviourReferenceWrapper>
    {
        private ulong networkObjectId;
        private ushort networkBehaviourId;
        
        const ulong NULL_NETWORK_OBJECT_ID = ulong.MaxValue;
        const ushort NULL_NETWORK_BEHAVIOUR_ID = ushort.MaxValue;

        public NetworkBehaviourReferenceWrapper(NetworkBehaviour _behaviour)
        {
            if (_behaviour == null)
            {
                networkBehaviourId = NULL_NETWORK_BEHAVIOUR_ID;
                networkObjectId = NULL_NETWORK_OBJECT_ID;
                return;
            }
            
            networkObjectId = _behaviour.NetworkObjectId;
            networkBehaviourId = _behaviour.NetworkBehaviourId;
        }

        public bool TryGet<T>(out T _behaviour) where T : NetworkBehaviour
        {
            _behaviour = null;
            if (networkBehaviourId == NULL_NETWORK_BEHAVIOUR_ID || networkObjectId == NULL_NETWORK_OBJECT_ID)
            {
                return false;
            }
            
            if (NetworkManager.Singleton.SpawnManager.SpawnedObjects.TryGetValue(networkObjectId, out NetworkObject _networkObject))
            {
                _behaviour = _networkObject.GetNetworkBehaviourAtOrderIndex(networkBehaviourId) as T;
                return _behaviour != null;
            }
            return false;
        }

        public void NetworkSerialize<T>(BufferSerializer<T> _serializer) where T : IReaderWriter
        {
            _serializer.SerializeValue(ref networkObjectId);
            _serializer.SerializeValue(ref networkBehaviourId);
        }

        public bool Equals(NetworkBehaviourReferenceWrapper _other)
        {
            return networkObjectId == _other.networkObjectId && networkBehaviourId == _other.networkBehaviourId;
        }

        public override bool Equals(object _obj)
        {
            return _obj is NetworkBehaviourReferenceWrapper _other && Equals(_other);
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(networkObjectId, networkBehaviourId);
        }
    }
}