using UnityEngine;

namespace TransformComposition
{
    /// <summary>
    /// Represents an animation layer with position, rotation, and scale offsets.
    /// Can be animated by DOTween or set manually.
    /// </summary>
    [System.Serializable]
    public class TransformLayer
    {
        public Vector3 localPosition;

        public Vector3 localEulerAngles;

        /// <summary>
        /// Default is (1, 1, 1) to have no effect on composition.
        /// </summary>
        public Vector3 localScale = Vector3.one;

        public Quaternion localRotation
        {
            get => Quaternion.Euler(localEulerAngles);
            set => localEulerAngles = value.eulerAngles;
        }

        public void Reset()
        {
            localPosition = Vector3.zero;
            localEulerAngles = Vector3.zero;
            localScale = Vector3.one;
        }

        public float positionX
        {
            get => localPosition.x;
            set => localPosition.x = value;
        }

        public float positionY
        {
            get => localPosition.y;
            set => localPosition.y = value;
        }

        public float positionZ
        {
            get => localPosition.z;
            set => localPosition.z = value;
        }

        public float rotationX
        {
            get => localEulerAngles.x;
            set => localEulerAngles.x = value;
        }

        public float rotationY
        {
            get => localEulerAngles.y;
            set => localEulerAngles.y = value;
        }

        public float rotationZ
        {
            get => localEulerAngles.z;
            set => localEulerAngles.z = value;
        }

        public float scaleX
        {
            get => localScale.x;
            set => localScale.x = value;
        }

        public float scaleY
        {
            get => localScale.y;
            set => localScale.y = value;
        }

        public float scaleZ
        {
            get => localScale.z;
            set => localScale.z = value;
        }
    }
}

