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
            return GetComposedTransformUpTo(null);
        }

        /// <summary>
        /// Computes the composite transformation up to (excluding) a specific layer.
        /// If layerName is null, computes all layers.
        /// </summary>
        public ComposedTransform GetComposedTransformUpTo(string stopBeforeLayerName)
        {
            Vector3 compositePosition = Vector3.zero;
            Quaternion compositeRotation = Quaternion.identity;
            Vector3 compositeScale = Vector3.one;

            foreach (var kvp in layers)
            {
                // Stop before the specified layer
                if (stopBeforeLayerName != null && kvp.Key == stopBeforeLayerName)
                {
                    break;
                }
                
                var layer = kvp.Value;
                
                if (layer.compositeMode == CompositeMode.Global)
                {
                    compositePosition += layer.localPosition;
                    compositeRotation = layer.localRotation * compositeRotation;
                    compositeScale.x *= layer.localScale.x;
                    compositeScale.y *= layer.localScale.y;
                    compositeScale.z *= layer.localScale.z;
                }
                else // CompositeMode.Local
                {
                    Vector3 rotatedPosition = compositeRotation * layer.localPosition;
                    Vector3 scaledPosition = Vector3.Scale(rotatedPosition, compositeScale);
                    compositePosition += scaledPosition;
                    
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
        /// Computes the composite transformation starting after (excluding) a specific layer.
        /// Returns identity transform if layerName is null or not found.
        /// </summary>
        public ComposedTransform GetComposedTransformAfter(string startAfterLayerName)
        {
            Vector3 compositePosition = Vector3.zero;
            Quaternion compositeRotation = Quaternion.identity;
            Vector3 compositeScale = Vector3.one;

            bool foundLayer = false;

            foreach (var kvp in layers)
            {
                // Skip until we pass the specified layer
                if (!foundLayer)
                {
                    if (kvp.Key == startAfterLayerName)
                    {
                        foundLayer = true;
                    }
                    continue;
                }
                
                var layer = kvp.Value;
                
                if (layer.compositeMode == CompositeMode.Global)
                {
                    compositePosition += layer.localPosition;
                    compositeRotation = layer.localRotation * compositeRotation;
                    compositeScale.x *= layer.localScale.x;
                    compositeScale.y *= layer.localScale.y;
                    compositeScale.z *= layer.localScale.z;
                }
                else // CompositeMode.Local
                {
                    Vector3 rotatedPosition = compositeRotation * layer.localPosition;
                    Vector3 scaledPosition = Vector3.Scale(rotatedPosition, compositeScale);
                    compositePosition += scaledPosition;
                    
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
        /// Gets the index of a layer in the evaluation order.
        /// Returns -1 if the layer doesn't exist.
        /// </summary>
        public int GetLayerIndex(string layerName)
        {
            return layers.Keys.IndexOf(layerName);
        }

        /// <summary>
        /// Gets the total number of layers.
        /// </summary>
        public int GetLayerCount()
        {
            return layers.Count;
        }

        /// <summary>
        /// Moves a layer up in the evaluation order (decreases index).
        /// Returns true if successful, false if already at top or layer doesn't exist.
        /// </summary>
        public bool MoveLayerUp(string layerName)
        {
            int index = GetLayerIndex(layerName);
            if (index <= 0) return false;
            
            return MoveLayerToIndex(layerName, index - 1);
        }

        /// <summary>
        /// Moves a layer down in the evaluation order (increases index).
        /// Returns true if successful, false if already at bottom or layer doesn't exist.
        /// </summary>
        public bool MoveLayerDown(string layerName)
        {
            int index = GetLayerIndex(layerName);
            if (index < 0 || index >= layers.Count - 1) return false;
            
            return MoveLayerToIndex(layerName, index + 1);
        }

        /// <summary>
        /// Moves a layer to a specific index in the evaluation order.
        /// Returns true if successful, false if layer doesn't exist or index is out of range.
        /// </summary>
        public bool MoveLayerToIndex(string layerName, int newIndex)
        {
            int currentIndex = GetLayerIndex(layerName);
            if (currentIndex < 0) return false;
            
            if (newIndex < 0 || newIndex >= layers.Count) return false;
            if (currentIndex == newIndex) return true;
            
            var key = layers.Keys[currentIndex];
            var value = layers.Values[currentIndex];
            
            layers.Keys.RemoveAt(currentIndex);
            layers.Values.RemoveAt(currentIndex);
            
            layers.Keys.Insert(newIndex, key);
            layers.Values.Insert(newIndex, value);
            
            layers.Clear();
            for (int i = 0; i < layers.Keys.Count; i++)
            {
                layers[layers.Keys[i]] = layers.Values[i];
            }
            
            return true;
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

        public new List<TKey> Keys => keys;
        public new List<TValue> Values => values;

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

