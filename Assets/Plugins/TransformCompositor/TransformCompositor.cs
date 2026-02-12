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
        /// </summary>
        public ComposedTransform GetComposedTransform()
        {
            Vector3 compositePosition = Vector3.zero;
            Vector3 compositeEulerAngles = Vector3.zero;
            Vector3 compositeScale = Vector3.one;

            foreach (var layer in layers.Values)
            {
                compositePosition += layer.localPosition;

                compositeEulerAngles += layer.localEulerAngles;

                compositeScale.x *= layer.localScale.x;
                compositeScale.y *= layer.localScale.y;
                compositeScale.z *= layer.localScale.z;
            }

            return new ComposedTransform
            {
                localPosition = compositePosition,
                localEulerAngles = compositeEulerAngles,
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
            public Vector3 localEulerAngles;
            public Vector3 localScale;

            public Quaternion localRotation => Quaternion.Euler(localEulerAngles);

            /// <summary>
            /// Applies this composed transform to a Unity Transform.
            /// </summary>
            public void ApplyTo(Transform target)
            {
                target.localPosition = localPosition;
                target.localEulerAngles = localEulerAngles;
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

