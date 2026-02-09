using System;
using AYellowpaper.SerializedCollections;
using UnityEngine;
using static Smartphone.SmartphoneController;

namespace Smartphone
{
    public class SmartphoneApp : MonoBehaviour
    {
        public Sprite icon;

        [SerializeField] private bool _isActive = true;
        public bool isActive
        {
            get => _isActive;
            set
            {
                _isActive = value;
                isActiveChanged?.Invoke();
            }
        }

        [SerializeField] private SerializedDictionary<SwipeDirection, SmartphoneApp> neighborApps = new()
        {
            {SwipeDirection.Up, null},
            {SwipeDirection.Down, null},
            {SwipeDirection.Left, null},
            {SwipeDirection.Right, null}
        };

        public RectTransform canvasGroupTransform;
        public CanvasGroup canvasGroup;
        public bool IsOpen { get; set; } = false;
        public Action onPanelClosed { get; set; }
        public Action onPanelOpened { get; set; }
        public event Action isActiveChanged;

        public SmartphoneApp GetNeighborApp(SwipeDirection direction)
        {
            return neighborApps[direction];
        }

        public void TryClosePanel()
        {
            if (!IsOpen)
            {
                return;
            }
            IsOpen = false;
            canvasGroup.interactable = false;
            canvasGroup.blocksRaycasts = false;
            onPanelClosed?.Invoke();
        }

        public void TryOpenPanel()
        {
            if (IsOpen)
            {
                return;
            }
            IsOpen = true;
            canvasGroup.interactable = true;
            canvasGroup.blocksRaycasts = true;
            onPanelOpened?.Invoke();
        }

        private void Reset()
        {
            canvasGroup = GetComponent<CanvasGroup>();
            if (canvasGroup)
            {
                canvasGroupTransform = canvasGroup.GetComponent<RectTransform>();
            }
        }
    }
}