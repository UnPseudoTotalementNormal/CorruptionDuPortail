using DG.Tweening;
using DG.Tweening.Core;
using DG.Tweening.Plugins.Options;
using UnityEngine;

namespace TransformComposition
{
    /// <summary>
    /// DOTween extensions for TransformLayer.
    /// Allows animating layer properties directly with DOTween.
    /// </summary>
    public static class TransformLayerDOTweenExtensions
    {
        #region Position

        /// <summary>
        /// Tweens the layer's local position to the given value.
        /// </summary>
        public static TweenerCore<Vector3, Vector3, VectorOptions> DOLocalMove(
            this TransformLayer layer, Vector3 endValue, float duration)
        {
            TweenerCore<Vector3, Vector3, VectorOptions> t = DOTween.To(
                () => layer.localPosition,
                x => layer.localPosition = x,
                endValue,
                duration
            );
            t.SetTarget(layer);
            return t;
        }

        /// <summary>
        /// Tweens the layer's local position X to the given value.
        /// </summary>
        public static Tweener DOLocalMoveX(
            this TransformLayer layer, float endValue, float duration)
        {
            Tweener t = DOTween.To(
                () => layer.localPosition,
                x => layer.localPosition = x,
                new Vector3(endValue, layer.localPosition.y, layer.localPosition.z),
                duration
            ).SetOptions(AxisConstraint.X);
            t.SetTarget(layer);
            return t;
        }

        /// <summary>
        /// Tweens the layer's local position Y to the given value.
        /// </summary>
        public static Tweener DOLocalMoveY(
            this TransformLayer layer, float endValue, float duration)
        {
            Tweener t = DOTween.To(
                () => layer.localPosition,
                x => layer.localPosition = x,
                new Vector3(layer.localPosition.x, endValue, layer.localPosition.z),
                duration
            ).SetOptions(AxisConstraint.Y);
            t.SetTarget(layer);
            return t;
        }

        /// <summary>
        /// Tweens the layer's local position Z to the given value.
        /// </summary>
        public static Tweener DOLocalMoveZ(
            this TransformLayer layer, float endValue, float duration)
        {
            Tweener t = DOTween.To(
                () => layer.localPosition,
                x => layer.localPosition = x,
                new Vector3(layer.localPosition.x, layer.localPosition.y, endValue),
                duration
            ).SetOptions(AxisConstraint.Z);
            t.SetTarget(layer);
            return t;
        }

        #endregion

        #region Rotation

        /// <summary>
        /// Tweens the layer's local rotation to the given euler angles.
        /// </summary>
        public static TweenerCore<Quaternion, Vector3, QuaternionOptions> DOLocalRotate(
            this TransformLayer layer, Vector3 endValue, float duration, RotateMode mode = RotateMode.Fast)
        {
            TweenerCore<Quaternion, Vector3, QuaternionOptions> t = DOTween.To(
                () => layer.localRotation,
                x => layer.localRotation = x,
                endValue,
                duration
            );
            t.plugOptions.rotateMode = mode;
            return t;
        }


        #endregion

        #region Scale

        /// <summary>
        /// Tweens the layer's local scale to the given value.
        /// </summary>
        public static TweenerCore<Vector3, Vector3, VectorOptions> DOScale(
            this TransformLayer layer, Vector3 endValue, float duration)
        {
            TweenerCore<Vector3, Vector3, VectorOptions> t = DOTween.To(
                () => layer.localScale,
                x => layer.localScale = x,
                endValue,
                duration
            );
            t.SetTarget(layer);
            return t;
        }

        /// <summary>
        /// Tweens the layer's local scale to the given uniform value.
        /// </summary>
        public static TweenerCore<Vector3, Vector3, VectorOptions> DOScale(
            this TransformLayer layer, float endValue, float duration)
        {
            TweenerCore<Vector3, Vector3, VectorOptions> t = DOTween.To(
                () => layer.localScale,
                x => layer.localScale = x,
                Vector3.one * endValue,
                duration
            );
            t.SetTarget(layer);
            return t;
        }

        /// <summary>
        /// Tweens the layer's local scale X to the given value.
        /// </summary>
        public static Tweener DOScaleX(
            this TransformLayer layer, float endValue, float duration)
        {
            Tweener t = DOTween.To(
                () => layer.localScale,
                x => layer.localScale = x,
                new Vector3(endValue, layer.localScale.y, layer.localScale.z),
                duration
            ).SetOptions(AxisConstraint.X);
            t.SetTarget(layer);
            return t;
        }

        /// <summary>
        /// Tweens the layer's local scale Y to the given value.
        /// </summary>
        public static Tweener DOScaleY(
            this TransformLayer layer, float endValue, float duration)
        {
            Tweener t = DOTween.To(
                () => layer.localScale,
                x => layer.localScale = x,
                new Vector3(layer.localScale.x, endValue, layer.localScale.z),
                duration
            ).SetOptions(AxisConstraint.Y);
            t.SetTarget(layer);
            return t;
        }

        /// <summary>
        /// Tweens the layer's local scale Z to the given value.
        /// </summary>
        public static Tweener DOScaleZ(
            this TransformLayer layer, float endValue, float duration)
        {
            Tweener t = DOTween.To(
                () => layer.localScale,
                x => layer.localScale = x,
                new Vector3(layer.localScale.x, layer.localScale.y, endValue),
                duration
            ).SetOptions(AxisConstraint.Z);
            t.SetTarget(layer);
            return t;
        }

        /// <summary>
        /// Punches the layer's scale with a punch effect.
        /// </summary>
        public static Tweener DOPunchScale(
            this TransformLayer layer, Vector3 punch, float duration, int vibrato = 10, float elasticity = 1f)
        {
            Tweener t = DOTween.Punch(
                () => layer.localScale,
                x => layer.localScale = x,
                punch,
                duration,
                vibrato,
                elasticity
            );
            t.SetTarget(layer);
            return t;
        }

        #endregion

        #region Kill

        /// <summary>
        /// Kills all tweens on this layer.
        /// </summary>
        public static void DOKill(this TransformLayer layer, bool complete = false)
        {
            DOTween.Kill(layer, complete);
        }

        #endregion
    }
}

