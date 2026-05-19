#region

using UnityEngine;

#endregion

namespace Extensions
{
    public static class RectTransformExtensions
    {
        
        
        public static void SetToFullStretch(this RectTransform _rectTransform)
        {
            _rectTransform.anchorMin = Vector2.zero;
            _rectTransform.anchorMax = Vector2.one;
            _rectTransform.offsetMin = Vector2.zero;
            _rectTransform.offsetMax = Vector2.zero;
        }
    }
}