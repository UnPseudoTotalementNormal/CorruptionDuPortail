#if DEVELOPMENT_BUILD && !UNITY_EDITOR
using System;
using System.Collections;
using System.Globalization;
using System.IO;
using UnityEngine;

namespace Autoplay
{
    /// <summary>
    /// Dev-build entry point for autoplay: a player launched with <c>-autoplay</c> plays one complete game by itself
    /// (host + simulated bots, see <see cref="AutoplaySession"/>), writes its report / state files / screenshots, then
    /// quits with exit code 0 (GameEnding) or 1 (failure). Absent the flag it does nothing — normal dev builds are
    /// untouched, and release builds do not even compile it.
    /// <para>
    /// <c>Game.exe -autoplay -autoplay-out &lt;dir&gt; [-autoplay-seed N] [-autoplay-bots 7] [-autoplay-timescale 4]
    /// [-autoplay-port 7850] [-autoplay-visual-picker] [-autoplay-sound] [-autoplay-restore-hwnd H] -screen-fullscreen 0 -screen-width 1600 -screen-height 900</c>
    /// </para>
    /// </summary>
    public sealed class AutoplayBootstrap : MonoBehaviour
    {
        private AutoplaySessionConfig config;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Initialize()
        {
            string[] _args = Environment.GetCommandLineArgs();
            if (Array.IndexOf(_args, "-autoplay") < 0)
            {
                return;
            }

            int _seed = ReadInt(_args, "-autoplay-seed", Environment.TickCount & 0x7FFFFFFF);
            var _config = new AutoplaySessionConfig
            {
                seed = _seed,
                bots = ReadInt(_args, "-autoplay-bots", 7),
                timeScale = ReadFloat(_args, "-autoplay-timescale", 4f),
                port = (ushort)ReadInt(_args, "-autoplay-port", 7850),
                scenario = ReadString(_args, "-autoplay-scenario", "build-solohost-8p-randomvalid"),
            };
            // Real picker on screen (blur veil, lifted cards) + capture bursts at each opening, instead of invisible picks.
            _config.options.visualPicker = Array.IndexOf(_args, "-autoplay-visual-picker") >= 0;
            string _root = ReadString(_args, "-autoplay-out", Path.Combine(Application.persistentDataPath, "AutoplayRuns"));
            _config.outputDirectory = AutoplaySession.NewRunDirectory(_root, _config.scenario, _seed);

            var _go = new GameObject("AutoplayBootstrap");
            DontDestroyOnLoad(_go);
            // Stay out of the user's way: no focus stealing, no cursor lock, muted (unless -autoplay-sound).
            _go.AddComponent<AutoplayWindowGuard>().Configure(
                ReadLong(_args, "-autoplay-restore-hwnd", 0), Array.IndexOf(_args, "-autoplay-sound") < 0);
            _go.AddComponent<AutoplayBootstrap>().config = _config;
        }

        private IEnumerator Start()
        {
            var _result = new AutoplaySessionResult();
            yield return AutoplaySession.RunSoloHost(config, _result);
            yield return new WaitForSecondsRealtime(1f);
            Application.Quit(_result.failure == null ? 0 : 1);
        }

        private static string ReadString(string[] _args, string _name, string _fallback)
        {
            int _index = Array.IndexOf(_args, _name);
            return _index >= 0 && _index + 1 < _args.Length ? _args[_index + 1] : _fallback;
        }

        private static int ReadInt(string[] _args, string _name, int _fallback)
            => int.TryParse(ReadString(_args, _name, null), NumberStyles.Integer, CultureInfo.InvariantCulture, out int _value) ? _value : _fallback;

        private static long ReadLong(string[] _args, string _name, long _fallback)
            => long.TryParse(ReadString(_args, _name, null), NumberStyles.Integer, CultureInfo.InvariantCulture, out long _value) ? _value : _fallback;

        private static float ReadFloat(string[] _args, string _name, float _fallback)
            => float.TryParse(ReadString(_args, _name, null), NumberStyles.Float, CultureInfo.InvariantCulture, out float _value) ? _value : _fallback;
    }
}
#endif
