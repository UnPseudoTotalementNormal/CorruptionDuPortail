using System;
using UI.InfoTable;
using UnityEngine;
using UnityEngine.UI;

namespace UI.TableSystem
{
    public class Header : MonoBehaviour
    { 
        public RectTransform rectTransform { get; private set; }
        [field:SerializeField] public LayoutElement layoutElement { get; private set; }
        [field:SerializeField] public InfoHeaderText headerText { get; private set; }
        
        public Vector2 preferredSize { get; private set; }
        
        public event Action<Vector2> onPreferredSizeChanged;

        private void Awake()
        {
            rectTransform = GetComponent<RectTransform>();
        }

        private void LateUpdate()
        {
            Vector2 _currentPreferredSize = new Vector2(layoutElement.preferredWidth, layoutElement.preferredHeight);
            if (preferredSize != _currentPreferredSize)
            {
                preferredSize = _currentPreferredSize;
                onPreferredSizeChanged?.Invoke(preferredSize);
            }
        }
    }
}
