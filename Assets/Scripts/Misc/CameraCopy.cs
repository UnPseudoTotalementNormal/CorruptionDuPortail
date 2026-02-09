using System;
using UnityEngine;

namespace Misc
{
    public class CameraCopy : MonoBehaviour
    {
        [SerializeField] private Camera cameraToCopy;
        private Camera cameraToApply;

        [SerializeField] private bool copyPositionAndRotation = true;
        
        private void Awake()
        {
            if (!cameraToCopy)
            {
                cameraToCopy = Camera.main;
            }
            cameraToApply = GetComponent<Camera>();
        }

        private void Update()
        {
            cameraToApply.fieldOfView = cameraToCopy.fieldOfView;
            if (copyPositionAndRotation)
            {
                cameraToApply.transform.position = cameraToCopy.transform.position;
                cameraToApply.transform.rotation = cameraToCopy.transform.rotation;
            }
        }
    }
}