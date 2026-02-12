using System.Collections.Generic;
using UnityEngine;

namespace TransformComposition
{
    /// <summary>
    /// Manages multiple TransformLayers and composes them into a final transformation.
    /// Position and rotation are additive, scale is multiplicative.
    /// </summary>
    [System.Serializable]
    public class TransformCompositor
    {
        [SerializeField] private SerializableDictionary<string, TransformLayer> layers = new();

        /// <summary>
        /// Gets or creates a layer by name.
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

        /// <summary>
        /// Checks if a layer exists.
        /// </summary>
        public bool HasLayer(string layerName)
        {
            return layers.ContainsKey(layerName);
        }

        /// <summary>
        /// Removes a layer by name.
        /// </summary>
        public void RemoveLayer(string layerName)
        {
            layers.Remove(layerName);
        }

        /// <summary>
        /// Removes all layers.
        /// </summary>
        public void ClearLayers()
        {
            layers.Clear();
        }

        /// <summary>
        /// Computes the composite transformation from all layers.
        /// Position and rotation are additive, scale is multiplicative.
        /// </summary>
        public ComposedTransform GetComposedTransform()
        {
            Vector3 compositePosition = Vector3.zero;
            Vector3 compositeEulerAngles = Vector3.zero;
            Vector3 compositeScale = Vector3.one;

            foreach (var layer in layers.Values)
            {
                // Additive for position
                compositePosition += layer.localPosition;

                // Additive for rotation (euler angles)
                compositeEulerAngles += layer.localEulerAngles;

                // Multiplicative for scale
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
        /// Gets all layers.
        /// </summary>
        public IEnumerable<TransformLayer> GetAllLayers()
        {
            return layers.Values;
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

    /// <summary>
    /// Simple serializable dictionary for Unity inspector support.
    /// If you have a better serializable dictionary (like AYellowpaper.SerializedCollections),
    /// you can replace this with that implementation.
    /// </summary>
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

