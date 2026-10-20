using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Diagnostics.Logs
{
    /// <summary>
    /// Writes a Firestore REST document body (<c>{"fields":{"x":{"stringValue":"…"}}}</c>) and remembers the field names
    /// for the PATCH <c>updateMask</c>.
    /// </summary>
    public sealed class FirestoreFields
    {
        private readonly StringBuilder _sb = new(256);
        private readonly List<string> _names = new();

        public IReadOnlyList<string> Names => _names;

        public FirestoreFields String(string _name, string _value) => Typed(_name, "stringValue", _value ?? string.Empty, true);
        public FirestoreFields Int(string _name, long _value) =>
            Typed(_name, "integerValue", _value.ToString(CultureInfo.InvariantCulture), true);
        public FirestoreFields Bool(string _name, bool _value) => Typed(_name, "booleanValue", _value ? "true" : "false", false);

        public FirestoreFields Timestamp(string _name, long _unixMs) =>
            Typed(_name, "timestampValue",
                DateTimeOffset.FromUnixTimeMilliseconds(_unixMs).UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture),
                true);

        public FirestoreFields StringArray(string _name, IEnumerable<string> _values)
        {
            Separator(_name);
            _sb.Append("{\"arrayValue\":{\"values\":[");
            bool _first = true;
            foreach (string _value in _values)
            {
                if (!_first) _sb.Append(',');
                _first = false;
                _sb.Append("{\"stringValue\":");
                LogJson.AppendString(_sb, _value ?? string.Empty);
                _sb.Append('}');
            }
            _sb.Append("]}}");
            return this;
        }

        public string ToJson() => "{\"fields\":{" + _sb + "}}";

        /// <summary>Query string of a PATCH that writes exactly these fields (creates the document if missing).</summary>
        public string UpdateMaskQuery()
        {
            var _query = new StringBuilder();
            foreach (string _name in _names)
            {
                _query.Append(_query.Length == 0 ? "?" : "&").Append("updateMask.fieldPaths=").Append(Uri.EscapeDataString(_name));
            }
            return _query.ToString();
        }

        private FirestoreFields Typed(string _name, string _type, string _value, bool _quote)
        {
            Separator(_name);
            _sb.Append("{\"").Append(_type).Append("\":");
            if (_quote) LogJson.AppendString(_sb, _value);
            else _sb.Append(_value);
            _sb.Append('}');
            return this;
        }

        private void Separator(string _name)
        {
            if (_names.Count > 0) _sb.Append(',');
            _names.Add(_name);
            LogJson.AppendString(_sb, _name);
            _sb.Append(':');
        }
    }
}
