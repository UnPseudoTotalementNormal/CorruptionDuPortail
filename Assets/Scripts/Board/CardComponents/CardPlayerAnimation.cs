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
        // Flat hover lift (non-FPS phases: board/picker mouse hover). The dramatic first-person look-at hover
        // is gated to the seated Vote (CameraModeChannel == Embodied) and computes its own lift — see Hover.
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

            // First-person seated Vote ONLY: rotate the card to look at the camera + lift by a COMPUTED amount
            // so it floats above the table without clipping (pure HoverFocusMath). Gated by the shared channel
            // so the tilt never leaks into the top-down board/picker mouse hover.
            var _channel = visualComponents.cameraModeChannel;
            Camera _cam = Camera.main;
            bool _firstPerson = _channel != null && _channel.Current == Avatars.CameraMode.Embodied && _cam != null;

            if (_firstPerson)
            {
                Presentation.HoverFocusPose _pose = Presentation.HoverFocusMath.Compute(
                    visualComponents.transform.position, _cam.transform.position,
                    visualComponents.hoverFaceLocalNormal, visualComponents.hoverFaceLocalUp,
                    visualComponents.hoverHalfHeight, visualComponents.hoverHalfWidth,
                    visualComponents.hoverSurfaceY, visualComponents.hoverFloatOffset, visualComponents.hoverRootScaleY);

                hoverLayer.DOLocalMoveY(_pose.LocalLiftY, ZOOM_ANIMATION_DURATION).SetEase(Ease.OutQuint);
                SlerpLayerRotation(hoverLayer, _pose.Rotation);
            }
            else
            {
                hoverLayer.DOLocalMoveY(HOVER_DISPLACEMENT_Y, ZOOM_ANIMATION_DURATION).SetEase(Ease.OutQuint);
            }
        }

        protected override void UnHover(Canvas _cardCanvas)
        {
            var hoverLayer = visualComponents.compositor.GetLayer(HOVER_LAYER);

            hoverLayer.DOKill();
            hoverLayer.DOScale(1f, ZOOM_ANIMATION_DURATION).SetEase(Ease.OutQuint);
            hoverLayer.DOLocalMoveY(0, ZOOM_ANIMATION_DURATION).SetEase(Ease.OutQuint);
            SlerpLayerRotation(hoverLayer, Quaternion.identity);
        }

        // Slerp the layer's local rotation to a target quaternion (the layer's DOTween helper only takes euler,
        // which can spin badly toward an arbitrary look-at orientation — slerp is clean and shortest-path).
        private void SlerpLayerRotation(TransformComposition.TransformLayer _layer, Quaternion _target)
        {
            Quaternion _from = _layer.localRotation;
            DOTween.To(() => 0f, _t => _layer.localRotation = Quaternion.Slerp(_from, _target, _t), 1f, ZOOM_ANIMATION_DURATION)
                .SetEase(Ease.OutQuint)
                .SetTarget(_layer);
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
            isBackSide = true;

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
            isBackSide = false;

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

        private bool isBackSide = false;

        private bool IsOnBackSide()
        {
            return isBackSide;
        }

        private bool IsOnFrontSide()
        {
            return !isBackSide;
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

