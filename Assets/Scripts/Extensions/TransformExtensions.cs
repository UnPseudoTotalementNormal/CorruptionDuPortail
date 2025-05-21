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
    }
}