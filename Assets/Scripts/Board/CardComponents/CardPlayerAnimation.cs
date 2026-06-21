#region

using System;
using Cysharp.Threading.Tasks;
using DG.Tweening;
using TransformComposition;
using UnityEngine;
using UnityEngine.UI;

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

        // Reused buffer for RectTransform.GetWorldCorners (no per-hover alloc beyond the Graphic[] scan).
        private static readonly Vector3[] _worldCorners = new Vector3[4];

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
                // DYNAMIC size: measure the card's real world extents (card face + the deployed vote canvas)
                // so the computed lift floats the WHOLE thing above the table — the vote panel that extends
                // below the card no longer clips into the floor.
                Transform _root = visualComponents.transform;
                if (!MeasureWorldExtents(_root, out float _top, out float _bottom, out float _halfWidth))
                {
                    // No measurable geometry (no active Graphic) → don't pin the pivot to the table; fall back
                    // to the plain lift so a card mid-transition can never be flung to a degenerate pose.
                    hoverLayer.DOLocalMoveY(HOVER_DISPLACEMENT_Y, ZOOM_ANIMATION_DURATION).SetEase(Ease.OutQuint);
                    return;
                }

                Presentation.HoverFocusPose _pose = Presentation.HoverFocusMath.Compute(
                    _root.position, _cam.transform.position,
                    visualComponents.hoverFaceLocalNormal, visualComponents.hoverFaceLocalUp,
                    _top, _bottom, _halfWidth,
                    visualComponents.hoverSurfaceY, visualComponents.hoverFloatOffset);

                // The lift is WORLD; convert to the Hover layer's local space (the root's parent scale).
                float _parentScaleY = _root.parent != null ? _root.parent.lossyScale.y : 1f;
                float _localLift = Mathf.Abs(_parentScaleY) > 1e-5f ? _pose.WorldLift / _parentScaleY : _pose.WorldLift;

                hoverLayer.DOLocalMoveY(_localLift, ZOOM_ANIMATION_DURATION).SetEase(Ease.OutQuint);
                SlerpLayerRotation(hoverLayer, _pose.Rotation);
            }
            else
            {
                hoverLayer.DOLocalMoveY(HOVER_DISPLACEMENT_Y, ZOOM_ANIMATION_DURATION).SetEase(Ease.OutQuint);
            }
        }

        // Measure the card's CURRENT world extents along the face axes (up / right), relative to the root
        // pivot, over every active uGUI Graphic under the card — so the card FACE and the deployed VOTE CANVAS
        // (which extends below) are both included. Projecting the world corner offsets onto the root's current
        // face axes is rotation-invariant, so it gives the rest extents even if the card is mid-hover.
        private bool MeasureWorldExtents(Transform _root, out float _top, out float _bottom, out float _halfWidth)
        {
            Vector3 _faceUp = visualComponents.hoverFaceLocalUp.normalized;
            Vector3 _faceNormal = visualComponents.hoverFaceLocalNormal.normalized;
            Vector3 _wUp = (_root.rotation * _faceUp).normalized;
            Vector3 _wRight = (_root.rotation * Vector3.Cross(_faceUp, _faceNormal)).normalized;
            Vector3 _pivot = _root.position;

            _top = 0f;
            _bottom = 0f;
            _halfWidth = 0f;
            bool _any = false;

            foreach (Graphic _g in visualComponents.GetComponentsInChildren<Graphic>())
            {
                if (_g == null || !_g.isActiveAndEnabled)
                {
                    continue;
                }
                _g.rectTransform.GetWorldCorners(_worldCorners);
                for (int _i = 0; _i < 4; _i++)
                {
                    Vector3 _off = _worldCorners[_i] - _pivot;
                    float _u = Vector3.Dot(_off, _wUp);
                    float _w = Mathf.Abs(Vector3.Dot(_off, _wRight));
                    if (!_any)
                    {
                        _top = _u;
                        _bottom = _u;
                        _halfWidth = _w;
                        _any = true;
                    }
                    else
                    {
                        _top = Mathf.Max(_top, _u);
                        _bottom = Mathf.Min(_bottom, _u);
                        _halfWidth = Mathf.Max(_halfWidth, _w);
                    }
                }
            }

            return _any;
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

