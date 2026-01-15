using System;
using System.Collections.Generic;
using System.Linq;
using Controllers;
using Controllers.Inputs;
using UnityEngine;

namespace Board.BoardCameraSystem
{
    public class BoardCameraManager : MonoController
    {
        public static BoardCameraManager instance;
        
        private List<BoardCamera> boardCameras = new();
        private BoardCamera currentBoardCamera;
        
        [SerializeField] private BoardCamera startingBoardCamera;
        [SerializeField] private Transform cameraParentTransform;
        
        private void Awake()
        {
            if (instance == null)
            {
                instance = this;
            }
            else
            {
                Destroy(gameObject);
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
        }
        
        public void SetActiveSource(BoardCameraInputActiveSource _activeSource, bool _isActive)
        {
            SetActiveSource(_activeSource.ToString(), _isActive);
        }
    }
    
    public enum BoardCameraInputActiveSource
    {
        GameState = 0,
        Cutscene = 1,
        Pause = 2,
    }
}