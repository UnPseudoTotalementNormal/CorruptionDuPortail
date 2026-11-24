using System.Collections.Generic;
using Characters;
using GameLogic;
using GameLogic.GameStates;
using MessageSystem;
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
            shownText.OnValueChanged += ShowText;
            ShowText(default, shownText.Value);
            if (!IsServer)
            {
                return;
            }
            
            Loop.onGameStarted += OnGameStarted;
        }

        // Bound at game start, never at spawn: GameManager clones its state assets in its own OnNetworkSpawn, and
        // in-scene objects spawn in no guaranteed order. Subscribing at spawn could land on the uncloned asset,
        // whose end never fires: the counter then stayed at its game-start "0/N" all game, while the bag (journal)
        // and the morning messages, recorded from the recap, were right (playtest 2026-11-21/22).
        private GameState[] boundAwakeningStates = System.Array.Empty<GameState>();

        private void BindAwakeningStates()
        {
            UnbindAwakeningStates();
            boundAwakeningStates = Query.GetGameStates(typeof(AwakeningState));
            foreach (GameState _awakeningState in boundAwakeningStates)
            {
                _awakeningState.onStateEndServer += OnAwakeningStateEnd;
            }
            Diagnostics.Logs.GameLog.Info("board", "board.counter.bound", "kind", "corruption", "states", boundAwakeningStates.Length);
        }

        private void UnbindAwakeningStates()
        {
            foreach (GameState _awakeningState in boundAwakeningStates)
            {
                if (_awakeningState != null)
                {
                    _awakeningState.onStateEndServer -= OnAwakeningStateEnd;
                }
            }
            boundAwakeningStates = System.Array.Empty<GameState>();
        }

        private void OnGameStarted()
        {
            BindAwakeningStates();
            RefreshCounter("game-start");
        }

        private void OnAwakeningStateEnd()
        {
            RefreshCounter("awakening-end");
        }

        // Same truth as the bag journal (MessageManager.RecordCurrentTurnStat): corrupted non-anomaly real seats.
        private void RefreshCounter(string _reason)
        {
            List<CharacterFactionState> _states = new();
            foreach (Character _character in CharacterQuery.GetCharacters())
            {
                // A seat without an attributed role (torn-down leaver) is not a participant to count.
                if (_character.role == null)
                {
                    continue;
                }
                _states.Add(new CharacterFactionState(_character.role.factionType, _character.isFake, _character.isCorrupted.Value));
            }
            TurnStat _stat = TurnStatCalculator.Compute(Loop.currentDay, _states, false, 0);
            SetShownText($"{_stat.corruptedCount}/{_stat.nonAnomalyTotal}", _reason);
        }

        // Replicated, not pushed by RPC: a peer that (re)joins mid-game reads the current count on spawn.
        private readonly NetworkVariable<FixedString32Bytes> shownText = new();

        private void SetShownText(FixedString32Bytes _newText, string _reason)
        {
            shownText.Value = _newText;
            Diagnostics.Logs.GameLog.Info("board", "board.counter", "kind", "corruption", "text", _newText.ToString(), "reason", _reason);
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
            if (IsServer)
            {
                UnbindAwakeningStates();
                if (gameManager != null)
                {
                    Loop.onGameStarted -= OnGameStarted;
                }
            }
            base.OnNetworkDespawn();
        }
    }
}
