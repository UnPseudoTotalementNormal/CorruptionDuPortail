namespace Reticle
{
    /// <summary>Per-tick hover decision: which target the reticle just LEFT and which it just ENTERED
    /// (<see cref="ReticleHover.None"/> = nothing). At most one of each per tick.</summary>
    public readonly struct ReticleHoverResult
    {
        public readonly int Exited;
        public readonly int Entered;

        public ReticleHoverResult(int _exited, int _entered)
        {
            Exited = _exited;
            Entered = _entered;
        }
    }

    /// <summary>
    /// PURE reticle hover hysteresis (EditMode-testable, no Unity deps). Targets are opaque int ids
    /// (a collider/GameObject instance id in the live interactor; <see cref="None"/> = no target).
    ///
    /// Two timers stop the center-reticle from flickering as it skims a row of cards:
    ///  • EXIT requires the ray to stay OFF the current target for <c>exitDwell</c> seconds before the
    ///    target is dropped — a one-frame graze never un-hovers.
    ///  • SWITCH requires a DIFFERENT target to persist for <c>switchDebounce</c> seconds before we move
    ///    to it — skating along a seam doesn't strobe between two cards.
    /// (PR1: this is the targeting hysteresis. The structural lifted-card anti-jitter envelope is PR2.)
    /// </summary>
    public sealed class ReticleHover
    {
        public const int None = -1;

        private readonly float _exitDwell;
        private readonly float _switchDebounce;

        private int _current = None;
        private int _candidate = None;
        private float _candidateAge;

        public int Current => _current;

        public ReticleHover(float _exitDwell, float _switchDebounce)
        {
            this._exitDwell = _exitDwell;
            this._switchDebounce = _switchDebounce;
        }

        /// <summary>Advance one frame with the ray's current hit (<see cref="None"/> if it hit nothing).</summary>
        public ReticleHoverResult Tick(int _hit, float _deltaTime)
        {
            // Still on the current target → reset any pending change, no events.
            if (_hit == _current)
            {
                _candidate = _current;
                _candidateAge = 0f;
                return new ReticleHoverResult(None, None);
            }

            // The hit differs from the current target: age the candidate (count THIS frame too, so a single
            // long frame can still cross the threshold; reset the clock when the candidate changes).
            if (_hit != _candidate)
            {
                _candidate = _hit;
                _candidateAge = _deltaTime;
            }
            else
            {
                _candidateAge += _deltaTime;
            }

            // Losing the target to empty space uses the (longer) exit dwell; moving to a real target uses
            // the switch debounce. Commit only once the candidate has out-waited its threshold.
            float _threshold = _hit == None ? _exitDwell : _switchDebounce;
            if (_candidateAge < _threshold)
            {
                return new ReticleHoverResult(None, None);
            }

            int _exited = _current != None ? _current : None;
            int _entered = _hit != None ? _hit : None;
            _current = _hit;
            _candidateAge = 0f;
            return new ReticleHoverResult(_exited, _entered);
        }

        /// <summary>Force-clear (e.g. the reticle is deactivated). Returns the target to exit, if any.</summary>
        public int Reset()
        {
            int _exited = _current;
            _current = None;
            _candidate = None;
            _candidateAge = 0f;
            return _exited;
        }
    }
}
