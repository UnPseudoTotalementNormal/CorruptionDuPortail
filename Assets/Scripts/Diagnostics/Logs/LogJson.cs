using System;
using System.Collections;
using System.Globalization;
using System.Text;

namespace Diagnostics.Logs
{
    /// <summary>
    /// Minimal JSON writer for the playtest log lines (NDJSON). Allocation-light, culture-invariant, never throws:
    /// a value it does not know is written as its <c>ToString()</c>.
    /// </summary>
    public static class LogJson
    {
        public static void AppendString(StringBuilder _sb, string _value)
        {
            if (_value == null)
            {
                _sb.Append("null");
                return;
            }
            _sb.Append('"');
            foreach (char _c in _value)
            {
                switch (_c)
                {
                    case '"': _sb.Append("\\\""); break;
                    case '\\': _sb.Append("\\\\"); break;
                    case '\n': _sb.Append("\\n"); break;
                    case '\r': _sb.Append("\\r"); break;
                    case '\t': _sb.Append("\\t"); break;
                    default:
                        if (_c < 0x20)
                        {
                            _sb.Append("\\u").Append(((int)_c).ToString("x4"));
                        }
                        else
                        {
                            _sb.Append(_c);
                        }
                        break;
                }
            }
            _sb.Append('"');
        }

        public static void AppendValue(StringBuilder _sb, object _value, int _depth = 0)
        {
            switch (_value)
            {
                case null: _sb.Append("null"); return;
                case string _s: AppendString(_sb, _s); return;
                case bool _b: _sb.Append(_b ? "true" : "false"); return;
                case Enum _e: AppendString(_sb, _e.ToString()); return;
                case int _i: _sb.Append(_i.ToString(CultureInfo.InvariantCulture)); return;
                case long _l: _sb.Append(_l.ToString(CultureInfo.InvariantCulture)); return;
                case ulong _ul: _sb.Append(_ul.ToString(CultureInfo.InvariantCulture)); return;
                case uint _ui: _sb.Append(_ui.ToString(CultureInfo.InvariantCulture)); return;
                case short _sh: _sb.Append(_sh.ToString(CultureInfo.InvariantCulture)); return;
                case ushort _us: _sb.Append(_us.ToString(CultureInfo.InvariantCulture)); return;
                case byte _by: _sb.Append(_by.ToString(CultureInfo.InvariantCulture)); return;
                case float _f: AppendNumber(_sb, _f); return;
                case double _d: AppendNumber(_sb, _d); return;
                case IEnumerable _list when _depth < 3:
                    _sb.Append('[');
                    bool _first = true;
                    foreach (object _item in _list)
                    {
                        if (!_first) _sb.Append(',');
                        _first = false;
                        AppendValue(_sb, _item, _depth + 1);
                    }
                    _sb.Append(']');
                    return;
                default:
                    string _text;
                    try { _text = _value.ToString(); }
                    catch (Exception) { _text = _value.GetType().Name; }
                    AppendString(_sb, _text);
                    return;
            }
        }

        private static void AppendNumber(StringBuilder _sb, double _value)
        {
            if (double.IsNaN(_value) || double.IsInfinity(_value))
            {
                AppendString(_sb, _value.ToString(CultureInfo.InvariantCulture));
                return;
            }
            _sb.Append(Math.Round(_value, 3).ToString("0.###", CultureInfo.InvariantCulture));
        }

        /// <summary>
        /// Builds a JSON object from alternating key/value arguments (<c>"from", 3, "to", 5</c>). An odd trailing
        /// value is kept under the key <c>"_"</c>. Returns null for no arguments.
        /// </summary>
        public static string Object(object[] _keyValues)
        {
            if (_keyValues == null || _keyValues.Length == 0)
            {
                return null;
            }
            var _sb = new StringBuilder(64);
            _sb.Append('{');
            for (int _i = 0; _i < _keyValues.Length; _i += 2)
            {
                if (_i > 0) _sb.Append(',');
                bool _hasValue = _i + 1 < _keyValues.Length;
                AppendString(_sb, _hasValue ? (_keyValues[_i]?.ToString() ?? "null") : "_");
                _sb.Append(':');
                AppendValue(_sb, _hasValue ? _keyValues[_i + 1] : _keyValues[_i]);
            }
            _sb.Append('}');
            return _sb.ToString();
        }
    }
}
