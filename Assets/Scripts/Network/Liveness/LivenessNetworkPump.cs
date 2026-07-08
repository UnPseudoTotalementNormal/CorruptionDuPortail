using System;
using System.Diagnostics;
using System.Threading;
using CorruptionDuPortail.Domain;
using Cysharp.Threading.Tasks;

namespace Network.Liveness
{
    /// <summary>
    /// [LIVENESS B2] Production <see cref="ILivenessPump"/> (arch-liveness-heartbeat §8.3). A UniTask realtime
    /// loop — NOT <c>GameManager.Update</c> (framerate/timeScale coupling) and NOT <c>OnNetworkSpawn</c> (the
    /// trap that bit vote-skip). It is the ONLY place wall-clock lives (§8.1): each wake it measures real
    /// elapsed via <see cref="ILivenessClock"/> and runs the stall-guard; the tracker stays a pure integer
    /// beat-count and never sees a second.
    ///
    /// Per wake, once the carrier is ready: send the beat (harmless even after a stall), then — unless the
    /// pump itself was starved (GC / scene-load / timeScale=0 / focus-loss, detected by
    /// <see cref="LivenessPumpPolicy.ShouldSkipTick"/>) — call <c>tick</c> once. Skipping the tick on a stall
    /// stops a single host hitch from evicting the whole match. <see cref="UniTask.Delay"/> uses
    /// <see cref="DelayType.Realtime"/> so a <c>timeScale=0</c> pause cannot freeze the clock.
    /// </summary>
    public sealed class LivenessNetworkPump : ILivenessPump
    {
        private readonly ILivenessClock _clock;
        private readonly TimeSpan _beatPeriod;
        private readonly long _beatPeriodTicks;
        private readonly System.Func<bool> _isReady;
        private readonly System.Action _sendBeat;
        private readonly System.Action _tick;

        private CancellationTokenSource _cts;
        private long _lastWakeTicks;

        /// <param name="clock">Monotonic clock (Stopwatch/QPC ticks) — the pump's ONLY time source.</param>
        /// <param name="beatPeriodSeconds">Beat cadence in seconds (1s at the locked 1 Hz rate).</param>
        /// <param name="isReady">Gate: the pump no-ops while false (bridge not spawned / session down).</param>
        /// <param name="sendBeat">Emit this side's beat (client heartbeat OR server keepalives).</param>
        /// <param name="tick">Advance the tracker one beat window (skipped on a detected stall).</param>
        public LivenessNetworkPump(ILivenessClock clock, double beatPeriodSeconds, System.Func<bool> isReady, System.Action sendBeat, System.Action tick)
        {
            _clock = clock;
            _beatPeriod = TimeSpan.FromSeconds(beatPeriodSeconds);
            _beatPeriodTicks = (long)(Stopwatch.Frequency * beatPeriodSeconds);
            _isReady = isReady;
            _sendBeat = sendBeat;
            _tick = tick;
        }

        public bool IsRunning { get; private set; }

        public void Start()
        {
            if (IsRunning)
            {
                return;
            }
            IsRunning = true;
            _cts = new CancellationTokenSource();
            _lastWakeTicks = _clock.NowTicks;
            RunAsync(_cts.Token).Forget();
        }

        public void Stop()
        {
            if (!IsRunning)
            {
                return;
            }
            IsRunning = false;
            if (_cts != null)
            {
                _cts.Cancel();
                _cts.Dispose();
                _cts = null;
            }
        }

        private async UniTaskVoid RunAsync(CancellationToken _cancellationToken)
        {
            while (!_cancellationToken.IsCancellationRequested)
            {
                bool _canceled = await UniTask
                    .Delay(_beatPeriod, DelayType.Realtime, PlayerLoopTiming.Update, _cancellationToken)
                    .SuppressCancellationThrow();
                if (_canceled || _cancellationToken.IsCancellationRequested)
                {
                    return;
                }

                long _now = _clock.NowTicks;
                long _elapsed = _now - _lastWakeTicks;
                _lastWakeTicks = _now;

                // No-op cleanly while the bridge is not spawned / session is down.
                if (_isReady == null || !_isReady())
                {
                    continue;
                }

                // A throw here must NEVER unwind the loop: _sendBeat is an RPC send and _tick drives
                // tracker.Tick -> PeerLost -> the full leave pipeline, any of which could transiently throw in a
                // spawn/despawn flicker window. If it unwound, RunAsync would terminate and — because Start() is
                // guarded by IsRunning — the pump could not restart: ALL liveness detection would die silently
                // for the rest of the session (arch code-review, MAJOR). Catch, log, keep beating.
                try
                {
                    _sendBeat?.Invoke();

                    // Stall-guard: a starved wake does NOT count as a missed beat for anyone.
                    if (!LivenessPumpPolicy.ShouldSkipTick(_elapsed, _beatPeriodTicks))
                    {
                        _tick?.Invoke();
                    }
                }
                catch (Exception _e)
                {
                    UnityEngine.Debug.LogException(_e);
                }
            }
        }
    }
}
