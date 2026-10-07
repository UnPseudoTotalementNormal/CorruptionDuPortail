#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEngine;

namespace Unpseudo.Autoplay
{
    /// <summary>Games register their adapter here (typically from a <c>RuntimeInitializeOnLoadMethod</c>).</summary>
    public static class AutoplayRegistry
    {
        private static readonly Dictionary<string, Func<IAutoplayGame>> factories = new(StringComparer.OrdinalIgnoreCase);

        public static void Register(string _name, Func<IAutoplayGame> _factory) => factories[_name] = _factory;

        /// <summary>The adapter named <paramref name="_name"/>, or the only registered one when no name is given.</summary>
        public static IAutoplayGame Create(string _name)
        {
            if (!string.IsNullOrEmpty(_name))
            {
                return factories.TryGetValue(_name, out Func<IAutoplayGame> _factory) ? _factory() : null;
            }

            foreach (Func<IAutoplayGame> _only in factories.Values)
            {
                return factories.Count == 1 ? _only() : null;
            }
            return null;
        }
    }

    /// <summary>
    /// Reads <c>-autoplay …</c> arguments. Known keys fill <see cref="AutoplayConfig"/>; every other
    /// <c>-autoplay-&lt;key&gt; [value]</c> lands in <see cref="AutoplayConfig.options"/> (a bare flag reads "true").
    /// <list type="bullet">
    /// <item><c>-autoplay</c> (required) · <c>-autoplay-game</c> · <c>-autoplay-scenario</c> · <c>-autoplay-seed</c></item>
    /// <item><c>-autoplay-timescale</c> · <c>-autoplay-port</c> · <c>-autoplay-out</c> (runs root) · <c>-autoplay-timeout</c></item>
    /// <item>window/audio: <c>-autoplay-restore-hwnd H</c> · <c>-autoplay-sound</c> · <c>-autoplay-no-png</c></item>
    /// <item>multi-process: <c>-autoplay-port-strict</c> (never move the agreed port)</item>
    /// <item>speed (tests only): <c>-autoplay-fast-phases &lt;regex&gt;</c> + <c>-autoplay-fast-timescale X</c></item>
    /// <item>animations: <c>-autoplay-record "kindRegex:seconds,…"</c> · <c>-autoplay-record-fps 20</c> · <c>-autoplay-record-width 480</c></item>
    /// </list>
    /// </summary>
    public static class AutoplayCommandLine
    {
        public const string Switch = "-autoplay";
        private const string Prefix = "-autoplay-";

        public static bool IsRequested(string[] _args) => Array.IndexOf(_args, Switch) >= 0;

        public static AutoplayConfig Parse(string[] _args)
        {
            var _config = new AutoplayConfig();
            for (int _i = 0; _i < _args.Length; _i++)
            {
                if (!_args[_i].StartsWith(Prefix, StringComparison.Ordinal))
                {
                    continue;
                }

                string _key = _args[_i].Substring(Prefix.Length);
                bool _hasValue = _i + 1 < _args.Length && !_args[_i + 1].StartsWith("-", StringComparison.Ordinal);
                _config.options[_key] = _hasValue ? _args[++_i] : "true";
            }

            _config.scenario = _config.Option("scenario", "build");
            _config.seed = _config.OptionInt("seed", Environment.TickCount & 0x7FFFFFFF);
            _config.timeScale = ReadFloat(_config.Option("timescale"), _config.timeScale);
            _config.port = (ushort)_config.OptionInt("port", _config.port);
            _config.gameTimeoutRealSeconds = ReadFloat(_config.Option("timeout"), _config.gameTimeoutRealSeconds);
            string _root = _config.Option("out", Path.Combine(Application.persistentDataPath, "AutoplayRuns"));
            _config.outputDirectory = AutoplayRunner.NewRunDirectory(_root, _config.scenario, _config.seed);
            return _config;
        }

        private static float ReadFloat(string _value, float _fallback)
            => float.TryParse(_value, NumberStyles.Float, CultureInfo.InvariantCulture, out float _v) ? _v : _fallback;
    }
}
#endif
