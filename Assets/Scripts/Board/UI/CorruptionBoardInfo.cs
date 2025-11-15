using System.Linq;
using Characters;
using GameLogic;
using GameLogic.GameStates;
using TMPro;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

namespace Board.UI
{
    public class CorruptionBoardInfo : NetworkBehaviour
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
            WriteNewTextRpc(
                $"0/{GameManager.instance.characterManager.GetCharacters().Count(_c => _c.role.factionType != FactionType.anomaly && !_c.isFake)}");
        }

        private void OnAwakeningStateEnd()
        {
            AskForNewTextRpc();
        }

        [Rpc(SendTo.Server)]
        private void AskForNewTextRpc()
        {
            int _chosenCount = GameManager.instance.characterManager.GetCharacters()
                .Count(_c => _c.role.factionType != FactionType.anomaly && !_c.isFake);
            int _corruptedChosenCount = GameManager.instance.characterManager.GetCharacters()
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
