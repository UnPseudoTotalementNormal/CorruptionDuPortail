using System;
using UnityEngine;

namespace TransformComposition
{
    /// <summary>
    /// MonoBehaviour component that applies a TransformCompositor to its transform.
    /// Automatically updates the transform each frame based on the composed layers.
    /// </summary>
    public class TransformCompositorComponent : MonoBehaviour
    {
        [SerializeField] private TransformCompositor compositor = new();
        [SerializeField] private Transform targetTransform;
        [SerializeField] private bool autoUpdate = true;

        private void Reset()
        {
            targetTransform = transform;
        }

        /// <summary>
        /// Access to the underlying compositor.
        /// </summary>
        public TransformCompositor Compositor => compositor;

        /// <summary>
        /// Gets or creates a layer by name.
        /// </summary>
        public TransformLayer GetLayer(string layerName)
        {
            return compositor.GetLayer(layerName);
        }

        /// <summary>
        /// Checks if a layer exists.
        /// </summary>
        public bool HasLayer(string layerName)
        {
            return compositor.HasLayer(layerName);
        }

        /// <summary>
        /// Removes a layer by name.
        /// </summary>
        public void RemoveLayer(string layerName)
        {
            compositor.RemoveLayer(layerName);
        }

        /// <summary>
        /// Removes all layers.
        /// </summary>
        public void ClearLayers()
        {
            compositor.ClearLayers();
        }

        /// <summary>
        /// Manually applies the composed transformation to this transform.
        /// Called automatically in LateUpdate if autoUpdate is true.
        /// </summary>
        public void ApplyComposedTransform()
        {
            var composed = compositor.GetComposedTransform();
            composed.ApplyTo(targetTransform);
        }

        private void LateUpdate()
        {
            if (autoUpdate)
            {
                ApplyComposedTransform();
            }
        }

        /// <summary>
        /// Gets the composed transform without applying it.
        /// </summary>
        public TransformCompositor.ComposedTransform GetComposedTransform()
        {
            return compositor.GetComposedTransform();
        }
    }
}

