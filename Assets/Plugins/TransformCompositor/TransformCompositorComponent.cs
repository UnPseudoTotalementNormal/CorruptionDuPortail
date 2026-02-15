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
        public bool detectExternalChanges = true;

#if UNITY_EDITOR
        /// <summary>
        /// Editor-only: Specifies which layer receives external changes from scene gizmos.
        /// </summary>
        [System.NonSerialized]
        public string ActiveLayerForSceneEdit = null;
#endif

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
        /// and updates the target layer accordingly.
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
#if UNITY_EDITOR
                string targetLayerName = string.IsNullOrEmpty(ActiveLayerForSceneEdit) 
                    ? BASE_TRANSFORM_LAYER_NAME 
                    : ActiveLayerForSceneEdit;
#else
                string targetLayerName = BASE_TRANSFORM_LAYER_NAME;
#endif
                
                TransformLayer targetLayer = compositor.GetLayer(targetLayerName);
                
                var composedBefore = compositor.GetComposedTransformUpTo(targetLayerName);
                var composedAfter = compositor.GetComposedTransformAfter(targetLayerName);
                
                Vector3 transformWithoutAfter;
                Quaternion rotationWithoutAfter;
                Vector3 scaleWithoutAfter;
                
                if (targetLayer.compositeMode == CompositeMode.Local)
                {
                    scaleWithoutAfter = new Vector3(
                        composedAfter.localScale.x != 0 ? currentScale.x / composedAfter.localScale.x : currentScale.x,
                        composedAfter.localScale.y != 0 ? currentScale.y / composedAfter.localScale.y : currentScale.y,
                        composedAfter.localScale.z != 0 ? currentScale.z / composedAfter.localScale.z : currentScale.z
                    );
                    
                    rotationWithoutAfter = Quaternion.Euler(currentRotation) * Quaternion.Inverse(composedAfter.localRotation);
                    
                    Vector3 afterPosInCurrentSpace = rotationWithoutAfter * Vector3.Scale(composedAfter.localPosition, scaleWithoutAfter);
                    transformWithoutAfter = currentPosition - afterPosInCurrentSpace;
                }
                else
                {
                    scaleWithoutAfter = new Vector3(
                        composedAfter.localScale.x != 0 ? currentScale.x / composedAfter.localScale.x : currentScale.x,
                        composedAfter.localScale.y != 0 ? currentScale.y / composedAfter.localScale.y : currentScale.y,
                        composedAfter.localScale.z != 0 ? currentScale.z / composedAfter.localScale.z : currentScale.z
                    );
                    
                    rotationWithoutAfter = Quaternion.Inverse(composedAfter.localRotation) * Quaternion.Euler(currentRotation);
                    transformWithoutAfter = currentPosition - composedAfter.localPosition;
                }
                
                if (targetLayer.compositeMode == CompositeMode.Local)
                {
                    Vector3 localPosition = transformWithoutAfter - composedBefore.localPosition;
                    localPosition = Quaternion.Inverse(composedBefore.localRotation) * localPosition;
                    if (composedBefore.localScale.x != 0) localPosition.x /= composedBefore.localScale.x;
                    if (composedBefore.localScale.y != 0) localPosition.y /= composedBefore.localScale.y;
                    if (composedBefore.localScale.z != 0) localPosition.z /= composedBefore.localScale.z;
                    
                    targetLayer.localPosition = localPosition;
                    targetLayer.localRotation = Quaternion.Inverse(composedBefore.localRotation) * rotationWithoutAfter;
                    targetLayer.localScale = new Vector3(
                        composedBefore.localScale.x != 0 ? scaleWithoutAfter.x / composedBefore.localScale.x : scaleWithoutAfter.x,
                        composedBefore.localScale.y != 0 ? scaleWithoutAfter.y / composedBefore.localScale.y : scaleWithoutAfter.y,
                        composedBefore.localScale.z != 0 ? scaleWithoutAfter.z / composedBefore.localScale.z : scaleWithoutAfter.z
                    );
                }
                else
                {
                    targetLayer.localPosition = transformWithoutAfter - composedBefore.localPosition;
                    targetLayer.localRotation = Quaternion.Inverse(composedBefore.localRotation) * rotationWithoutAfter;
                    targetLayer.localScale = new Vector3(
                        composedBefore.localScale.x != 0 ? scaleWithoutAfter.x / composedBefore.localScale.x : scaleWithoutAfter.x,
                        composedBefore.localScale.y != 0 ? scaleWithoutAfter.y / composedBefore.localScale.y : scaleWithoutAfter.y,
                        composedBefore.localScale.z != 0 ? scaleWithoutAfter.z / composedBefore.localScale.z : scaleWithoutAfter.z
                    );
                }
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
        /// Gets the index of a layer in the evaluation order.
        /// </summary>
        public int GetLayerIndex(string layerName)
        {
            return compositor.GetLayerIndex(layerName);
        }

        /// <summary>
        /// Gets the total number of layers.
        /// </summary>
        public int GetLayerCount()
        {
            return compositor.GetLayerCount();
        }

        /// <summary>
        /// Moves a layer up in the evaluation order (earlier evaluation).
        /// </summary>
        public bool MoveLayerUp(string layerName)
        {
            return compositor.MoveLayerUp(layerName);
        }

        /// <summary>
        /// Moves a layer down in the evaluation order (later evaluation).
        /// </summary>
        public bool MoveLayerDown(string layerName)
        {
            return compositor.MoveLayerDown(layerName);
        }

        /// <summary>
        /// Moves a layer to a specific index in the evaluation order.
        /// </summary>
        public bool MoveLayerToIndex(string layerName, int newIndex)
        {
            return compositor.MoveLayerToIndex(layerName, newIndex);
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
