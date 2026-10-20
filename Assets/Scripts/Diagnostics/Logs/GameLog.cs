using System;
using System.Diagnostics;
using UnityEngine;

namespace Diagnostics.Logs
{
    /// <summary>
    /// Structured playtest log. One call = one named event with its parameters as alternating key/value pairs:
    /// <code>GameLog.Info("vote", "vote.cast", "from", 3, "to", 5);</code>
    /// The local seat, role, phase, day and game id are added to every line. Every entry point is
    /// <c>[Conditional(CDP_PLAYTEST_LOGS)]</c>: in a build without the define (the Editor, tests, a public build) the
    /// calls disappear at compile time, arguments included. The define is added by <c>CdpBuild</c> to CI playtest
    /// builds only. Without a running <c>PlaytestLogRuntime</c> (Editor, autoplay without upload…) the calls are no-ops.
    /// Never log per frame: the buffer caps each category per minute.
    /// Arguments are evaluated by the caller, outside the logger's guard: when computing them could throw (lookups in
    /// game state), wrap the call in <see cref="Guarded"/> so a logging bug never breaks the game.
    /// </summary>
    public static class GameLog
    {
        public const string Define = "CDP_PLAYTEST_LOGS";

        /// <summary>Set by the runtime when logging is live in this process.</summary>
        internal static LogBuffer Sink;

        private static ulong s_seat;
        private static bool s_hasSeat;
        private static string s_role;
        private static string s_phase;
        private static int s_day;
        private static string s_gameId;
        private static bool s_isHost;

        public static string GameId => s_gameId;
        public static bool IsHost => s_isHost;

        [Conditional(Define)]
        public static void Verbose(string _category, string _event, params object[] _keyValues) => Write('D', _category, _event, null, _keyValues);

        [Conditional(Define)]
        public static void Info(string _category, string _event, params object[] _keyValues) => Write('I', _category, _event, null, _keyValues);

        [Conditional(Define)]
        public static void Warn(string _category, string _event, params object[] _keyValues) => Write('W', _category, _event, null, _keyValues);

        [Conditional(Define)]
        public static void Error(string _category, string _event, params object[] _keyValues) => Write('E', _category, _event, null, _keyValues);

        /// <summary>A picture of the game state (host, phase start): what Claude reads first to understand a bug.</summary>
        [Conditional(Define)]
        public static void Snapshot(string _event, params object[] _keyValues) => Write('I', "state", _event, null, _keyValues);

        /// <summary>A moment someone pointed at (bug report key): stands out in the timeline.</summary>
        [Conditional(Define)]
        public static void Mark(string _event, params object[] _keyValues) => Write('W', "mark", _event, null, _keyValues);

        /// <summary>
        /// Runs a logging call whose arguments are computed from game state, swallowing anything it throws:
        /// <code>GameLog.Guarded(() => GameLog.Info("net", "player.left", "name", _seat.GetPlayerName()));</code>
        /// Compiled out with the rest (the lambda is never even allocated without the define).
        /// </summary>
        [Conditional(Define)]
        public static void Guarded(Action _log)
        {
            if (Sink == null) return;
            try { _log(); }
            catch (Exception) { /* logging never breaks the game */ }
        }

        /// <summary>Phase or day change on this peer: updates the context and writes <c>phase.enter</c> / <c>day.enter</c>.</summary>
        [Conditional(Define)]
        public static void SetPhase(string _phase, int _day)
        {
            bool _phaseChanged = _phase != s_phase;
            bool _dayChanged = _day != s_day;
            if (!_phaseChanged && !_dayChanged) return;
            s_phase = _phase;
            s_day = _day;
            Write('I', "phase", _phaseChanged ? "phase.enter" : "day.enter", null, null);
        }

        /// <summary>Null or empty clears it (back to the lobby or the menu).</summary>
        [Conditional(Define)]
        public static void SetGameId(string _gameId)
        {
            if (string.IsNullOrEmpty(_gameId)) _gameId = null;
            if (_gameId == s_gameId) return;
            if (_gameId == null)
            {
                Write('I', "game", "game.left", null, null);
                s_gameId = null;
                return;
            }
            s_gameId = _gameId;
            Write('I', "game", "game.id", null, new object[] { "host", s_isHost });
        }

        [Conditional(Define)]
        public static void SetLocalSeat(ulong _seat, bool _isHost)
        {
            if (s_hasSeat && _seat == s_seat && _isHost == s_isHost) return;
            s_seat = _seat;
            s_hasSeat = true;
            s_isHost = _isHost;
            Write('I', "net", "seat.local", null, new object[] { "host", _isHost });
        }

        [Conditional(Define)]
        public static void SetLocalRole(string _role)
        {
            if (string.IsNullOrEmpty(_role)) _role = null;
            if (_role == s_role) return;
            s_role = _role;
            if (_role != null) Write('I', "game", "role.local", null, null);
        }

        /// <summary>Leaving the network session (despawn): the next lines belong to no game.</summary>
        [Conditional(Define)]
        public static void ClearContext()
        {
            SetGameId(null);
            s_hasSeat = false;
            s_role = null;
            s_phase = null;
            s_day = 0;
        }

        /// <summary>Captured Unity log (runtime only): level, [TAG] as event name, text, stack.</summary>
        internal static void WriteUnityLog(char _level, string _tag, string _message, string _stack) =>
            Push(_level, "unity", _tag, _message, null, _stack);

        private static void Write(char _level, string _category, string _event, string _message, object[] _keyValues)
        {
            if (Sink == null) return;
            string _params;
            try { _params = LogJson.Object(_keyValues); }
            catch (Exception) { _params = null; }
            Push(_level, _category, _event, _message, _params, null);
        }

        private static void Push(char _level, string _category, string _event, string _message, string _params, string _stack)
        {
            LogBuffer _sink = Sink;
            if (_sink == null) return;
            try
            {
                _sink.Add(new LogRecord
                {
                    t = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                    lv = _level,
                    c = _category,
                    e = _event,
                    m = _message,
                    p = _params,
                    st = _stack,
                    s = s_seat,
                    hasSeat = s_hasSeat,
                    ro = s_role,
                    ph = s_phase,
                    d = s_day,
                    g = s_gameId,
                });
            }
            catch (Exception)
            {
                // Logging never breaks the game.
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            Sink = null;
            s_seat = 0;
            s_hasSeat = false;
            s_role = null;
            s_phase = null;
            s_day = 0;
            s_gameId = null;
            s_isHost = false;
        }
    }
}
