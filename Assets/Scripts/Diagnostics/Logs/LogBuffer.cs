using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Diagnostics.Logs
{
    /// <summary>One log line before serialization. Short JSON keys keep the files cheap to read.</summary>
    public struct LogRecord
    {
        /// <summary>Unix time in milliseconds (UTC).</summary>
        public long t;
        /// <summary>Level: D(ebug), I(nfo), W(arn), E(rror).</summary>
        public char lv;
        /// <summary>Category (phase, power, vote, chat, net, ui, unity…).</summary>
        public string c;
        /// <summary>Event name ("vote.cast") or, for a captured Unity log, its [TAG].</summary>
        public string e;
        /// <summary>Free text (captured Unity logs).</summary>
        public string m;
        /// <summary>Parameters, already a JSON object.</summary>
        public string p;
        /// <summary>Stack trace (errors and exceptions).</summary>
        public string st;
        /// <summary>Local seat, -1 when unknown.</summary>
        public long s;
        /// <summary>Current phase (game state type name).</summary>
        public string ph;
        /// <summary>Current day (1-based), 0 outside a game.</summary>
        public int d;
        /// <summary>Game id shared by the host and every client of the game.</summary>
        public string g;
    }

    /// <summary>
    /// Thread-safe collector between the game (any thread: Unity's threaded log callback) and the local file.
    /// Merges consecutive identical lines (<c>"r":n</c>) and caps each category per minute so a log storm can't flood
    /// the file or the upload; the number of dropped lines is reported as a <c>log.dropped</c> line.
    /// </summary>
    public sealed class LogBuffer
    {
        public const int MaxMessageChars = 2000;
        public const int MaxStackChars = 4000;
        public const long RepeatWindowMs = 5000;

        private sealed class Pending
        {
            public LogRecord record;
            public int repeat = 1;
            public long lastT;
        }

        private readonly object _lock = new();
        private readonly List<Pending> _pending = new();
        private readonly Dictionary<string, int> _countThisMinute = new();
        private readonly Dictionary<string, int> _droppedThisMinute = new();
        private long _minute = -1;
        private int _errors;
        private int _warnings;
        private long _lines;

        public int CapPerMinute { get; set; }

        public LogBuffer(int _capPerMinute = 300)
        {
            CapPerMinute = _capPerMinute;
        }

        public int Errors { get { lock (_lock) return _errors; } }
        public int Warnings { get { lock (_lock) return _warnings; } }
        public long Lines { get { lock (_lock) return _lines; } }

        public void Add(LogRecord _record)
        {
            _record.m = Truncate(_record.m, MaxMessageChars);
            _record.st = Truncate(_record.st, MaxStackChars);
            lock (_lock)
            {
                if (_record.lv == 'E') _errors++;
                else if (_record.lv == 'W') _warnings++;

                RollMinute(_record.t);

                if (_pending.Count > 0)
                {
                    Pending _last = _pending[_pending.Count - 1];
                    if (_record.t - _last.lastT <= RepeatWindowMs && SameContent(_last.record, _record))
                    {
                        _last.repeat++;
                        _last.lastT = _record.t;
                        return;
                    }
                }

                string _category = _record.c ?? string.Empty;
                _countThisMinute.TryGetValue(_category, out int _count);
                if (_count >= CapPerMinute)
                {
                    _droppedThisMinute.TryGetValue(_category, out int _dropped);
                    _droppedThisMinute[_category] = _dropped + 1;
                    return;
                }
                _countThisMinute[_category] = _count + 1;
                _pending.Add(new Pending { record = _record, lastT = _record.t });
            }
        }

        /// <summary>Takes every pending line, serialized as NDJSON lines (no trailing newline), oldest first.</summary>
        public List<string> Drain(long _nowMs)
        {
            var _out = new List<string>();
            lock (_lock)
            {
                RollMinute(_nowMs);
                var _sb = new StringBuilder(256);
                foreach (Pending _item in _pending)
                {
                    _sb.Clear();
                    Serialize(_sb, _item.record, _item.repeat);
                    _out.Add(_sb.ToString());
                }
                _lines += _pending.Count;
                _pending.Clear();
            }
            return _out;
        }

        public static void Serialize(StringBuilder _sb, LogRecord _r, int _repeat = 1)
        {
            _sb.Append("{\"t\":").Append(_r.t.ToString(CultureInfo.InvariantCulture));
            _sb.Append(",\"lv\":\"").Append(_r.lv).Append('"');
            AppendField(_sb, "c", _r.c);
            AppendField(_sb, "e", _r.e);
            if (_r.s >= 0) _sb.Append(",\"s\":").Append(_r.s.ToString(CultureInfo.InvariantCulture));
            AppendField(_sb, "ph", _r.ph);
            if (_r.d > 0) _sb.Append(",\"d\":").Append(_r.d.ToString(CultureInfo.InvariantCulture));
            AppendField(_sb, "g", _r.g);
            AppendField(_sb, "m", _r.m);
            if (!string.IsNullOrEmpty(_r.p)) _sb.Append(",\"p\":").Append(_r.p);
            AppendField(_sb, "st", _r.st);
            if (_repeat > 1) _sb.Append(",\"r\":").Append(_repeat.ToString(CultureInfo.InvariantCulture));
            _sb.Append('}');
        }

        private static void AppendField(StringBuilder _sb, string _key, string _value)
        {
            if (string.IsNullOrEmpty(_value)) return;
            _sb.Append(",\"").Append(_key).Append("\":");
            LogJson.AppendString(_sb, _value);
        }

        // Caller holds the lock.
        private void RollMinute(long _t)
        {
            long _now = _t / 60000;
            if (_now == _minute) return;
            if (_minute >= 0)
            {
                foreach (KeyValuePair<string, int> _drop in _droppedThisMinute)
                {
                    _pending.Add(new Pending
                    {
                        record = new LogRecord
                        {
                            t = _t, lv = 'W', c = "log", e = "log.dropped", s = -1,
                            p = LogJson.Object(new object[] { "cat", _drop.Key, "n", _drop.Value }),
                        },
                        lastT = _t,
                    });
                }
            }
            _droppedThisMinute.Clear();
            _countThisMinute.Clear();
            _minute = _now;
        }

        private static bool SameContent(LogRecord _a, LogRecord _b) =>
            _a.lv == _b.lv && _a.s == _b.s && _a.d == _b.d
            && string.Equals(_a.c, _b.c, StringComparison.Ordinal)
            && string.Equals(_a.e, _b.e, StringComparison.Ordinal)
            && string.Equals(_a.m, _b.m, StringComparison.Ordinal)
            && string.Equals(_a.p, _b.p, StringComparison.Ordinal)
            && string.Equals(_a.st, _b.st, StringComparison.Ordinal)
            && string.Equals(_a.ph, _b.ph, StringComparison.Ordinal)
            && string.Equals(_a.g, _b.g, StringComparison.Ordinal);

        private static string Truncate(string _s, int _max) =>
            _s == null || _s.Length <= _max ? _s : _s.Substring(0, _max) + "…";
    }
}
