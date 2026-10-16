using GameLogic;
using GameLogic.GameStates;
using UnityEngine;
using UnityEngine.Assertions;

namespace FX
{
    /// <summary>
    /// A small lamp on the cork board at night (board task T11, 2026-07-23: "une petite lumière éclaire uniquement le
    /// tableau en liège afin qu'on puisse un minimum suivre l'avancée dans la nuit"), so the roles shown on it can be
    /// followed while the rest of the room stays dark. A <see cref="FramedLamp"/> on the board's face: the table and the
    /// cards in front never catch it (but for the profile's little spill at the board's foot), and the board's own shadow
    /// stops what would go past it. Fades in when the night (<see cref="AwakeningState"/>) begins and out when it ends.
    /// Every player sees it.
    /// </summary>
    public class CorkBoardLamp : FramedLamp
    {
        [Tooltip("The cork board (a box mesh whose face looks along -forward).")]
        [SerializeField] private MeshFilter _board;
        // Lane A (scene-wired), narrowed to the read slice like LightManager.
        [SerializeField] private GameManager _gameManager;

        private IGameStateQuery Query => _gameManager;

        protected override void Awake()
        {
            Assert.IsNotNull(_board, "CorkBoardLamp._board is not wired — wire it in GameScene (the composition root).");
            Assert.IsNotNull(_gameManager, "CorkBoardLamp._gameManager is not wired — wire it in GameScene (the composition root).");
            base.Awake();
        }

        private void Start()
        {
            Query.currentGameStateIndex.OnValueChanged += OnGameStateChanged;
        }

        protected override void OnDestroy()
        {
            if (_gameManager != null)
            {
                Query.currentGameStateIndex.OnValueChanged -= OnGameStateChanged;
            }
            base.OnDestroy();
        }

        protected override LampFraming.Framing FrameTarget(float _distance, float _spill) =>
            LampFraming.Frame(_board.transform, _board.sharedMesh.bounds, _distance, _spill);

        private void OnGameStateChanged(int _previousValue, int _newValue)
        {
            SetLit(Query.GetGameState(_newValue) is AwakeningState);
        }
    }
}
