using System.Linq;
using Characters;
using GameLogic;
using GameLogic.GameStates;
using TMPro;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.Assertions;

namespace Board.UI
{
    public class CorruptionBoardInfo : NetworkBehaviour
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
            Assert.IsNotNull(characterManager, "CorruptionBoardInfo.characterManager is not wired — wire it in GameScene (the composition root).");
            Assert.IsNotNull(gameManager, "CorruptionBoardInfo.gameManager is not wired — wire it in GameScene (the composition root).");
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
            WriteNewTextRpc(
                $"0/{CharacterQuery.GetCharacters().Count(_c => _c.role.factionType != FactionType.anomaly && !_c.isFake)}");
        }

        private void OnAwakeningStateEnd()
        {
            AskForNewTextRpc();
        }

        [Rpc(SendTo.Server)]
        private void AskForNewTextRpc()
        {
            int _chosenCount = CharacterQuery.GetCharacters()
                .Count(_c => _c.role.factionType != FactionType.anomaly && !_c.isFake);
            int _corruptedChosenCount = CharacterQuery.GetCharacters()
                .Count(_c => _c.role.factionType != FactionType.anomaly && _c.isCorrupted.Value && !_c.isFake);
            WriteNewTextRpc($"{_corruptedChosenCount}/{_chosenCount}");
        }

        [Rpc(SendTo.Everyone)]
        private void WriteNewTextRpc(FixedString32Bytes _newText)
        {
            text.text = _newText.ToString();
        }
    }
}
