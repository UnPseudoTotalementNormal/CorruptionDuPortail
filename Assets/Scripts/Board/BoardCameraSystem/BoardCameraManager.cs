using System;
using System;
using System.Collections.Generic;
using System.Linq;
using Controllers;
using Controllers.Inputs;
using GameLogic;
using UnityEngine;
using UnityEngine.Assertions;

namespace Board.BoardCameraSystem
{
    public class BoardCameraManager : MonoController
    {
        public static BoardCameraManager instance;
        
        private List<BoardCamera> boardCameras = new();
        private BoardCamera currentBoardCamera;

        /// <summary>Fires after the current board camera changes (incl. the initial set), carrying the new id.
        /// The AvatarCameraArbiter listens so the embodied Vote camera / reticle / cursor follow whether the
        /// seated first-person node is the live camera as the player arrows between it and the board cams.</summary>
        public event Action<BoardCameraIdEnum> onCurrentCameraChanged;

        /// <summary>Id of the live board camera (None if none active yet).</summary>
        public BoardCameraIdEnum CurrentBoardCameraId =>
            currentBoardCamera != null ? currentBoardCamera.boardCameraId : BoardCameraIdEnum.None;

        [SerializeField] private BoardCamera startingBoardCamera;
        [SerializeField] private Transform cameraParentTransform;
        [SerializeField] private GameManager gameManager;

        // Story 8.2 (Epic 8 / D2): depend on the narrow read slice, not the whole GameManager.
        // Field stays concrete (Unity can't serialize an interface); the property narrows it (lane A).
        private IGameStateQuery Query => gameManager;

        private void Awake()
        {
            if (instance != null && instance != this)
            {
                Destroy(gameObject);
                return;
            }
            instance = this;
            Assert.IsNotNull(gameManager, "BoardCameraManager.gameManager is not wired — wire it in GameScene (the composition root).");
        }

        private void OnDestroy()
        {
            if (instance == this)
            {
                instance = null;
            }
            // Story 11.4 lifecycle hygiene: mirror the Start subscription (subscribe-in-X ⇒ unsubscribe-in-its-teardown).
            if (gameManager != null)
            {
                Query.currentGameStateIndex.OnValueChanged -= OnGameStateChanged;
            }
        }

        private void Start()
        {
            boardCameras = cameraParentTransform.GetComponentsInChildren<BoardCamera>().ToList();
            foreach (BoardCamera _boardCamera in boardCameras)
            {
                _boardCamera.DeactivateCamera();
            }
            SetCurrentBoardCamera(startingBoardCamera);
            
            InputManager.instance.RegisterAction(InputID.ArrowUp, InputState.Started, () => TrySwitchCameraToNeighbour(NeighbourDirection.Up));
            InputManager.instance.RegisterAction(InputID.ArrowDown, InputState.Started, () => TrySwitchCameraToNeighbour(NeighbourDirection.Down));
            InputManager.instance.RegisterAction(InputID.ArrowLeft, InputState.Started, () => TrySwitchCameraToNeighbour(NeighbourDirection.Left));
            InputManager.instance.RegisterAction(InputID.ArrowRight, InputState.Started, () => TrySwitchCameraToNeighbour(NeighbourDirection.Right));
            
            Query.currentGameStateIndex.OnValueChanged += OnGameStateChanged;
            OnGameStateChanged(Query.currentGameStateIndex.Value, Query.currentGameStateIndex.Value);
        }

        private void OnGameStateChanged(int _previousValue, int _newValue)
        {
            GameState _gameState = Query.GetGameState(_newValue);
            if (_gameState == null)
            {
                return;
            }
            
            if (_gameState.forceBoardCamera != BoardCameraIdEnum.None)
            {
                SetCurrentBoardCamera(_gameState.forceBoardCamera);
                SetActiveSource(BoardCameraInputActiveSource.GameState, false);
            }
            else
            {
                // A starting camera (unlike a forced one) only sets the ENTRY view; arrow nav stays live so the
                // player can move on from it. The Vote opens on the seated first-person node this way.
                if (_gameState.startingBoardCamera != BoardCameraIdEnum.None)
                {
                    SetCurrentBoardCamera(_gameState.startingBoardCamera);
                }
                SetActiveSource(BoardCameraInputActiveSource.GameState, true);
            }
        }

        public void TrySwitchCameraToNeighbour(NeighbourDirection _neighbourDirection)
        {
            if (!IsActive())
            {
                return;
            }
            
            SwitchCameraToNeighbour(_neighbourDirection);
        }
        
        public void SwitchCameraToNeighbour(NeighbourDirection _neighbourDirection)
        {
            if (currentBoardCamera == null || !currentBoardCamera.neighbours.ContainsKey(_neighbourDirection) ||
                currentBoardCamera.neighbours[_neighbourDirection] == null)
            {
                return;
            }
            SetCurrentBoardCamera(currentBoardCamera.neighbours[_neighbourDirection]);
        }

        public void SetCurrentBoardCamera(BoardCamera _boardCamera)
        {
            currentBoardCamera?.DeactivateCamera();
            currentBoardCamera = _boardCamera;
            currentBoardCamera?.ActivateCamera();
            onCurrentCameraChanged?.Invoke(CurrentBoardCameraId);
        }
        
        public void SetCurrentBoardCamera(BoardCameraIdEnum _boardCameraId)
        {
            BoardCamera _boardCamera = boardCameras.FirstOrDefault(_bc => _bc.boardCameraId == _boardCameraId);
            if (_boardCamera == null)
            {
                Debug.LogWarning("BoardCameraManager: No BoardCamera found with ID " + _boardCameraId);
                return;
            }
            SetCurrentBoardCamera(_boardCamera);
        }
        
        public void SetActiveSource(BoardCameraInputActiveSource _activeSource, bool _isActive)
        {
            SetActiveSource(_activeSource.ToString(), _isActive);
        }

        /// <summary>Restore the inspector-assigned default board camera. The arbiter calls this when leaving the
        /// Vote so the board is never stranded on the seated first-person node (a Vote-only camera whose driver
        /// stops outside the Vote). No-op if no default is wired.</summary>
        public void ResetToStartingCamera()
        {
            if (startingBoardCamera != null)
            {
                SetCurrentBoardCamera(startingBoardCamera);
            }
        }
    }
    
    public enum BoardCameraInputActiveSource
    {
        GameState = 0,
        Cutscene = 1,
        Pause = 2,
        // Story 13.3 (Epic 13 — Player Embodiment): the source the AvatarCameraArbiter toggles.
        // APPEND-ONLY — never renumber 0/1/2 (other call-sites + the inspector depend on the values).
        // It ANDs with the existing GameState source via ControllerBase.IsActive() (Controller.cs:43-53),
        // so SetActiveSource(Avatar, false) cuts board-camera arrow neighbour-nav in FreeRoam/Embodied
        // WITHOUT touching the GameState source or removing/disabling any board camera (NFR1).
        Avatar = 3,
    }
}