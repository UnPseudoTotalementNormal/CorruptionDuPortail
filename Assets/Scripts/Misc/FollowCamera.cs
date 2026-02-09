using System;
using UnityEngine;

namespace Misc
{
    public class FollowCamera : MonoBehaviour
    {
        [SerializeField] private Vector3 positionOffset;
        [SerializeField] private Vector3 rotationOffset;
        [SerializeField] private Camera camera;
        
        private void Start()
        {
            if (!camera)
            {
                camera = Camera.main;
            }
        }

        private void Update()
        {
            transform.position = camera.transform.position + positionOffset;
            transform.rotation = camera.transform.rotation * Quaternion.Euler(rotationOffset);
        }
    }
}
