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
using UnityEngine.Assertions;

namespace Board.UI
{
    public class RobotBoardInfo : NetworkBehaviour
    {
        [SerializeField] private TMP_Text text;

        // Story 7.4 lane A: scene-wired CharacterManager, replacing the GameManager hub-hop.
        [SerializeField] private CharacterManager characterManager;

        private void Start()
        {
            Assert.IsNotNull(characterManager, "RobotBoardInfo.characterManager is not wired — wire it in GameScene (the composition root).");
        }

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
            Character _robot = characterManager.GetCharacters().FirstOrDefault(_c => _c.role.roleID == RoleID.Robot);
            if (!_robot)
            {
                WriteNewTextRpc("0");
                return;
            }
            var _targetingDatas = RoleTargetSystem.instance.GetAllTargetersForTarget(_robot.ownerClientId.Value);
            WriteNewTextRpc($"{_targetingDatas.Count}");
        }

        [Rpc(SendTo.Everyone)]
        private void WriteNewTextRpc(FixedString32Bytes _newText)
        {
            text.text = _newText.ToString();
        }
    }
}