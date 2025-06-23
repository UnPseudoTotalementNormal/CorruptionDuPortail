using System;
using System.Collections.Generic;
using Unity.Netcode;

namespace RoleTargetSystem
{
    public class RoleTargetSystem : NetworkBehaviour
    {
        public static RoleTargetSystem instance;
        
        public List<TargetingData> currentTargetingDataList = new();

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
            instance = this;
        }
        
        public void NewTargeting(ulong _targeterId, ulong _targetId)
        {
            NewTargetingRpc(_targeterId, _targetId);
        }

        [Rpc(SendTo.Server)]
        private void NewTargetingRpc(ulong _targeterId, ulong _targetId)
        {
            ReceiveTargetingDataRpc(new TargetingData(_targeterId, _targetId));
        }
        
        [Rpc(SendTo.ClientsAndHost)]
        private void ReceiveTargetingDataRpc(TargetingData _targetingData)
        {
            currentTargetingDataList.Add(_targetingData);
        }
    }

    [Serializable]
    public class TargetingData : INetworkSerializable
    {
        public ulong targeterId;
        public ulong targetId;

        public TargetingData() { }
        
        public TargetingData(ulong _targeterId, ulong _targetId)
        {
            targeterId = _targeterId;
            targetId = _targetId;
        }

        public void NetworkSerialize<T>(BufferSerializer<T> _serializer) where T : IReaderWriter
        {
            _serializer.SerializeValue(ref targeterId);
            _serializer.SerializeValue(ref targetId);
        }
    }
}
