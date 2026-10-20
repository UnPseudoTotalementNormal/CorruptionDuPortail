using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace Diagnostics.Logs
{
    /// <summary>
    /// Cuts the local NDJSON file into upload chunks (Firestore documents are capped at 1 MiB) and summarizes each one
    /// (time span, game ids, levels) for the chunk document.
    /// </summary>
    public sealed class LogChunk
    {
        public const int MaxChunkBytes = 900 * 1024;

        private static readonly Regex TimeRegex = new("\"t\":(\\d+)", RegexOptions.CultureInvariant);
        private static readonly Regex LevelRegex = new("\"lv\":\"(\\w)\"", RegexOptions.CultureInvariant);
        private static readonly Regex CategoryRegex = new("\"c\":\"([^\"]*)\"", RegexOptions.CultureInvariant);
        private static readonly Regex GameRegex = new("\"g\":\"([^\"]+)\"", RegexOptions.CultureInvariant);

        public string lines = string.Empty;
        public int lineCount;
        public long t0;
        public long t1;
        public int errors;
        public int warnings;
        public readonly List<string> gameIds = new();

        /// <summary>
        /// Length in bytes of the complete lines at the start of <paramref name="_bytes"/> (up to <paramref name="_count"/>)
        /// that fit in <paramref name="_maxBytes"/>. A line longer than the cap is taken alone (never blocks the cursor).
        /// 0 = no complete line yet.
        /// </summary>
        public static int CompleteLinesLength(byte[] _bytes, int _count, int _maxBytes = MaxChunkBytes)
        {
            int _limit = Math.Min(_count, _maxBytes);
            for (int _i = _limit - 1; _i >= 0; _i--)
            {
                if (_bytes[_i] == (byte)'\n') return _i + 1;
            }
            for (int _i = _limit; _i < _count; _i++)
            {
                if (_bytes[_i] == (byte)'\n') return _i + 1;
            }
            return 0;
        }

        /// <summary>Keeps the lines <paramref name="_keep"/> accepts and summarizes them.</summary>
        public static LogChunk Build(string _text, Func<char, string, bool> _keep)
        {
            var _chunk = new LogChunk();
            var _sb = new StringBuilder(_text.Length);
            foreach (string _line in _text.Split('\n'))
            {
                if (_line.Length == 0) continue;
                Match _level = LevelRegex.Match(_line);
                char _lv = _level.Success ? _level.Groups[1].Value[0] : 'I';
                Match _cat = CategoryRegex.Match(_line);
                if (_keep != null && !_keep(_lv, _cat.Success ? _cat.Groups[1].Value : string.Empty)) continue;

                Match _time = TimeRegex.Match(_line);
                if (_time.Success && long.TryParse(_time.Groups[1].Value, out long _t))
                {
                    if (_chunk.t0 == 0 || _t < _chunk.t0) _chunk.t0 = _t;
                    if (_t > _chunk.t1) _chunk.t1 = _t;
                }
                if (_lv == 'E') _chunk.errors++;
                else if (_lv == 'W') _chunk.warnings++;
                Match _game = GameRegex.Match(_line);
                if (_game.Success && !_chunk.gameIds.Contains(_game.Groups[1].Value)) _chunk.gameIds.Add(_game.Groups[1].Value);

                _sb.Append(_line).Append('\n');
                _chunk.lineCount++;
            }
            _chunk.lines = _sb.ToString();
            return _chunk;
        }
    }
}
