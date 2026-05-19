#region

using System;
using Cysharp.Threading.Tasks;
using UnityEngine;

#endregion

namespace Board.CardComponents
{
    /// <summary>
    /// Base abstract class for card animations.
    /// Contains common logic for velocity calculation and movement detection.
    /// </summary>
    [System.Serializable]
    public abstract class BaseCardAnimation : ICardAnimation
    {
        protected const float MIN_ZOOM_DURATION = 0.15f;
        
        protected CardVisualComponents visualComponents;
        protected float lastZoomStartTime;
        
        private Vector3 oldPosition;
        private Vector3 oldRotation;
        private Transform cardTransform;
        
        public bool isCardZoomed { get; protected set; }
        public Vector3 velocity { get; private set; }
        public Vector3 angularVelocity { get; private set; }
        public bool isCardMoving { get; private set; }
        
        public event Action onCardStartMoving;
        public event Action onCardStopMoving;
        
        public virtual void Initialize(CardVisualComponents _visualComponents)
        {
            visualComponents = _visualComponents;
            cardTransform = visualComponents.transform;
            oldPosition = cardTransform.position;
            oldRotation = cardTransform.eulerAngles;
        }
        
        public virtual void Update()
        {
            CalculateVelocity();
        }
        
        public bool CanUnZoom()
        {
            return Time.time - lastZoomStartTime >= MIN_ZOOM_DURATION;
        }
        
        public void OnHover(Canvas _cardCanvas, Action _onZoomStarted = null)
        {
            if (isCardZoomed)
            {
                return;
            }
            
            Hover(_cardCanvas);
            
            lastZoomStartTime = Time.time;
            isCardZoomed = true;
            _cardCanvas.sortingOrder += 1;
            
            _onZoomStarted?.Invoke();
        }
        
        public void OnUnHover(Canvas _cardCanvas, Action _onZoomEnded = null)
        {
            if (!isCardZoomed)
            {
                return;
            }
            
            UnHover(_cardCanvas);
            
            isCardZoomed = false;
            _cardCanvas.sortingOrder -= 1;
            
            _onZoomEnded?.Invoke();
        }
        
        protected abstract void Hover(Canvas _cardCanvas);
        protected abstract void UnHover(Canvas _cardCanvas);
        
        public abstract void OnClick();
        public abstract UniTask FlipToBack(bool _instant = false, Action _onFlipStart = null);
        public abstract UniTask FlipToFront(bool _instant = false, Action _onFlipStart = null);
        
        protected void CalculateVelocity()
        {
            if (cardTransform == null)
            {
                return;
            }
            
            velocity = (cardTransform.position - oldPosition) / Time.deltaTime;
            angularVelocity = (cardTransform.eulerAngles - oldRotation) / Time.deltaTime;
            
            oldPosition = cardTransform.position;
            oldRotation = cardTransform.eulerAngles;
            
            UpdateMovingState();
        }
        
        private void UpdateMovingState()
        {
            float velocityMagnitude = velocity.magnitude;
            float angularVelocityMagnitude = angularVelocity.magnitude;
            const float velocityThreshold = 0.01f; 
            const float angularVelocityThreshold = 0.01f;

            if (velocityMagnitude > velocityThreshold || angularVelocityMagnitude > angularVelocityThreshold)
            {
                if (!isCardMoving)
                {
                    onCardStartMoving?.Invoke();
                    isCardMoving = true;
                }
            }
            else
            {
                if (isCardMoving)
                {
                    isCardMoving = false;
                    onCardStopMoving?.Invoke();
                }
            }
        }
    }
}

