#if UNITY_EDITOR || DEVELOPMENT_BUILD
using UnityEngine;
using UnityEngine.UI;

namespace Autoplay
{
    /// <summary>
    /// Breakage test for the real-input mode (<c>-autoplay-real-input-mask &lt;GameObject name&gt;</c>): keeps a
    /// transparent, raycast-blocking overlay named <c>AutoplayMask</c> over the named object's screen rect, so every
    /// click on it must end in <c>input.miss … hit=…AutoplayMask</c>. Proves a hidden or covered button is caught.
    /// </summary>
    public sealed class AutoplayInputMask : MonoBehaviour
    {
        public const string MaskName = "AutoplayMask";

        private string targetName;
        private RectTransform mask;
        private GameObject target;
        private readonly Vector3[] corners = new Vector3[4];

        public static AutoplayInputMask Create(string _targetName)
        {
            var _root = new GameObject("AutoplayInputMaskCanvas");
            DontDestroyOnLoad(_root);
            var _canvas = _root.AddComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _canvas.sortingOrder = short.MaxValue;
            _root.AddComponent<GraphicRaycaster>();

            var _maskObject = new GameObject(MaskName, typeof(RectTransform), typeof(Image));
            _maskObject.transform.SetParent(_root.transform, false);
            var _image = _maskObject.GetComponent<Image>();
            _image.color = new Color(0f, 0f, 0f, 0f);
            _image.raycastTarget = true;

            var _component = _root.AddComponent<AutoplayInputMask>();
            _component.targetName = _targetName;
            _component.mask = (RectTransform)_maskObject.transform;
            _component.mask.anchorMin = _component.mask.anchorMax = Vector2.zero;
            _component.mask.pivot = Vector2.zero;
            _component.mask.gameObject.SetActive(false);
            return _component;
        }

        private void LateUpdate()
        {
            if (target == null || !target.activeInHierarchy)
            {
                target = GameObject.Find(targetName);
            }

            bool _visible = target != null && target.activeInHierarchy && TryScreenRect(target, out Vector2 _min, out Vector2 _max)
                            && ApplyRect(_min, _max);
            if (mask.gameObject.activeSelf != _visible)
            {
                mask.gameObject.SetActive(_visible);
            }
        }

        private bool ApplyRect(Vector2 _min, Vector2 _max)
        {
            // Padded so a click anywhere on the target lands on the mask.
            mask.anchoredPosition = _min - new Vector2(8f, 8f);
            mask.sizeDelta = _max - _min + new Vector2(16f, 16f);
            return true;
        }

        private bool TryScreenRect(GameObject _object, out Vector2 _min, out Vector2 _max)
        {
            _min = _max = default;
            if (_object.transform is RectTransform _rect)
            {
                Canvas _canvas = _object.GetComponentInParent<Canvas>();
                if (_canvas == null)
                {
                    return false;
                }
                Canvas _root = _canvas.rootCanvas;
                Camera _camera = _root.renderMode == RenderMode.ScreenSpaceOverlay ? null : (_root.worldCamera ? _root.worldCamera : Camera.main);
                _rect.GetWorldCorners(corners);
                Vector2 _a = RectTransformUtility.WorldToScreenPoint(_camera, corners[0]);
                Vector2 _b = RectTransformUtility.WorldToScreenPoint(_camera, corners[2]);
                _min = Vector2.Min(_a, _b);
                _max = Vector2.Max(_a, _b);
                return true;
            }

            Collider _collider = _object.GetComponentInChildren<Collider>();
            Camera _main = Camera.main;
            if (_collider == null || _main == null)
            {
                return false;
            }
            Bounds _bounds = _collider.bounds;
            _min = new Vector2(float.MaxValue, float.MaxValue);
            _max = new Vector2(float.MinValue, float.MinValue);
            for (int _corner = 0; _corner < 8; _corner++)
            {
                Vector3 _offset = Vector3.Scale(_bounds.extents, new Vector3((_corner & 1) == 0 ? -1 : 1, (_corner & 2) == 0 ? -1 : 1, (_corner & 4) == 0 ? -1 : 1));
                Vector3 _point = _main.WorldToScreenPoint(_bounds.center + _offset);
                if (_point.z <= 0f)
                {
                    return false; // behind the camera: nothing to cover on screen
                }
                _min = Vector2.Min(_min, _point);
                _max = Vector2.Max(_max, _point);
            }
            return true;
        }
    }
}
#endif
