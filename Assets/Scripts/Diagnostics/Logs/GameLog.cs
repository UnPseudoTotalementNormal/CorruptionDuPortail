using System;
using System.Diagnostics;
using UnityEngine;

namespace Diagnostics.Logs
{
    /// <summary>
    /// Structured playtest log. One call = one named event with its parameters as alternating key/value pairs:
    /// <code>GameLog.Info("vote", "vote.cast", "from", 3, "to", 5);</code>
    /// The local seat, phase, day and game id are added to every line. Every entry point is
    /// <c>[Conditional(CDP_PLAYTEST_LOGS)]</c>: in a build without the define (the Editor, tests, a public build) the
    /// calls disappear at compile time, arguments included. The define is added by <c>CdpBuild</c> to CI playtest
    /// builds only. Without a running <c>PlaytestLogRuntime</c> (Editor, autoplay without upload…) the calls are no-ops.
    /// Never log per frame: the buffer caps each category per minute.
    /// </summary>
    public static class GameLog
    {
        public const string Define = "CDP_PLAYTEST_LOGS";

        /// <summary>Set by the runtime when logging is live in this process.</summary>
        internal static LogBuffer Sink;

        private static long s_seat = -1;
        private static string s_phase;
        private static int s_day;
        private static string s_gameId;
        private static bool s_isHost;

        public static string GameId => s_gameId;
        public static bool IsHost => s_isHost;
        public static long Seat => s_seat;

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

        /// <summary>Phase or day change on this peer: updates the context and writes <c>phase.enter</c>.</summary>
        [Conditional(Define)]
        public static void SetPhase(string _phase, int _day)
        {
            if (_phase == s_phase && _day == s_day) return;
            s_phase = _phase;
            s_day = _day;
            Write('I', "phase", "phase.enter", null, null);
        }

        [Conditional(Define)]
        public static void SetGameId(string _gameId)
        {
            if (string.IsNullOrEmpty(_gameId) || _gameId == s_gameId) return;
            s_gameId = _gameId;
            Write('I', "game", "game.id", null, new object[] { "host", s_isHost });
        }

        [Conditional(Define)]
        public static void SetLocalSeat(ulong _seat, bool _isHost)
        {
            if ((long)_seat == s_seat && _isHost == s_isHost) return;
            s_seat = (long)_seat;
            s_isHost = _isHost;
            Write('I', "net", "seat.local", null, new object[] { "host", _isHost });
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
            s_seat = -1;
            s_phase = null;
            s_day = 0;
            s_gameId = null;
            s_isHost = false;
        }
    }
}
