using System.Collections.Generic;
using UnityEngine;

namespace TransformComposition
{
    /// <summary>
    /// Manages multiple TransformLayers and composes them into a final transformation.
    /// </summary>
    [System.Serializable]
    public class TransformCompositor
    {
        [SerializeField] private SerializableDictionary<string, TransformLayer> layers = new();

        /// <summary>
        /// Gets a layer by name.
        /// If the layer doesn't exist, it will be created with default values.
        /// </summary>
        public TransformLayer GetLayer(string layerName)
        {
            if (!layers.ContainsKey(layerName))
            {
                layers[layerName] = new TransformLayer();
            }

            return layers[layerName];
        }

        public bool HasLayer(string layerName)
        {
            return layers.ContainsKey(layerName);
        }

        public void RemoveLayer(string layerName)
        {
            layers.Remove(layerName);
        }

        public void ClearLayers()
        {
            layers.Clear();
        }

        /// <summary>
        /// Computes the composite transformation from all layers.
        /// - Global mode: Rotations applied around global axes (order-independent feel)
        /// - Local mode: Applied in the space of accumulated transform (like parent-child hierarchy)
        /// </summary>
        public ComposedTransform GetComposedTransform()
        {
            Vector3 compositePosition = Vector3.zero;
            Quaternion compositeRotation = Quaternion.identity;
            Vector3 compositeScale = Vector3.one;

            foreach (var layer in layers.Values)
            {
                if (layer.compositeMode == CompositeMode.Global)
                {
                    compositePosition += layer.localPosition;
                    // Global mode: rotation applied around global axes (multiply on the left)
                    compositeRotation = layer.localRotation * compositeRotation;
                    compositeScale.x *= layer.localScale.x;
                    compositeScale.y *= layer.localScale.y;
                    compositeScale.z *= layer.localScale.z;
                }
                else // CompositeMode.Local
                {
                    // Position: rotate by accumulated rotation, then scale, then add
                    Vector3 rotatedPosition = compositeRotation * layer.localPosition;
                    Vector3 scaledPosition = Vector3.Scale(rotatedPosition, compositeScale);
                    compositePosition += scaledPosition;
                    
                    // Local mode: rotation applied in local space (multiply on the right)
                    compositeRotation *= layer.localRotation;
                    
                    compositeScale.x *= layer.localScale.x;
                    compositeScale.y *= layer.localScale.y;
                    compositeScale.z *= layer.localScale.z;
                }
            }

            return new ComposedTransform
            {
                localPosition = compositePosition,
                localRotation = compositeRotation,
                localScale = compositeScale
            };
        }

        /// <summary>
        /// Gets all layer names.
        /// </summary>
        public IEnumerable<string> GetLayerNames()
        {
            return layers.Keys;
        }

        /// <summary>
        /// Gets all layer transforms.
        /// </summary>
        public IEnumerable<TransformLayer> GetAllLayers()
        {
            return layers.Values;
        }

        /// <summary>
        /// Gets all layer names and their corresponding transforms as tuples.
        /// </summary>
        public IEnumerable<(string, TransformLayer)> GetAllLayersNamesAndTransforms()
        {
            foreach (var kvp in layers)
            {
                yield return (kvp.Key, kvp.Value);
            }
        } 

        /// <summary>
        /// Structure containing the composed transformation values.
        /// </summary>
        public struct ComposedTransform
        {
            public Vector3 localPosition;
            public Quaternion localRotation;
            public Vector3 localScale;

            public Vector3 localEulerAngles
            {
                get => localRotation.eulerAngles;
                set => localRotation = Quaternion.Euler(value);
            }

            /// <summary>
            /// Applies this composed transform to a Unity Transform.
            /// </summary>
            public void ApplyTo(Transform target)
            {
                target.localPosition = localPosition;
                target.localRotation = localRotation;
                target.localScale = localScale;
            }
        }
    }

    [System.Serializable]
    public class SerializableDictionary<TKey, TValue> : Dictionary<TKey, TValue>, ISerializationCallbackReceiver
    {
        [SerializeField] private List<TKey> keys = new();
        [SerializeField] private List<TValue> values = new();

        public void OnBeforeSerialize()
        {
            keys.Clear();
            values.Clear();

            foreach (var kvp in this)
            {
                keys.Add(kvp.Key);
                values.Add(kvp.Value);
            }
        }

        public void OnAfterDeserialize()
        {
            Clear();

            for (int i = 0; i < keys.Count && i < values.Count; i++)
            {
                this[keys[i]] = values[i];
            }
        }
    }
}

