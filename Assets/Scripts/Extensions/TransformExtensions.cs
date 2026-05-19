#region

using UnityEngine;

#endregion

namespace Extensions
{
    public static class TransformExtensions
    {
        public static void ResetLocalValues(this Transform _transform)
        {
            _transform.localPosition = Vector3.zero;
            _transform.localRotation = Quaternion.identity;
            _transform.localScale = Vector3.one;
        }
        
        public static Bounds GetWorldBounds(this Transform _transform)
        {
            var _renderers = _transform.GetComponentsInChildren<Renderer>();
            if (_renderers.Length == 0)
            {
                return new Bounds(_transform.position, Vector3.zero);
            }

            var _bounds = _renderers[0].bounds;
            for (int i = 1; i < _renderers.Length; i++)
            {
                _bounds.Encapsulate(_renderers[i].bounds);
            }
            
            return _bounds;
        }
    }
}