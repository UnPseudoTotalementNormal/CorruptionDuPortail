using System;
using AYellowpaper.SerializedCollections;
using Unity.Cinemachine;
using UnityEngine;

namespace Board.BoardCameraSystem
{
    [RequireComponent(typeof(CinemachineCamera))]
    public class BoardCamera : MonoBehaviour
    {
        public BoardCameraIdEnum boardCameraId;
        public SerializedDictionary<NeighbourDirection, BoardCamera> neighbours = new();
        private CinemachineCamera cinemachineCamera;

        public event Action onCameraActivated;
        public event Action onCameraDeactivated;
        
        private void Awake()
        {
            cinemachineCamera = GetComponent<CinemachineCamera>();
        }
        
        public void ActivateCamera()
        {
            cinemachineCamera.enabled = true;
            onCameraActivated?.Invoke();
        }
        
        public void DeactivateCamera()
        {
            cinemachineCamera.enabled = false;
            onCameraDeactivated?.Invoke();
        }
    }
}