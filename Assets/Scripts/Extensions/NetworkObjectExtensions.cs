namespace Extensions
{
    using Unity.Netcode;
    using UnityEngine;

    public static class NetworkObjectExtensions
    {
        public static GameObject InstantiateAndSpawnWithChildren(this NetworkObject _prefab, Transform _parent = null, bool _destroyWithScene = true)
        {
            var _instance = Object.Instantiate(_prefab.gameObject, _parent);

            var _netObjects = _instance.GetComponentsInChildren<NetworkObject>(true);
            if (_netObjects.Length == 0)
                return _instance;

            _netObjects[0].Spawn(_destroyWithScene);

            for (int i = 1; i < _netObjects.Length; i++)
            {
                _netObjects[i].Spawn(_destroyWithScene);
            }

            return _instance;
        }
        
        /// <summary>
        /// Fait un Spawn() sur le NetworkObject parent et tous ses enfants NetworkObject.
        /// </summary>
        public static void SpawnWithChildren(this NetworkObject _parent, bool _destroyWithScene = true)
        {
            if (!_parent.IsSpawned)
                _parent.Spawn(_destroyWithScene);

            var _children = _parent.GetComponentsInChildren<NetworkObject>(true);
            foreach (var _child in _children)
            {
                if (_child != _parent && !_child.IsSpawned)
                {
                    var originalParent = _child.transform.parent;
                    _child.Spawn(_destroyWithScene);
                    // Reparent uniquement si le parent est null ou un NetworkObject
                    if (originalParent == null || originalParent.GetComponent<NetworkObject>() != null)
                    {
                        _child.transform.SetParent(originalParent, true);
                    }
                    // Sinon, ne pas reparenter pour éviter InvalidParentException
                }
            }
        }
    }
}

