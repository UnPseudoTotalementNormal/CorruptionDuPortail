using System.Collections.Generic;
using System.Linq;
using Characters;
using GameLogic;
using GameLogic.GameStates;
using RoleTarget;
using TMPro;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

namespace Board.UI
{
    public class RobotBoardInfo : NetworkBehaviour
    {
        [SerializeField] private TMP_Text text;
        
        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
            if (!IsServer)
            {
                return;
            }
            
            GameState[] _awakeningStates = GameManager.instance.GetGameStates(typeof(AwakeningState));
            foreach (var _awakeningState in _awakeningStates)
            {
                _awakeningState.onStateEndServer += OnAwakeningStateEnd;
            }
            GameManager.instance.onGameStarted += OnGameStarted;
        }

        private void OnGameStarted()
        {
            WriteNewTextRpc("0");
        }

        private void OnAwakeningStateEnd()
        {
            AskForNewTextRpc();
        }

        [Rpc(SendTo.Server)]
        private void AskForNewTextRpc()
        {
            Character _robot = GameManager.instance.characterManager.GetCharacters().FirstOrDefault(_c => _c.role.roleID == RoleID.Robot);
            if (!_robot)
            {
                WriteNewTextRpc("0");
                return;
            }
            List<TargetingData> _targetingDatas = RoleTargetSystem.instance.GetAllTargetingDataForTarget(_robot.ownerClientId.Value);
            WriteNewTextRpc($"{_targetingDatas.Count}");
        }

        [Rpc(SendTo.Everyone)]
        private void WriteNewTextRpc(FixedString32Bytes _newText)
        {
            text.text = _newText.ToString();
        }
    }
}