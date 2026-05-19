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
    public class CardRoleAnimation : BaseCardAnimation
    {
        private const float ZOOM_ANIMATION_DURATION = 0.35f;
        private const float HOVER_ZOOM = 1.1f;
        private const float HOVER_DISPLACEMENT_Z = 4f;
        private const string HOVER_LAYER = "Hover";

        protected override void Hover(Canvas _cardCanvas)
        {
            var hoverLayer = visualComponents.compositor.GetLayer(HOVER_LAYER);
            
            hoverLayer.DOKill();
            hoverLayer.DOLocalMove(visualComponents.compositor.transform.forward * HOVER_DISPLACEMENT_Z, ZOOM_ANIMATION_DURATION).SetEase(Ease.OutQuint);
            hoverLayer.DOScale(HOVER_ZOOM, ZOOM_ANIMATION_DURATION).SetEase(Ease.OutQuint);
        }

        protected override void UnHover(Canvas _cardCanvas)
        {
            var hoverLayer = visualComponents.compositor.GetLayer(HOVER_LAYER);
            
            hoverLayer.DOKill();
            hoverLayer.DOLocalMove(Vector3.zero, ZOOM_ANIMATION_DURATION).SetEase(Ease.OutQuint);
            hoverLayer.DOScale(1f, ZOOM_ANIMATION_DURATION).SetEase(Ease.OutQuint);
        }

        public override void OnClick()
        {
            var clickLayer = visualComponents.compositor.GetLayer("Click");
            clickLayer.DOKill(true);
            clickLayer.DOPunchScale(Vector3.one * 0.15f, 0.2f, 1, 0.2f);
        }

        public override UniTask FlipToBack(bool _instant = false, Action _onFlipStart = null)
        {
            Debug.LogWarning("FlipToBack should not be called on role cards");
            return UniTask.CompletedTask;
        }

        public override UniTask FlipToFront(bool _instant = false, Action _onFlipStart = null)
        {
            Debug.LogWarning("FlipToFront should not be called on role cards");
            return UniTask.CompletedTask;
        }
    }
}

