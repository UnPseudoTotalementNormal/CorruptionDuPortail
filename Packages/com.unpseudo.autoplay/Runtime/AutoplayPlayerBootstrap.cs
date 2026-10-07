#if DEVELOPMENT_BUILD && !UNITY_EDITOR
using System;
using System.Collections;
using UnityEngine;

namespace Unpseudo.Autoplay
{
    /// <summary>
    /// Dev-build entry point: a player launched with <c>-autoplay</c> plays one complete game by itself through the
    /// registered <see cref="IAutoplayGame"/>, writes its run folder, then quits with exit code 0 (completed) or 1.
    /// The window stays out of the way (<see cref="AutoplayWindowGuard"/>). Absent the flag nothing happens; release
    /// builds do not even compile it. Launch with tools: <c>Tools~/launch-background.ps1</c>.
    /// </summary>
    public sealed class AutoplayPlayerBootstrap : MonoBehaviour
    {
        private IAutoplayGame game;
        private AutoplayConfig config;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Initialize()
        {
            string[] _args = Environment.GetCommandLineArgs();
            if (!AutoplayCommandLine.IsRequested(_args))
            {
                return;
            }

            AutoplayConfig _config = AutoplayCommandLine.Parse(_args);
            IAutoplayGame _game = AutoplayRegistry.Create(_config.Option("game"));
            if (_game == null)
            {
                Debug.LogError($"{AutoplayJournal.LogTag} no IAutoplayGame registered (AutoplayRegistry.Register) — quitting.");
                Application.Quit(2);
                return;
            }

            var _go = new GameObject("AutoplayPlayerBootstrap");
            DontDestroyOnLoad(_go);
            long.TryParse(_config.Option("restore-hwnd", "0"), out long _restoreHwnd);
            bool _mute = !_config.Flag("sound");
            _go.AddComponent<AutoplayWindowGuard>().Configure(_restoreHwnd, _mute ? () => _game.SetAudioMuted(true) : null,
                _config.Flag("real-input"));
            var _bootstrap = _go.AddComponent<AutoplayPlayerBootstrap>();
            _bootstrap.game = _game;
            _bootstrap.config = _config;
        }

        private IEnumerator Start()
        {
            var _result = new AutoplayResult();
            yield return AutoplayRunner.Run(game, config, _result);
            yield return new WaitForSecondsRealtime(1f);
            Application.Quit(_result.Succeeded ? 0 : 1);
        }
    }
}
#endif
