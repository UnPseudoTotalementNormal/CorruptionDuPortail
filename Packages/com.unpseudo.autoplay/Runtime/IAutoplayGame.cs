#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace Unpseudo.Autoplay
{
    /// <summary>
    /// What a game provides so <see cref="AutoplayRunner"/> can play it. Everything game-specific lives behind this
    /// contract (how to boot, host, seat players, start, what the current phase is, when it is over, what to export);
    /// the runner owns the generic part (journal, phase tracking, stall/timeout detection, captures, report).
    /// <para>
    /// The coroutine steps report a failure with <see cref="AutoplayContext.Fail"/> and simply end; the runner stops
    /// at the first failed step. Bots themselves are the game's business (usually a MonoBehaviour started in
    /// <see cref="SetUp"/>), playing through the game's real code paths.
    /// </para>
    /// </summary>
    public interface IAutoplayGame
    {
        /// <summary>Short id used in reports and on the command line (<c>-autoplay-game</c>).</summary>
        string Name { get; }

        /// <summary>Bring the application to the point where it can host (boot scene, menus…).</summary>
        IEnumerator Boot(AutoplayContext _context);

        /// <summary>Start hosting on <paramref name="_port"/> (loopback) and load the gameplay scene.</summary>
        IEnumerator Host(AutoplayContext _context, ushort _port);

        /// <summary>Seat the players (bots / simulated clients), apply the composition, start the bot brains.</summary>
        IEnumerator SetUp(AutoplayContext _context);

        /// <summary>Start the game proper. The runner applies the time scale right after.</summary>
        IEnumerator StartGame(AutoplayContext _context);

        /// <summary>Current phase label; a change is recorded as a phase event (and captured), a phase that never
        /// changes for <see cref="AutoplayConfig.stallRealSeconds"/> fails the run.</summary>
        string Phase { get; }

        /// <summary>True once the game reached its end screen.</summary>
        bool IsOver { get; }

        /// <summary>False when the session died under the run (e.g. the host stopped listening).</summary>
        bool IsAlive { get; }

        /// <summary>One line per player for the report.</summary>
        IEnumerable<string> DescribeRoster();

        /// <summary>Game facts for the report ("days=3", "winner=…").</summary>
        IEnumerable<string> DescribeOutcome();

        /// <summary>Game state exported with every capture: a JSON object (e.g. <c>JsonUtility.ToJson(…)</c>).</summary>
        string ExportStateJson();

        void SetAudioMuted(bool _muted);

        /// <summary>Stop the bots and release game-side resources (called once, even after a failure).</summary>
        void TearDown();
    }

    /// <summary>
    /// Optional: a game that can play several rounds in one process (lever <c>-autoplay-replay N</c>: N more games after
    /// the first). Between two rounds the runner calls <see cref="EndRound"/>, then <see cref="IAutoplayGame.Host"/>,
    /// <see cref="IAutoplayGame.SetUp"/> and <see cref="IAutoplayGame.StartGame"/> again: state that survives a game
    /// (statics, singletons, cached sessions) is exercised the way a player meets it with "play again".
    /// </summary>
    public interface IAutoplayRounds
    {
        /// <summary>Leave the finished game the way players do (end-of-game button, back to the menu) and bring this
        /// process back to where <see cref="IAutoplayGame.Host"/> can start round <paramref name="_nextRound"/>
        /// (2 for the first replay).</summary>
        IEnumerator EndRound(AutoplayContext _context, int _nextRound);
    }

    [Serializable]
    public sealed class AutoplayConfig
    {
        public string scenario = "default";
        public int seed;
        public float timeScale = 4f;
        [Tooltip("First UDP port tried; the runner takes the first free one of [port, port + portRangeSize).")]
        public ushort port = 7850;
        public int portRangeSize = 50;
        public string outputDirectory;
        public float gameTimeoutRealSeconds = 600f;
        public float stallRealSeconds = 150f;
        public bool captureOnPhaseChange = true;
        public float phaseCaptureDelay = 0.6f;

        /// <summary>Free-form game options (<c>-autoplay-&lt;key&gt; [value]</c> on the command line).</summary>
        public Dictionary<string, string> options = new(StringComparer.OrdinalIgnoreCase);

        public bool Flag(string _key) => options.TryGetValue(_key, out string _v) && _v != "false" && _v != "0";

        public string Option(string _key, string _fallback = null) => options.TryGetValue(_key, out string _v) ? _v : _fallback;

        public int OptionInt(string _key, int _fallback)
            => int.TryParse(Option(_key), NumberStyles.Integer, CultureInfo.InvariantCulture, out int _v) ? _v : _fallback;
    }

    /// <summary>What a game step can use: config, journal, captures, the policy seed, and a way to fail the run.</summary>
    public sealed class AutoplayContext
    {
        public AutoplayConfig Config { get; }
        public AutoplayJournal Journal { get; }
        public AutoplayCapture Capture { get; }
        public string FailureReason { get; private set; }
        public bool Failed => FailureReason != null;

        public AutoplayContext(AutoplayConfig _config, AutoplayJournal _journal, AutoplayCapture _capture)
        {
            Config = _config;
            Journal = _journal;
            Capture = _capture;
        }

        /// <summary>Marks the run as failed (first reason wins). The current step should end right after.</summary>
        public void Fail(string _reason)
        {
            FailureReason ??= _reason;
            Journal.Record("run.fail", _reason);
        }

        /// <summary>Bounded wait for a game step: yields until <paramref name="_condition"/> or fails the run.</summary>
        public IEnumerator WaitFor(Func<bool> _condition, float _timeoutRealSeconds, string _failure)
        {
            float _start = Time.realtimeSinceStartup;
            while (!_condition())
            {
                if (Time.realtimeSinceStartup - _start > _timeoutRealSeconds)
                {
                    Fail(_failure);
                    yield break;
                }
                yield return null;
            }
        }
    }
}
#endif
