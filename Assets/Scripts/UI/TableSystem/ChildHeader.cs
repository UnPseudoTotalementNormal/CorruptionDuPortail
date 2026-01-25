using System;
using UnityEngine;
using UnityEngine.UI;

namespace UI.TableSystem
{
    public class ChildHeader : MonoBehaviour
    {
        [field:SerializeField] public LayoutElement layoutElement { get; private set; }
        private Header horizontalHeader;
        private Header verticalHeader;

        private void Awake()
        {
            layoutElement = GetComponent<LayoutElement>();
        }

        public void SetHorizontalHeader(Header _header)
        {
            if (horizontalHeader != null)
            {
                horizontalHeader.onPreferredSizeChanged -= OnHeaderPreferredSizeChanged;
            }
            horizontalHeader = _header;
            horizontalHeader.onPreferredSizeChanged += OnHeaderPreferredSizeChanged;
            UpdateLayout();
        }
        
        public void SetVerticalHeader(Header _header)
        {
            if (verticalHeader != null)
            {
                verticalHeader.onPreferredSizeChanged -= OnHeaderPreferredSizeChanged;
            }
            verticalHeader = _header;
            verticalHeader.onPreferredSizeChanged += OnHeaderPreferredSizeChanged;
            UpdateLayout();
        }
        
        private void OnHeaderPreferredSizeChanged(Vector2 _newSize)
        {
            UpdateLayout();
        }

        private void UpdateLayout()
        {
            if (verticalHeader)
            {
                layoutElement.preferredHeight = verticalHeader.layoutElement.preferredHeight;
            }
            if (horizontalHeader)
            {
                layoutElement.preferredWidth = horizontalHeader.layoutElement.preferredWidth;
            }
        }
    }
}