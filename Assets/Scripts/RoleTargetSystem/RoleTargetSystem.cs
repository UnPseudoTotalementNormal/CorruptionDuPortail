using System;
using System.Collections.Generic;
using GameLogic;
using GameLogic.GameStates;
using Unity.Netcode;

namespace RoleTarget
{
    public class RoleTargetSystem : NetworkBehaviour
    {
        // Story 10.2 (Epic 10 / D4): the gameplay consumers (targeting powers + RobotBoardInfo) were
        // rerouted off this global onto an injected roleTargetSystem field, resolved through
        // CompositionRoot. The static now backs ONLY one recorded-caller exception: the CompositionRoot
        // targeting accessor (the one sanctioned locator, since RoleTargetSystem is not de-singletonised).
        // recorded §4 census survivor (12.3 strategy B), whitelisted in StaticSingletonCensusGuardTests
        public static RoleTargetSystem instance;
        
        public List<TargetingData> currentTargetingDataList = new();

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();

            if (instance != null && instance != this)
            {
                return;
            }
            instance = this;
        }

        public override void OnNetworkDespawn()
        {
            if (instance == this)
            {
                instance = null;
            }

            base.OnNetworkDespawn();
        }

        private void Start()
        {
            foreach (var _awakeningState in GameManager.For(NetworkManager).GetGameStates(typeof(AwakeningState)))
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
        
        public HashSet<ulong> GetAllTargetersForTarget(ulong _targetId)
        {
            HashSet<ulong> _targeterIdList = new();
            foreach (var _targetingData in currentTargetingDataList)
            {
                if (_targetingData.targetId == _targetId)
                {
                    _targeterIdList.Add(_targetingData.targeterId);
                }
            }
            return _targeterIdList;
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
