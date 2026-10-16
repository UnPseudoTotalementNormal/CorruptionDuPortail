using Characters;
using GameLogic;
using GameLogic.GameStates;
using MessageSystem;
using UnityEngine;
using UnityEngine.Assertions;

namespace FX
{
    /// <summary>
    /// A small lamp on the letters (board task T11, 2026-07-23: "une petite lumière qui éclaire les lettres pour rappeler
    /// qu'on peut s'en servir"), a <see cref="FramedLamp"/> hung above the envelope lying on the nightstand. Lit only
    /// while the local player can send a message (<see cref="MessageAvailability"/>: at night, with messages left, not
    /// sent yet this night), so it reminds them of it and goes out once there is nothing left to send. Each player sees
    /// their own.
    /// </summary>
    public class LetterLamp : FramedLamp
    {
        [Tooltip("The letters (the envelope a message is sent with), lying flat: the pool is framed on its bounds.")]
        [SerializeField] private Renderer _letters;
        // Lane A (scene-wired), narrowed to the slices read here like AnonymeMessageButton.
        [SerializeField] private GameManager _gameManager;
        [SerializeField] private CharacterManager _characterManager;

        private IGameStateQuery Query => _gameManager;
        private IGameLoop Loop => _gameManager;
        private ICharacterQuery CharacterQuery => _characterManager;

        // Cached so OnDestroy unsubscribes from the same instance (Story 11.4 lifecycle hygiene).
        private Character _localCharacter;
        private bool _isNight;

        protected override void Awake()
        {
            Assert.IsNotNull(_letters, "LetterLamp._letters is not wired — wire it in GameScene (the composition root).");
            Assert.IsNotNull(_gameManager, "LetterLamp._gameManager is not wired — wire it in GameScene (the composition root).");
            Assert.IsNotNull(_characterManager, "LetterLamp._characterManager is not wired — wire it in GameScene (the composition root).");
            base.Awake();
        }

        private void Start()
        {
            Loop.onGameStarted += OnGameStarted;
            Query.currentGameStateIndex.OnValueChanged += OnGameStateChanged;
        }

        protected override void OnDestroy()
        {
            if (_gameManager != null)
            {
                Loop.onGameStarted -= OnGameStarted;
                Query.currentGameStateIndex.OnValueChanged -= OnGameStateChanged;
            }
            UnsubscribeLocalCharacter();
            base.OnDestroy();
        }

        protected override LampFraming.Framing FrameTarget(float _distance, float _spill) =>
            LampFraming.FrameFromAbove(_letters.bounds, _distance, _spill);

        protected override LampFraming.Profile DefaultProfile => new LampFraming.Profile
        {
            endFade = 1.25f, edgeFade = 0.9f, endLevel = 1f, bottomLevel = 1f, spill = 0.5f, spillPastTopEdge = true,
        };

        private void OnGameStarted()
        {
            UnsubscribeLocalCharacter();
            _localCharacter = CharacterQuery.GetLocalCharacter(false);
            if (_localCharacter != null)
            {
                _localCharacter.messageLeft.OnValueChanged += OnMessageLeftChanged;
                _localCharacter.hasSentMessageThisTurn.OnValueChanged += OnHasSentMessageChanged;
            }
            _isNight = Query.GetGameState(Query.currentGameStateIndex.Value) is AwakeningState;
            Refresh();
        }

        private void UnsubscribeLocalCharacter()
        {
            if (_localCharacter == null)
            {
                return;
            }
            _localCharacter.messageLeft.OnValueChanged -= OnMessageLeftChanged;
            _localCharacter.hasSentMessageThisTurn.OnValueChanged -= OnHasSentMessageChanged;
            _localCharacter = null;
        }

        private void OnGameStateChanged(int _previousValue, int _newValue)
        {
            _isNight = Query.GetGameState(_newValue) is AwakeningState;
            Refresh();
        }

        private void OnMessageLeftChanged(int _previousValue, int _newValue) => Refresh();

        private void OnHasSentMessageChanged(bool _previousValue, bool _newValue) => Refresh();

        private void Refresh()
        {
            SetLit(_localCharacter != null && MessageAvailability.CanSend(_isNight, _localCharacter.messageLeft.Value,
                _localCharacter.hasSentMessageThisTurn.Value));
        }
    }
}
