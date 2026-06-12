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
        // Story 9.1 (Epic 9 / D3): read slice of the scene-wired characterManager (D-NFR6 internal-narrowing).
        private ICharacterQuery CharacterQuery => characterManager;
        // Story 8.3 lane A: scene-wired GameManager, narrowed to the loop slices (IGameStateQuery + IGameLoop).
        [SerializeField] private GameManager gameManager;
        private IGameStateQuery Query => gameManager;
        private IGameLoop Loop => gameManager;

        private void Start()
        {
            Assert.IsNotNull(characterManager, "RobotBoardInfo.characterManager is not wired — wire it in GameScene (the composition root).");
            Assert.IsNotNull(gameManager, "RobotBoardInfo.gameManager is not wired — wire it in GameScene (the composition root).");
        }

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
            if (!IsServer)
            {
                return;
            }
            
            GameState[] _awakeningStates = Query.GetGameStates(typeof(AwakeningState));
            foreach (var _awakeningState in _awakeningStates)
            {
                _awakeningState.onStateEndServer += OnAwakeningStateEnd;
            }
            Loop.onGameStarted += OnGameStarted;
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
            Character _robot = CharacterQuery.GetCharacters().FirstOrDefault(_c => _c.role.roleID == RoleID.Robot);
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