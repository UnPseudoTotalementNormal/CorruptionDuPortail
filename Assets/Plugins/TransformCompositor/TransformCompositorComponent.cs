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
        public const string BASE_TRANSFORM_LAYER_NAME = "Transform";
        private const float CHANGE_DETECTION_EPSILON = 0.0001f;
        
        [SerializeField] private TransformCompositor compositor = new();
        public bool autoUpdate = true;
        public bool detectExternalChanges = true; // Disable if you don't need external change detection for better performance

        private Vector3 previousComposedPosition;
        private Vector3 previousComposedRotation;
        private Vector3 previousComposedScale = Vector3.one;
        private bool isInitialized;

        private void Reset()
        {
            transform.hideFlags = HideFlags.HideInInspector;
            TransformLayer baseLayer = compositor.GetLayer(BASE_TRANSFORM_LAYER_NAME);
            baseLayer.localPosition = transform.localPosition;
            baseLayer.localRotation = transform.localRotation;
            baseLayer.localScale = transform.localScale;
            #if UNITY_EDITOR
            while (UnityEditorInternal.ComponentUtility.MoveComponentUp(this)) { }
            #endif
        }

        private void OnDestroy()
        {
            if (transform != null)
            {
                transform.hideFlags = HideFlags.None;
            }
        }

        private void OnValidate()
        {
            if (this != null)
            {
                transform.hideFlags = HideFlags.HideInInspector;
            }
        }

        private void LateUpdate()
        {
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

            Vector3 currentPosition = transform.localPosition;
            Vector3 currentRotation = transform.localEulerAngles;
            Vector3 currentScale = transform.localScale;

            bool positionChanged = Vector3.Distance(currentPosition, previousComposedPosition) > CHANGE_DETECTION_EPSILON;
            bool rotationChanged = Vector3.Distance(currentRotation, previousComposedRotation) > CHANGE_DETECTION_EPSILON;
            bool scaleChanged = Vector3.Distance(currentScale, previousComposedScale) > CHANGE_DETECTION_EPSILON;

            if (positionChanged || rotationChanged || scaleChanged)
            {
                TransformLayer baseLayer = compositor.GetLayer(BASE_TRANSFORM_LAYER_NAME);
                
                // Calculate the delta: what changed externally
                Vector3 positionDelta = currentPosition - previousComposedPosition;
                Vector3 rotationDelta = currentRotation - previousComposedRotation;
                Vector3 scaleDelta = new Vector3(
                    previousComposedScale.x != 0 ? currentScale.x / previousComposedScale.x : 1f,
                    previousComposedScale.y != 0 ? currentScale.y / previousComposedScale.y : 1f,
                    previousComposedScale.z != 0 ? currentScale.z / previousComposedScale.z : 1f
                );

                // Apply the external delta to the base layer
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
            if (detectExternalChanges)
            {
                DetectExternalChanges();
            }
            
            var composed = compositor.GetComposedTransform();
            composed.ApplyTo(transform);
            
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
