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
        /// <summary>
        /// Position offset for this layer (additive).
        /// </summary>
        public Vector3 localPosition;

        /// <summary>
        /// Rotation offset in euler angles for this layer (additive).
        /// </summary>
        public Vector3 localEulerAngles;

        /// <summary>
        /// Scale multiplier for this layer (multiplicative).
        /// Default is (1, 1, 1) to have no effect on composition.
        /// </summary>
        public Vector3 localScale = Vector3.one;

        /// <summary>
        /// Rotation offset as Quaternion (additive).
        /// </summary>
        public Quaternion localRotation
        {
            get => Quaternion.Euler(localEulerAngles);
            set => localEulerAngles = value.eulerAngles;
        }

        /// <summary>
        /// Resets the layer to default values (no transformation).
        /// </summary>
        public void Reset()
        {
            localPosition = Vector3.zero;
            localEulerAngles = Vector3.zero;
            localScale = Vector3.one;
        }

        /// <summary>
        /// Individual position component accessors for easier manipulation.
        /// </summary>
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

        /// <summary>
        /// Individual rotation component accessors for easier manipulation.
        /// </summary>
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

        /// <summary>
        /// Individual scale component accessors for easier manipulation.
        /// </summary>
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

