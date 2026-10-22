using System;
using UnityEngine;

namespace Diagnostics.Logs
{
    /// <summary>
    /// The remote switch (Firestore <c>config/logging</c>, string field <c>json</c>). Read before the first upload and
    /// every minute: <see cref="enabled"/> false stops every upload and drops what was waiting. Example:
    /// <code>{"enabled":true,"minLevel":"I","verbosePlayers":["Wouh"],"debugCategories":["net"],"flushSeconds":20,"capPerMinute":300}</code>
    /// </summary>
    [Serializable]
    public sealed class RemoteLogConfig
    {
        public bool enabled;
        /// <summary>Lowest level uploaded: D, I, W or E.</summary>
        public string minLevel = "I";
        /// <summary>Player names, Steam ids or session ids whose Debug lines are uploaded too.</summary>
        public string[] verbosePlayers = Array.Empty<string>();
        /// <summary>Categories whose Debug lines are uploaded for everyone.</summary>
        public string[] debugCategories = Array.Empty<string>();
        public int flushSeconds = 20;
        public int capPerMinute = 300;

        public const int MinFlushSeconds = 5;
        public const int MaxFlushSeconds = 60;

        /// <summary>Null when the text is not a usable config (the caller then keeps logs local).</summary>
        public static RemoteLogConfig Parse(string _json)
        {
            if (string.IsNullOrWhiteSpace(_json))
            {
                return null;
            }
            try
            {
                var _config = JsonUtility.FromJson<RemoteLogConfig>(_json);
                if (_config == null) return null;
                _config.verbosePlayers ??= Array.Empty<string>();
                _config.debugCategories ??= Array.Empty<string>();
                _config.flushSeconds = Math.Max(MinFlushSeconds, Math.Min(MaxFlushSeconds, _config.flushSeconds));
                _config.capPerMinute = _config.capPerMinute <= 0 ? 300 : Math.Max(50, Math.Min(5000, _config.capPerMinute));
                if (string.IsNullOrEmpty(_config.minLevel)) _config.minLevel = "I";
                return _config;
            }
            catch (Exception)
            {
                return null;
            }
        }

        public bool IsVerbose(params string[] _playerKeys)
        {
            foreach (string _key in _playerKeys)
            {
                if (string.IsNullOrEmpty(_key)) continue;
                foreach (string _verbose in verbosePlayers)
                {
                    if (string.Equals(_verbose, _key, StringComparison.OrdinalIgnoreCase)) return true;
                }
            }
            return false;
        }

        /// <summary>True when a line of this level and category goes online.</summary>
        public bool Uploads(char _level, string _category, bool _verbosePlayer)
        {
            if (_level == 'D')
            {
                if (_verbosePlayer) return true;
                foreach (string _cat in debugCategories)
                {
                    if (string.Equals(_cat, _category, StringComparison.OrdinalIgnoreCase)) return true;
                }
            }
            return Rank(_level) >= Rank(minLevel[0]);
        }

        public static int Rank(char _level)
        {
            switch (char.ToUpperInvariant(_level))
            {
                case 'D': return 0;
                case 'I': return 1;
                case 'W': return 2;
                case 'E': return 3;
                default: return 1;
            }
        }
    }
}
