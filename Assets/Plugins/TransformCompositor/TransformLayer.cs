using UnityEngine;

namespace TransformComposition
{
    /// <summary>
    /// Defines how a layer's transformation is applied during composition.
    /// </summary>
    public enum CompositeMode
    {
        /// <summary>
        /// Layer transformations are applied in global space.
        /// </summary>
        Global,
        
        /// <summary>
        /// Layer transformations are applied in the local space of the previous layers.
        /// Simulates a parent-child transform hierarchy.
        /// </summary>
        Local
    }

    /// <summary>
    /// Represents an animation layer with position, rotation, and scale offsets.
    /// </summary>
    [System.Serializable]
    public class TransformLayer
    {
        public Vector3 localPosition;

        public Quaternion localRotation = Quaternion.identity;

        /// <summary>
        /// Default is (1, 1, 1) to have no effect on composition.
        /// </summary>
        public Vector3 localScale = Vector3.one;

        /// <summary>
        /// Determines how this layer is composed with others.
        /// </summary>
        public CompositeMode compositeMode = CompositeMode.Global;

        public Vector3 localEulerAngles
        {
            get => localRotation.eulerAngles;
            set => localRotation = Quaternion.Euler(value);
        }

        public void Reset()
        {
            localPosition = Vector3.zero;
            localRotation = Quaternion.identity;
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
            set
            {
                Vector3 euler = localEulerAngles;
                euler.x = value;
                localEulerAngles = euler;
            }
        }

        public float rotationY
        {
            get => localEulerAngles.y;
            set
            {
                Vector3 euler = localEulerAngles;
                euler.y = value;
                localEulerAngles = euler;
            }
        }

        public float rotationZ
        {
            get => localEulerAngles.z;
            set
            {
                Vector3 euler = localEulerAngles;
                euler.z = value;
                localEulerAngles = euler;
            }
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

