using System;
using UnityEngine;

namespace TransformComposition
{
    /// <summary>
    /// MonoBehaviour component that applies a TransformCompositor to its transform.
    /// Automatically updates the transform each frame based on the composed layers.
    /// </summary>
    [ExecuteInEditMode]
    public class TransformCompositorComponent : MonoBehaviour
    {
        private const string BASE_TRANSFORM_LAYER_NAME = "Transform";
        private const float CHANGE_DETECTION_EPSILON = 0.0001f;
        
        [SerializeField] private TransformCompositor compositor = new();
        public bool autoUpdate = true;

        private Vector3 previousComposedPosition;
        private Vector3 previousComposedRotation;
        private Vector3 previousComposedScale = Vector3.one;
        private bool isInitialized;

        private void Reset()
        {
            transform.hideFlags = HideFlags.HideInInspector;
        }

        private void OnDestroy()
        {
            if (transform != null)
            {
                transform.hideFlags = HideFlags.None;
            }
        }
        
        private void LateUpdate()
        {
            DetectExternalChanges();
            
            if (autoUpdate)
            {
                ApplyComposedTransform();
            }
        }

        /// <summary>
        /// Detects if the transform was modified externally (e.g., via Inspector or other scripts)
        /// and updates the base layer accordingly.
        /// </summary>
        private void DetectExternalChanges()
        {
            if (!isInitialized)
            {
                isInitialized = true;
                return;
            }

            var expectedComposedTransform = compositor.GetComposedTransform();
            
            Vector3 currentPosition = transform.localPosition;
            Vector3 currentRotation = transform.localEulerAngles;
            Vector3 currentScale = transform.localScale;

            bool positionChanged = Vector3.Distance(currentPosition, previousComposedPosition) > CHANGE_DETECTION_EPSILON;
            bool rotationChanged = Vector3.Distance(currentRotation, previousComposedRotation) > CHANGE_DETECTION_EPSILON;
            bool scaleChanged = Vector3.Distance(currentScale, previousComposedScale) > CHANGE_DETECTION_EPSILON;

            if (positionChanged || rotationChanged || scaleChanged)
            {
                TransformLayer baseLayer = compositor.GetLayer(BASE_TRANSFORM_LAYER_NAME);
                
                Vector3 positionDelta = currentPosition - expectedComposedTransform.localPosition;
                Vector3 rotationDelta = currentRotation - expectedComposedTransform.localEulerAngles;
                Vector3 scaleDelta = new Vector3(
                    expectedComposedTransform.localScale.x != 0 ? currentScale.x / expectedComposedTransform.localScale.x : 1f,
                    expectedComposedTransform.localScale.y != 0 ? currentScale.y / expectedComposedTransform.localScale.y : 1f,
                    expectedComposedTransform.localScale.z != 0 ? currentScale.z / expectedComposedTransform.localScale.z : 1f
                );

                baseLayer.localPosition += positionDelta;
                baseLayer.localEulerAngles += rotationDelta;
                baseLayer.localScale = new Vector3(
                    baseLayer.localScale.x * scaleDelta.x,
                    baseLayer.localScale.y * scaleDelta.y,
                    baseLayer.localScale.z * scaleDelta.z
                );
            }
        }

        public TransformCompositor Compositor => compositor;

        /// <summary>
        /// Gets or creates a layer by name.
        /// </summary>
        public TransformLayer GetLayer(string layerName)
        {
            return compositor.GetLayer(layerName);
        }
        
        public bool HasLayer(string layerName)
        {
            return compositor.HasLayer(layerName);
        }

        public void RemoveLayer(string layerName)
        {
            compositor.RemoveLayer(layerName);
        }

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
            composed.ApplyTo(transform);
            
            // Store the values we just applied for next frame's change detection
            previousComposedPosition = composed.localPosition;
            previousComposedRotation = composed.localEulerAngles;
            previousComposedScale = composed.localScale;
        }

        public TransformCompositor.ComposedTransform GetComposedTransform()
        {
            return compositor.GetComposedTransform();
        }
    }
}

