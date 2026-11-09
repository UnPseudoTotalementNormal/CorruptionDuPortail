namespace CorruptionDuPortail.Domain
{
    public enum TooltipAction
    {
        None,
        Show,
        Hide,
    }

    /// <summary>
    /// When the hover bubble opens and closes (mockups infobulles v1, T1). The pointer rests on a trigger for
    /// <see cref="OpenDelay"/> before the first bubble, so sweeping the cursor across the power objects does not make
    /// bubbles flash; once a bubble is open (or was closed a moment ago) the next trigger opens its own at once; leaving
    /// keeps it for <see cref="CloseGrace"/>. Triggers are ids, 0 = nothing hovered. Decision-only: the adapter shows
    /// and hides the bubble.
    /// </summary>
    public sealed class TooltipHoverTimeline
    {
        public const float OpenDelay = 0.3f;
        public const float WarmWindow = 0.5f;
        public const float CloseGrace = 0.15f;

        private int _pending;
        private float _pendingSince;
        private float _leftSince = -1f;
        private float _hiddenAt = float.NegativeInfinity;

        /// <summary>The trigger whose bubble is shown, 0 when none.</summary>
        public int Shown { get; private set; }

        /// <summary>One frame: <paramref name="_hovered"/> is the trigger under the pointer (0 = none).</summary>
        public TooltipAction Tick(int _hovered, float _now)
        {
            if (_hovered != 0 && _hovered == Shown)
            {
                _pending = 0;
                _leftSince = -1f;
                return TooltipAction.None;
            }

            if (_hovered != 0)
            {
                _leftSince = -1f;
                if (Shown != 0 || _now - _hiddenAt < WarmWindow)
                {
                    _pending = 0;
                    Shown = _hovered;
                    return TooltipAction.Show;
                }
                if (_pending != _hovered)
                {
                    _pending = _hovered;
                    _pendingSince = _now;
                    return TooltipAction.None;
                }
                if (_now - _pendingSince >= OpenDelay)
                {
                    _pending = 0;
                    Shown = _hovered;
                    return TooltipAction.Show;
                }
                return TooltipAction.None;
            }

            _pending = 0;
            if (Shown == 0)
            {
                return TooltipAction.None;
            }
            if (_leftSince < 0f)
            {
                _leftSince = _now;
                return TooltipAction.None;
            }
            return _now - _leftSince >= CloseGrace ? Hide(_now) : TooltipAction.None;
        }

        /// <summary>Closes the bubble now (click on its trigger, trigger gone). None when nothing was shown.</summary>
        public TooltipAction Hide(float _now)
        {
            if (Shown == 0)
            {
                return TooltipAction.None;
            }
            Shown = 0;
            _hiddenAt = _now;
            _leftSince = -1f;
            return TooltipAction.Hide;
        }
    }
}
