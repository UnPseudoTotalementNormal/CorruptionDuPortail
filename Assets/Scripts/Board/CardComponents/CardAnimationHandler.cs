#region

using System;
using Cysharp.Threading.Tasks;
using DG.Tweening;
using TransformComposition;
using UnityEngine;

#endregion

namespace Board.CardComponents
{
    /// <summary>
    /// Handles all card animations (hover, flip, etc.).
    /// Single responsibility: animations and visual transformations.
    /// </summary>
    public class CardAnimationHandler : TransformCompositorComponent
    {
        private const float ZOOM_ANIMATION_DURATION = 0.35f;
        private const float HOVER_DISPLACEMENT_Y = 0.35f;
        private const float HOVER_SCALE = 1.15f;
        private const float FLIP_DISPLACEMENT_Y = 4f;
        private const float FLIP_ROTATION_ANGLE = 180f;
        private const float PUNCH_SCALE_INTENSITY = 0.15f;
        private const float PUNCH_DURATION = 0.2f;
        private const float MIN_ZOOM_DURATION = 0.15f;

        // Layer names
        private const string HOVER_LAYER = "Hover";
        private const string FLIP_LAYER = "Flip";
        private const string PUNCH_LAYER = "Punch";

        [Header("Animation Settings")]
        [field:SerializeField] public float hoverZoom { get; private set; } = HOVER_SCALE;
        [field:SerializeField] public float rotateTime { get; private set; } = 1f;

        private float lastZoomStartTime;

        public bool isCardZoomed;
        
        private Vector3 oldPosition;
        private Vector3 oldRotation;
        public Vector3 velocity { get; private set; }
        public Vector3 angularVelocity { get; private set; }
        public bool isCardMoving { get; private set; }
        
        public Action onCardStartMoving;
        public Action onCardStopMoving;
        
        private void Update()
        {
            CalculateVelocity();
        }

        public bool CanUnZoom()
        {
            return Time.time - lastZoomStartTime >= MIN_ZOOM_DURATION;
        }

        public void ZoomIn(Canvas _cardCanvas, Action _onZoomStarted = null)
        {
            if (isCardZoomed)
            {
                return;
            }
            
            var hoverLayer = GetLayer(HOVER_LAYER);
            
            hoverLayer.DOKill();
            hoverLayer.DOScale(hoverZoom, ZOOM_ANIMATION_DURATION).SetEase(Ease.OutQuint);
            hoverLayer.DOLocalMoveY(HOVER_DISPLACEMENT_Y, ZOOM_ANIMATION_DURATION).SetEase(Ease.OutQuint);
            
            lastZoomStartTime = Time.time;
            isCardZoomed = true;
            _cardCanvas.sortingOrder += 1;
            
            _onZoomStarted?.Invoke();
        }

        public void ZoomOut(Canvas _cardCanvas, Action _onZoomEnded = null)
        {
            if (!isCardZoomed)
            {
                return;
            }
            
            var hoverLayer = GetLayer(HOVER_LAYER);
            
            hoverLayer.DOKill();
            hoverLayer.DOScale(1f, ZOOM_ANIMATION_DURATION).SetEase(Ease.OutQuint);
            hoverLayer.DOLocalMoveY(0, ZOOM_ANIMATION_DURATION).SetEase(Ease.OutQuint);
            
            isCardZoomed = false;
            _cardCanvas.sortingOrder -= 1;
            
            _onZoomEnded?.Invoke();
        }

        public void PunchScale()
        {
            var punchLayer = GetLayer(PUNCH_LAYER);
            punchLayer.DOKill(true);
            punchLayer.DOPunchScale(Vector3.one * PUNCH_SCALE_INTENSITY, PUNCH_DURATION, 1, 0.2f);
        }

        public async UniTask FlipToBack(bool _instant = false, Action _onFlipStart = null)
        {
            if (IsOnBackSide())
            {
                return;
            }

            var flipLayer = GetLayer(FLIP_LAYER);

            if (_instant)
            {
                flipLayer.localEulerAngles = new Vector3(0, 0, FLIP_ROTATION_ANGLE);
            }
            else
            {
                _onFlipStart?.Invoke();
                AnimateFlipDisplacement();
                flipLayer.DOLocalRotate(new Vector3(0, 0, -FLIP_ROTATION_ANGLE), rotateTime * 0.75f);
                await UniTask.Delay(TimeSpan.FromSeconds(rotateTime));
            }
        }

        public async UniTask FlipToFront(bool _instant = false, Action _onFlipStart = null)
        {
            if (IsOnFrontSide())
            {
                return;
            }

            var flipLayer = GetLayer(FLIP_LAYER);

            if (_instant)
            {
                flipLayer.localEulerAngles = Vector3.zero;
            }
            else
            {
                _onFlipStart?.Invoke();
                AnimateFlipDisplacement();
                flipLayer.DOLocalRotate(Vector3.zero, rotateTime * 0.75f);
                await UniTask.Delay(TimeSpan.FromSeconds(rotateTime));
            }
        }

        private bool IsOnBackSide()
        {
            var flipLayer = GetLayer(FLIP_LAYER);
            return Mathf.Approximately(Mathf.Abs(flipLayer.localEulerAngles.z), FLIP_ROTATION_ANGLE);
        }

        private bool IsOnFrontSide()
        {
            var flipLayer = GetLayer(FLIP_LAYER);
            return Mathf.Approximately(Mathf.Abs(flipLayer.localEulerAngles.z), 0);
        }

        private void AnimateFlipDisplacement()
        {
            var flipLayer = GetLayer(FLIP_LAYER);
            flipLayer.DOLocalMoveY(FLIP_DISPLACEMENT_Y, rotateTime / 2f).SetEase(Ease.OutQuint).onComplete = () =>
            {
                flipLayer.DOLocalMoveY(0, rotateTime / 2f).SetEase(Ease.OutQuint);
            };
        }
        
        private void CalculateVelocity()
        {
            velocity = (transform.position - oldPosition) / Time.deltaTime;
            angularVelocity = (transform.eulerAngles - oldRotation) / Time.deltaTime;
            
            oldPosition = transform.position;
            oldRotation = transform.eulerAngles;
            
            IsMoving();
        }
        
        /// <summary>
        /// Check if card is moving based on calculated velocity.
        /// </summary>
        private void IsMoving()
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

