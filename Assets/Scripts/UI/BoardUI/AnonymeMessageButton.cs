using Characters;
using GameLogic;
using GameLogic.GameStates;
using UnityEngine;

namespace UI.BoardUI
{
    public class AnonymeMessageButton : CustomButton
    {
        private bool isAwakeningState = false;
        private bool hasMessagesLeft = false;
        private bool hasNotSentMessageThisTurn = true;

        // Story 12.2 lane A: GameManager + CharacterManager injected as scene-wired [SerializeField]s
        // (clears the last §4a-entangled hub reads off both globals). SceneWiringGuard is the wiring control.
        [SerializeField] private GameManager gameManager;
        [SerializeField] private CharacterManager characterManager;

        // Story 11.4 lifecycle hygiene: cache the exact GameManager + character whose events/NetworkVariables
        // we subscribe to, so OnDestroy can unsubscribe from the same instances.
        private GameManager _subscribedGameManager;
        private Character _subscribedLocalCharacter;

        private void Start()
        {
            _subscribedGameManager = gameManager;
            _subscribedGameManager.onGameStarted += OnGameStarted;
            _subscribedGameManager.currentGameStateIndex.OnValueChanged += OnCurrentGameStateIndexChanged;
        }

        private void OnGameStarted()
        {
            _subscribedLocalCharacter = characterManager.GetLocalCharacter(false);
            _subscribedLocalCharacter.hasSentMessageThisTurn.OnValueChanged += OnHasSentMessageThisTurnChanged;
            _subscribedLocalCharacter.messageLeft.OnValueChanged += OnMessageLeftChanged;

            isAwakeningState = gameManager.GetGameState(gameManager.currentGameStateIndex.Value) is AwakeningState;
            hasMessagesLeft = _subscribedLocalCharacter.messageLeft.Value > 0;
            hasNotSentMessageThisTurn = !_subscribedLocalCharacter.hasSentMessageThisTurn.Value;
        }

        private void OnDestroy()
        {
            // Mirror all four subscriptions (Start → onGameStarted + currentGameStateIndex,
            // OnGameStarted → hasSentMessageThisTurn + messageLeft).
            if (_subscribedGameManager != null)
            {
                _subscribedGameManager.onGameStarted -= OnGameStarted;
                _subscribedGameManager.currentGameStateIndex.OnValueChanged -= OnCurrentGameStateIndexChanged;
            }
            if (_subscribedLocalCharacter != null)
            {
                _subscribedLocalCharacter.hasSentMessageThisTurn.OnValueChanged -= OnHasSentMessageThisTurnChanged;
                _subscribedLocalCharacter.messageLeft.OnValueChanged -= OnMessageLeftChanged;
            }
        }

        private void OnCurrentGameStateIndexChanged(int _previousValue, int _newValue)
        {
            isAwakeningState = gameManager.GetGameState(_newValue) is AwakeningState;
            UpdateButtonState();
        }
        
        private void OnMessageLeftChanged(int _previousValue, int _newValue)
        {
            hasMessagesLeft = _newValue > 0;
            UpdateButtonState();
        }

        private void OnHasSentMessageThisTurnChanged(bool _previousValue, bool _newValue)
        {
            hasNotSentMessageThisTurn = !_newValue;
            UpdateButtonState();
        }

        private void UpdateButtonState()
        {
            enabled = isAwakeningState && hasMessagesLeft && hasNotSentMessageThisTurn;
        }
    }
}
