using Characters;
using GameLogic;
using GameLogic.GameStates;

namespace UI.BoardUI
{
    public class AnonymeMessageButton : CustomButton
    {
        private bool isAwakeningState = false;
        private bool hasMessagesLeft = false;
        private bool hasNotSentMessageThisTurn = true;

        private void Start()
        {
            GameManager.instance.onGameStarted += OnGameStarted;
            GameManager.instance.currentGameStateIndex.OnValueChanged += OnCurrentGameStateIndexChanged;
        }

        private void OnGameStarted()
        {
            var _localCharacter = CharacterManager.instance.GetLocalCharacter(false);
            _localCharacter.hasSentMessageThisTurn.OnValueChanged += OnHasSentMessageThisTurnChanged;
            _localCharacter.messageLeft.OnValueChanged += OnMessageLeftChanged;
            
            isAwakeningState = GameManager.instance.GetGameState(GameManager.instance.currentGameStateIndex.Value) is AwakeningState;
            hasMessagesLeft = _localCharacter.messageLeft.Value > 0;
            hasNotSentMessageThisTurn = !_localCharacter.hasSentMessageThisTurn.Value;
        }

        private void OnCurrentGameStateIndexChanged(int _previousValue, int _newValue)
        {
            isAwakeningState = GameManager.instance.GetGameState(_newValue) is AwakeningState;
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
