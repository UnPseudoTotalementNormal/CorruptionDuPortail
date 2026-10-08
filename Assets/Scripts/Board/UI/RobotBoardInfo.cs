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
        // Story 10.2 lane C: the targeting system, resolved through the composition root in OnNetworkSpawn
        // (RoleTargetSystem stays a singleton — not de-singletonised), consumed by AskForNewTextRpc. RoleTargetSystem
        // now publishes its singleton in Awake: it used to do so in its own OnNetworkSpawn, and when this board spawned
        // first the field stayed null all game (NRE at every night's end, robot counter stuck; autoplay 2026-10-07).
        private RoleTargetSystem roleTargetSystem;

        private void Start()
        {
            Assert.IsNotNull(characterManager, "RobotBoardInfo.characterManager is not wired — wire it in GameScene (the composition root).");
            Assert.IsNotNull(gameManager, "RobotBoardInfo.gameManager is not wired — wire it in GameScene (the composition root).");
        }

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
            shownText.OnValueChanged += ShowText;
            ShowText(default, shownText.Value);
            roleTargetSystem = CompositionRoot.For(NetworkManager).RoleTargetSystem;
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
            SetShownText("0");
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
                SetShownText("0");
                return;
            }
            if (roleTargetSystem == null)
            {
                Debug.LogWarning("[RobotBoardInfo] no RoleTargetSystem — robot counter not updated this night.");
                return;
            }
            var _targetingDatas = roleTargetSystem.GetAllTargetersForTarget(_robot.ownerClientId.Value);
            SetShownText($"{_targetingDatas.Count}");
        }

        // Replicated, not pushed by RPC: a peer that (re)joins mid-game reads the current count on spawn.
        private readonly NetworkVariable<FixedString32Bytes> shownText = new();

        private void SetShownText(FixedString32Bytes _newText)
        {
            shownText.Value = _newText;
        }

        private void ShowText(FixedString32Bytes _previous, FixedString32Bytes _current)
        {
            if (_current.Length > 0)
            {
                text.text = _current.ToString();
            }
        }

        public override void OnNetworkDespawn()
        {
            shownText.OnValueChanged -= ShowText;
            base.OnNetworkDespawn();
        }
    }
}