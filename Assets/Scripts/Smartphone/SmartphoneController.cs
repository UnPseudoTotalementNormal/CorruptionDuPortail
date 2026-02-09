using System;
using System.Collections;
using System.Collections.Generic;
using Board.BoardCameraSystem;
using Controllers;
using DG.Tweening;
using Extensions;
using Unity.Cinemachine;
using UnityEngine;

namespace Smartphone
{
    public class SmartphoneController : NetworkController
    {
        public enum SwipeDirection
        {
            Up,
            Down,
            Left,
            Right
        }
        private Dictionary<SwipeDirection, Vector2> swipeDirectionVectors = new()
        {
            {SwipeDirection.Up, Vector2.up},
            {SwipeDirection.Down, Vector2.down},
            {SwipeDirection.Left, Vector2.left},
            {SwipeDirection.Right, Vector2.right}
        };

        public bool IsOpen { get; set; } = false;
        public Action onPanelClosed { get; set; }
        public Action onPanelOpened { get; set; }
        
        public SmartphoneApp currentApp { get; private set; }

        [SerializeField] private BoardCamera openOnCamera;

        [SerializeField] private RectTransform phoneCanvasTransform;
        [SerializeField] private SmartphoneApp defaultApp;
        [SerializeField] private Transform appParent;

        [SerializeField] private CanvasGroup phoneCanvasGroup;

        [SerializeField] private Vector3 phoneOpenLocalPosition;
        [SerializeField] private Vector3 phoneCloseLocalPosition;
        
        public event Action onAppChanged;

        private void Awake()
        {
            foreach (SmartphoneApp smartphoneApp in appParent.GetComponentsInChildren<SmartphoneApp>(true))
            {
                smartphoneApp.canvasGroupTransform.anchoredPosition = phoneCanvasTransform.sizeDelta * new Vector2(10f, 10f);
            }
            
            GoToApp(defaultApp);

            openOnCamera.onCameraActivated += TryOpenPanel;
            openOnCamera.onCameraDeactivated += TryClosePanel;
        }

        private void Start()
        {
            StartCoroutine(WaitAFrameAndClosePhone());
            IEnumerator WaitAFrameAndClosePhone()
            {
                yield return null;
                IsOpen = true;
                TryClosePanel();
            }
        }

        
        public void GoToApp(SmartphoneApp smartphoneApp, SwipeDirection swipeDirection = SwipeDirection.Up)
        {
            if (currentApp)
            {
                currentApp.TryClosePanel();
                MoveApp(currentApp, swipeDirectionVectors[swipeDirection], false);
            }
            currentApp = smartphoneApp;
            if (currentApp)
            {
                currentApp?.TryOpenPanel();
                MoveApp(currentApp, swipeDirectionVectors[swipeDirection], true);
            }
            onAppChanged?.Invoke();
        }

        private void MoveApp(SmartphoneApp smartphoneApp, Vector2 direction, bool directionOnCenter)
        {
            if (directionOnCenter)
            {
                smartphoneApp.canvasGroupTransform.anchoredPosition = direction * phoneCanvasTransform.sizeDelta;
            }
            Vector2 target = directionOnCenter ? Vector2.zero : -direction * phoneCanvasTransform.sizeDelta;
            smartphoneApp.canvasGroupTransform.DOAnchorPos(target, 0.5f).SetEase(Ease.OutQuint);
        }

        public void OnSwipe(SwipeDirection swipeDirection)
        {
            SmartphoneApp nextApp = currentApp.GetNeighborApp(swipeDirection);
            if (nextApp == null)
            {
                Debug.Log("Swiped " + swipeDirection + ", no neighbor app");
                return;
            }
            while (!nextApp.isActive)
            {
                nextApp = nextApp.GetNeighborApp(swipeDirection);
                if (nextApp == null)
                {
                    break;
                }
            }
            Debug.Log("Swiped " + swipeDirection + ", next app: " + (nextApp != null ? nextApp.name : "null"));
            if (nextApp != null)
            {
                GoToApp(nextApp, swipeDirection);
            }
        }

        public void TryClosePanel()
        {
            if (!IsOpen)
            {
                return;
            }
            phoneCanvasGroup.DoHideGroup();
            IsOpen = false;
            transform.DOLocalMove(phoneCloseLocalPosition, 0.5f).SetEase(Ease.OutQuint);
        }

        public void TryOpenPanel()
        {
            if (IsOpen)
            {
                return;
            }
            phoneCanvasGroup.DoShowGroup();
            IsOpen = true; 
            transform.DOLocalMove(phoneOpenLocalPosition, 0.5f).SetEase(Ease.OutQuint);
        }
    }
}
