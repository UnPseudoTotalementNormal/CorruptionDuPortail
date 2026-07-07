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

        /// <summary>
        /// Projects the 8 corners of a world-space AABB to screen space and returns the axis-aligned
        /// screen-space bounding box (size + center) that encloses the object's on-screen silhouette.
        /// Projecting only min/max is wrong under perspective — the two diagonal corners differ in depth
        /// and do not bound the projected shape, so all 8 corners must be considered.
        /// Corners in front of the near plane (camera-space z &lt;= nearClipPlane) are skipped: those behind the
        /// camera project to mirrored x/y, and those in the 0..near band project to unbounded coordinates.
        /// Returns false when the camera is null or every corner is behind it.
        /// </summary>
        public static bool TryGetScreenBounds(this Bounds _worldBounds, Camera _camera,
            out Vector2 _screenSize, out Vector2 _screenCenter)
        {
            _screenSize = Vector2.zero;
            _screenCenter = Vector2.zero;

            if (_camera == null)
            {
                return false;
            }

            Vector3 _center = _worldBounds.center;
            Vector3 _extents = _worldBounds.extents;

            Vector2 _min = new Vector2(float.PositiveInfinity, float.PositiveInfinity);
            Vector2 _max = new Vector2(float.NegativeInfinity, float.NegativeInfinity);
            int _validCorners = 0;

            for (int i = 0; i < 8; i++)
            {
                Vector3 _corner = _center + new Vector3(
                    (i & 1) == 0 ? -_extents.x : _extents.x,
                    (i & 2) == 0 ? -_extents.y : _extents.y,
                    (i & 4) == 0 ? -_extents.z : _extents.z);

                // WorldToScreenPoint returns camera-space depth in z. Corners at or in front of the near
                // plane are skipped: behind the camera (z <= 0) projects mirrored, and the 0..near band
                // projects to unbounded screen coordinates that would explode the box.
                Vector3 _screenPoint = _camera.WorldToScreenPoint(_corner);
                if (_screenPoint.z <= _camera.nearClipPlane)
                {
                    continue;
                }

                _min = Vector2.Min(_min, _screenPoint);
                _max = Vector2.Max(_max, _screenPoint);
                _validCorners++;
            }

            if (_validCorners == 0)
            {
                return false;
            }

            _screenSize = _max - _min;
            _screenCenter = (_min + _max) * 0.5f;
            return true;
        }
    }
}