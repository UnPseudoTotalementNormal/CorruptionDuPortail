using System;
using System.Collections.Generic;
using GameLogic;
using GameLogic.GameStates;
using Unity.Netcode;

namespace RoleTarget
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

        private void Start()
        {
            foreach (var _awakeningState in GameManager.instance.GetGameStates(typeof(AwakeningState)))
            {
                _awakeningState.onStateStartClient += ResetTargetingData;
            }
        }

        private void ResetTargetingData()
        {
            currentTargetingDataList.Clear();
        }

        public void NewTargeting(TargetingData _targetingData)
        {
            NewTargetingRpc(_targetingData);
        }

        public void NewTargeting(ulong _targeterId, ulong _targetId)
        {
            NewTargetingRpc(new TargetingData(_targeterId, _targetId));
        }

        [Rpc(SendTo.Server)]
        private void NewTargetingRpc(TargetingData _targetingData)
        {
            ReceiveTargetingDataRpc(_targetingData);
        }
        
        [Rpc(SendTo.Everyone)]
        private void ReceiveTargetingDataRpc(TargetingData _targetingData)
        {
            currentTargetingDataList.Add(_targetingData);
        }
        
        public List<TargetingData> GetAllTargetingDataForTarget(ulong _targetId)
        {
            List<TargetingData> _targetingDataList = new();
            foreach (var _targetingData in currentTargetingDataList)
            {
                if (_targetingData.targetId == _targetId)
                {
                    _targetingDataList.Add(_targetingData);
                }
            }
            return _targetingDataList;
        }
        
        public List<TargetingData> GetAllTargetingDataForTargeter(ulong _targeterId)
        {
            List<TargetingData> _targetingDataList = new();
            foreach (var _targetingData in currentTargetingDataList)
            {
                if (_targetingData.targeterId == _targeterId)
                {
                    _targetingDataList.Add(_targetingData);
                }
            }
            return _targetingDataList;
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
