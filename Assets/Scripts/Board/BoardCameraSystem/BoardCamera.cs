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
        
        private void Awake()
        {
            cinemachineCamera = GetComponent<CinemachineCamera>();
        }
        
        public void ActivateCamera()
        {
            cinemachineCamera.enabled = true;
        }
        
        public void DeactivateCamera()
        {
            cinemachineCamera.enabled = false;
        }
    }
}