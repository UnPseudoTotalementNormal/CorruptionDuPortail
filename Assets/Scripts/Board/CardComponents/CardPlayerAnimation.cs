#region

using System;
using System.Collections.Generic;
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

        // First-person hover: while active, the lift is re-measured EVERY frame (in Update) so it tracks the
        // vote canvas as it slides into place — the panel can't sink into the floor while it deploys. Rotation
        // is a one-shot slerp on enter; only the lift needs the continuous tracking.
        private const float HOVER_LIFT_LERP = 12f;
        private bool _fpsHoverActive;
        private Camera _hoverCamera;
        // Cached at arm-time so Update avoids the per-frame string GetLayer + lossyScale chain walk.
        private TransformLayer _hoverLayerRef;
        private float _hoverParentScaleY = 1f;
        // Reused so the per-frame measurement allocates nothing (GetComponentsInChildren list overload).
        private readonly List<Graphic> _graphicsBuffer = new();

        protected override void Hover(Canvas _cardCanvas)
        {
            var hoverLayer = visualComponents.compositor.GetLayer(HOVER_LAYER);

            hoverLayer.DOKill();
            hoverLayer.DOScale(visualComponents.hoverZoom, ZOOM_ANIMATION_DURATION).SetEase(Ease.OutQuint);

            // First-person seated Vote ONLY: rotate the card to look at the camera + lift by a COMPUTED amount
            // so it floats above the table without clipping (pure HoverFocusMath). Gated by the shared channel
            // so the tilt never leaks into the top-down board/picker mouse hover. SeatedFirstPersonLive is
            // required in ADDITION to the Embodied mode: the whole Vote is Embodied, but the player can arrow to
            // an overhead board-overview camera — there Camera.main is that overhead camera, and aiming the card
            // at it would lay it flat. Only run the look-at when the seated first-person node actually drives it.
            var _channel = visualComponents.cameraModeChannel;
            Camera _cam = Camera.main;
            bool _firstPerson = _channel != null && _channel.Current == Avatars.CameraMode.Embodied
                                && _channel.SeatedFirstPersonLive && _cam != null;

            if (_firstPerson && TryComputeFpsHoverPose(visualComponents.transform, _cam, out Presentation.HoverFocusPose _pose))
            {
                // Start the one-shot look-at rotation + ARM continuous lift tracking: the lift is re-measured
                // every Update so it follows the vote canvas as it slides in (no floor clip during deploy).
                _hoverCamera = _cam;
                _hoverLayerRef = hoverLayer;
                Transform _parent = visualComponents.transform.parent;
                _hoverParentScaleY = _parent != null ? _parent.lossyScale.y : 1f;
                _fpsHoverActive = true;
                SlerpLayerRotation(hoverLayer, _pose.Rotation);
            }
            else
            {
                // Non-FPS phases, or no measurable geometry → the plain flat lift (never a degenerate pose).
                _fpsHoverActive = false;
                hoverLayer.DOLocalMoveY(HOVER_DISPLACEMENT_Y, ZOOM_ANIMATION_DURATION).SetEase(Ease.OutQuint);
            }
        }

        // Per-frame while first-person hovering: re-measure the (deploying) card+vote-canvas bounds and ease the
        // Hover layer's lift toward the height that floats the lowest point at surface+offset. The lift is
        // SIGNED (eases up OR down toward the target, clamped to never go below rest), so the card tracks the
        // vote panel both as it slides DOWN (rise to clear) and back if a transient over-measure peaked it.
        public override void Update()
        {
            base.Update();
            if (!_fpsHoverActive)
            {
                return;
            }

            // Disarm if we left the seated Vote mid-hover (else we'd keep tilting/lifting in a top-down view),
            // if the player arrowed to a board overview (seated FP no longer the live camera), or if the
            // card/compositor is being torn down.
            var _channel = visualComponents != null ? visualComponents.cameraModeChannel : null;
            Transform _root = visualComponents != null ? visualComponents.transform : null;
            if (_channel == null || _channel.Current != Avatars.CameraMode.Embodied || !_channel.SeatedFirstPersonLive
                || _root == null || visualComponents.compositor == null || _hoverLayerRef == null)
            {
                DisarmFpsHover();
                return;
            }

            if (!TryComputeFpsHoverPose(_root, _hoverCamera, out Presentation.HoverFocusPose _pose))
            {
                return;
            }

            float _addLocal = Mathf.Abs(_hoverParentScaleY) > 1e-5f ? _pose.WorldLift / _hoverParentScaleY : _pose.WorldLift;

            Vector3 _lp = _hoverLayerRef.localPosition;
            float _eased = Mathf.Lerp(_lp.y, _lp.y + _addLocal, 1f - Mathf.Exp(-HOVER_LIFT_LERP * Time.deltaTime));
            _lp.y = Mathf.Max(0f, _eased); // never ease BELOW rest, but may ease down toward the target
            _hoverLayerRef.localPosition = _lp;
        }

        // Stop tracking and return the Hover layer to rest (used when the seated Vote ends mid-hover / teardown).
        private void DisarmFpsHover()
        {
            _fpsHoverActive = false;
            _hoverCamera = null;
            if (_hoverLayerRef != null)
            {
                _hoverLayerRef.DOKill();
                _hoverLayerRef.DOLocalMoveY(0f, ZOOM_ANIMATION_DURATION).SetEase(Ease.OutQuint);
                SlerpLayerRotation(_hoverLayerRef, Quaternion.identity);
            }
            _hoverLayerRef = null;
        }

        // Measure the dynamic card+vote-canvas bounds and compute the look-at + world lift. False (no FPS pose)
        // when there is no measurable geometry or no camera.
        private bool TryComputeFpsHoverPose(Transform _root, Camera _cam, out Presentation.HoverFocusPose _pose)
        {
            _pose = default;
            if (_cam == null || _root == null)
            {
                return false;
            }
            if (!MeasureWorldExtents(_root, out float _top, out float _bottom, out float _halfWidth))
            {
                return false;
            }
            _pose = Presentation.HoverFocusMath.Compute(
                _root.position, _cam.transform.position,
                visualComponents.hoverFaceLocalNormal, visualComponents.hoverFaceLocalUp,
                _top, _bottom, _halfWidth,
                visualComponents.hoverSurfaceY, visualComponents.hoverFloatOffset);
            return true;
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

            visualComponents.GetComponentsInChildren(false, _graphicsBuffer);
            foreach (Graphic _g in _graphicsBuffer)
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
            // Stop the per-frame lift tracking BEFORE tweening back, so Update no longer fights the return.
            _fpsHoverActive = false;
            _hoverCamera = null;
            _hoverLayerRef = null;

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

