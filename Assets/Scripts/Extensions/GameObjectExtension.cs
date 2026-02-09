using UnityEngine;

namespace Extensions
{
    public static class GameObjectExtension
    {
        /// <summary>
        /// Sets the layer for this object and all its children recursively
        /// </summary>
        /// <param name="_gameObject">The GameObject</param>
        /// <param name="_layer">The layer to apply</param>
        public static void SetLayerRecursively(this GameObject _gameObject, int _layer)
        {
            _gameObject.layer = _layer;
            
            foreach (Transform child in _gameObject.transform)
            {
                child.gameObject.SetLayerRecursively(_layer);
            }
        }
        
        /// <summary>
        /// Sets the layer for this object and all its children recursively
        /// </summary>
        /// <param name="_gameObject">The GameObject</param>
        /// <param name="_layerName">The layer name to apply</param>
        public static void SetLayerRecursively(this GameObject _gameObject, string _layerName)
        {
            int layer = LayerMask.NameToLayer(_layerName);
            if (layer != -1)
            {
                _gameObject.SetLayerRecursively(layer);
            }
            else
            {
                Debug.LogWarning($"Layer '{_layerName}' does not exist.");
            }
        }
    }
}