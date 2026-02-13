#region

using System;
using Cysharp.Threading.Tasks;
using DG.Tweening;
using TransformComposition;
using UnityEngine;

#endregion

namespace Board.CardComponents
{
    [System.Serializable]
    public class CardPlayerAnimation : BaseCardAnimation
    {
        private const float ZOOM_ANIMATION_DURATION = 0.35f;
        private const float HOVER_DISPLACEMENT_Y = 0.35f;
        private const float FLIP_DISPLACEMENT_Y = 4f;
        private const float FLIP_ROTATION_ANGLE = 180f;
        private const float PUNCH_SCALE_INTENSITY = 0.15f;
        private const float PUNCH_DURATION = 0.2f;

        private const string HOVER_LAYER = "Hover";
        private const string FLIP_LAYER = "Flip";
        private const string PUNCH_LAYER = "Punch";

        protected override void Hover(Canvas _cardCanvas)
        {
            var hoverLayer = visualComponents.compositor.GetLayer(HOVER_LAYER);
            
            hoverLayer.DOKill();
            hoverLayer.DOScale(visualComponents.hoverZoom, ZOOM_ANIMATION_DURATION).SetEase(Ease.OutQuint);
            hoverLayer.DOLocalMoveY(HOVER_DISPLACEMENT_Y, ZOOM_ANIMATION_DURATION).SetEase(Ease.OutQuint);
        }

        protected override void UnHover(Canvas _cardCanvas)
        {
            var hoverLayer = visualComponents.compositor.GetLayer(HOVER_LAYER);
            
            hoverLayer.DOKill();
            hoverLayer.DOScale(1f, ZOOM_ANIMATION_DURATION).SetEase(Ease.OutQuint);
            hoverLayer.DOLocalMoveY(0, ZOOM_ANIMATION_DURATION).SetEase(Ease.OutQuint);
        }

        public override void OnClick()
        {
            var punchLayer = visualComponents.compositor.GetLayer(PUNCH_LAYER);
            punchLayer.DOKill(true);
            punchLayer.DOPunchScale(Vector3.one * PUNCH_SCALE_INTENSITY, PUNCH_DURATION, 1, 0.2f);
        }

        public override async UniTask FlipToBack(bool _instant = false, Action _onFlipStart = null)
        {
            if (IsOnBackSide())
            {
                return;
            }

            var flipLayer = visualComponents.compositor.GetLayer(FLIP_LAYER);

            if (_instant)
            {
                flipLayer.localEulerAngles = new Vector3(0, 0, FLIP_ROTATION_ANGLE);
            }
            else
            {
                _onFlipStart?.Invoke();
                AnimateFlipDisplacement();
                flipLayer.DOLocalRotate(new Vector3(0, 0, -FLIP_ROTATION_ANGLE), visualComponents.rotateTime * 0.75f);
                await UniTask.Delay(TimeSpan.FromSeconds(visualComponents.rotateTime));
            }
        }

        public override async UniTask FlipToFront(bool _instant = false, Action _onFlipStart = null)
        {
            if (IsOnFrontSide())
            {
                return;
            }

            var flipLayer = visualComponents.compositor.GetLayer(FLIP_LAYER);

            if (_instant)
            {
                flipLayer.localEulerAngles = Vector3.zero;
            }
            else
            {
                _onFlipStart?.Invoke();
                AnimateFlipDisplacement();
                flipLayer.DOLocalRotate(Vector3.zero, visualComponents.rotateTime * 0.75f);
                await UniTask.Delay(TimeSpan.FromSeconds(visualComponents.rotateTime));
            }
        }

        private bool IsOnBackSide()
        {
            var flipLayer = visualComponents.compositor.GetLayer(FLIP_LAYER);
            return Mathf.Approximately(Mathf.Abs(flipLayer.localEulerAngles.z), FLIP_ROTATION_ANGLE);
        }

        private bool IsOnFrontSide()
        {
            var flipLayer = visualComponents.compositor.GetLayer(FLIP_LAYER);
            return Mathf.Approximately(Mathf.Abs(flipLayer.localEulerAngles.z), 0);
        }

        private void AnimateFlipDisplacement()
        {
            var flipLayer = visualComponents.compositor.GetLayer(FLIP_LAYER);
            flipLayer.DOLocalMoveY(FLIP_DISPLACEMENT_Y, visualComponents.rotateTime / 2f).SetEase(Ease.OutQuint).onComplete = () =>
            {
                flipLayer.DOLocalMoveY(0, visualComponents.rotateTime / 2f).SetEase(Ease.OutQuint);
            };
        }
    }
}

