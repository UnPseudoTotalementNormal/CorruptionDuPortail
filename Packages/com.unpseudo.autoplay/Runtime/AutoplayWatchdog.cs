#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Collections;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using UnityEngine;

namespace Unpseudo.Autoplay
{
    /// <summary>
    /// Optional on an <see cref="IAutoplayGame"/>: how long each step / phase may take, and what the game is waiting on.
    /// Without it the watchdog uses the package defaults and says nothing about the wait.
    /// </summary>
    public interface IAutoplayWatchdogSource
    {
        /// <summary>
        /// Real-seconds budget of a step ("boot", "host", "setup", "start") or of a phase ("phase &lt;label&gt;");
        /// 0 or less = package default. Think "it never takes longer than this when all is well".
        /// </summary>
        float BudgetFor(string _step);

        /// <summary>One line: what the game is waiting on right now (who is awake, which power is open, who has
        /// not voted, which client is still loading…). Goes into every alert.</summary>
        string DescribeWait();
    }

    /// <summary>
    /// Stall detector for a whole run, every step included (boot, host, set-up, start, then each phase). A step is
    /// SUSPECT once it is over its budget AND nothing moved for <c>stall-idle</c> real seconds (no game event in the
    /// journal: a long but busy phase is not a stall). Escalation, each journaled with what the game waits on and a
    /// capture: 1× budget <c>watchdog.slow</c>, 2× <c>watchdog.stall</c>, <c>stall-factor</c>× (3) the run fails with a
    /// report ("watchdog: …") instead of hanging until the global timeout.
    /// <para>
    /// Operator control while it runs: write <c>watchdog-control.txt</c> in the process's run folder with
    /// <c>extend &lt;seconds&gt;</c> (the wait is legit: more budget for the current step), <c>abort [reason]</c> (it is
    /// stuck: fail now, report written) or <c>dump</c> (an alert + capture now). <c>watchdog.json</c> in the same folder
    /// is a heartbeat (step, elapsed, budget, idle, level, wait) refreshed every 2 s.
    /// </para>
    /// Levers: <c>-autoplay-budget "regex:seconds;regex:seconds"</c> (overrides, matched on the step name),
    /// <c>-autoplay-stall-idle S</c> (25), <c>-autoplay-stall-factor F</c> (3), <c>-autoplay-no-watchdog</c>.
    /// </summary>
    public sealed class AutoplayWatchdog
    {
        public const string SlowEvent = "watchdog.slow";
        public const string StallEvent = "watchdog.stall";
        public const string ControlFile = "watchdog-control.txt";

        private static readonly (string pattern, float seconds)[] DefaultBudgets =
        {
            ("^boot$", 120f), ("^host$", 150f), ("^setup$", 300f), ("^start$", 180f), ("^phase ", 90f),
        };

        private readonly AutoplayContext context;
        private readonly IAutoplayWatchdogSource source;
        private readonly (Regex pattern, float seconds)[] overrides;
        private readonly float idleSeconds;
        private readonly float failFactor;
        private readonly bool enabled;

        private string step = "boot";
        private float stepSince = Time.realtimeSinceStartup;
        private float extension;
        private int level;
        private bool stopped;
        private float nextHeartbeat;

        public AutoplayWatchdog(AutoplayContext _context, IAutoplayGame _game)
        {
            context = _context;
            source = _game as IAutoplayWatchdogSource;
            AutoplayConfig _config = _context.Config;
            enabled = !_config.Flag("no-watchdog");
            idleSeconds = ParseFloat(_config.Option("stall-idle"), 25f);
            failFactor = Mathf.Max(1.5f, ParseFloat(_config.Option("stall-factor"), 3f));
            overrides = ParseOverrides(_config.Option("budget"));
        }

        public string Step => step;

        /// <summary>A new step or phase starts: its own budget, alerts re-armed.</summary>
        public void Enter(string _step)
        {
            step = _step;
            stepSince = Time.realtimeSinceStartup;
            extension = 0f;
            level = 0;
        }

        public void Stop() => stopped = true;

        public IEnumerator Run()
        {
            while (!stopped && !context.Failed)
            {
                if (enabled)
                {
                    Tick();
                }
                yield return new WaitForSecondsRealtime(0.5f);
            }
        }

        public float BudgetOf(string _step)
        {
            foreach ((Regex _pattern, float _seconds) in overrides)
            {
                if (_pattern.IsMatch(_step))
                {
                    return _seconds;
                }
            }
            float _fromGame = 0f;
            try
            {
                _fromGame = source?.BudgetFor(_step) ?? 0f;
            }
            catch (Exception _exception)
            {
                context.Journal.Record("watchdog.error", $"BudgetFor({_step}): {_exception.Message}");
            }
            if (_fromGame > 0f)
            {
                return _fromGame;
            }
            foreach ((string _pattern, float _seconds) in DefaultBudgets)
            {
                if (Regex.IsMatch(_step, _pattern))
                {
                    return _seconds;
                }
            }
            return 90f;
        }

        private void Tick()
        {
            float _now = Time.realtimeSinceStartup;
            float _elapsed = _now - stepSince;
            float _idle = _now - context.Journal.LastProgressRealTime;
            ReadControl(BudgetOf(step) + extension);
            if (context.Failed)
            {
                return;
            }
            float _budget = BudgetOf(step) + extension; // after the control: an extension counts from this tick

            bool _suspect = _elapsed > _budget && _idle > idleSeconds;
            if (_suspect && level < 1)
            {
                level = 1;
                Alert(SlowEvent, _elapsed, _budget, _idle);
            }
            if (_suspect && level < 2 && _elapsed > _budget * 2f)
            {
                level = 2;
                Alert(StallEvent, _elapsed, _budget, _idle);
            }
            if (_suspect && _elapsed > _budget * failFactor)
            {
                context.Fail($"watchdog: {step} stalled {_elapsed:0}s (budget {_budget:0}s, idle {_idle:0}s) waiting on {Wait()}");
                stopped = true;
            }

            if (_now >= nextHeartbeat)
            {
                nextHeartbeat = _now + 2f;
                WriteHeartbeat(_elapsed, _budget, _idle);
            }
        }

        private void Alert(string _kind, float _elapsed, float _budget, float _idle)
        {
            context.Journal.Record(_kind, string.Format(CultureInfo.InvariantCulture,
                "{0} elapsed={1:0}s budget={2:0}s idle={3:0}s waiting={4}", step, _elapsed, _budget, _idle, Wait()));
            context.Capture.Request(_kind, 0f);
        }

        private string Wait()
        {
            try
            {
                return source?.DescribeWait() ?? "-";
            }
            catch (Exception _exception)
            {
                return $"<{_exception.GetType().Name}: {_exception.Message}>";
            }
        }

        private void ReadControl(float _budget)
        {
            string _path = Path.Combine(context.Journal.OutputDirectory, ControlFile);
            if (!File.Exists(_path))
            {
                return;
            }
            string _command;
            try
            {
                _command = File.ReadAllText(_path).Trim();
                File.Delete(_path);
            }
            catch (IOException)
            {
                return; // being written: next tick
            }
            string[] _parts = _command.Split(new[] { ' ' }, 2, StringSplitOptions.RemoveEmptyEntries);
            string _verb = _parts.Length > 0 ? _parts[0].ToLowerInvariant() : string.Empty;
            string _rest = _parts.Length > 1 ? _parts[1] : string.Empty;
            switch (_verb)
            {
                case "extend":
                    float _more = ParseFloat(_rest, _budget);
                    extension += _more;
                    level = 0;
                    context.Journal.Record("watchdog.extend", $"{step} +{_more:0}s by the operator");
                    break;
                case "abort":
                    context.Fail($"watchdog: aborted by the operator in {step}: {(_rest.Length > 0 ? _rest : "stuck")} (waiting on {Wait()})");
                    stopped = true;
                    break;
                case "dump":
                    float _now = Time.realtimeSinceStartup;
                    Alert("watchdog.dump", _now - stepSince, _budget, _now - context.Journal.LastProgressRealTime);
                    break;
                default:
                    context.Journal.Record("watchdog.error", $"unknown control '{_command}' (extend <s> | abort [reason] | dump)");
                    break;
            }
        }

        private void WriteHeartbeat(float _elapsed, float _budget, float _idle)
        {
            try
            {
                File.WriteAllText(Path.Combine(context.Journal.OutputDirectory, "watchdog.json"), string.Format(CultureInfo.InvariantCulture,
                    "{{\"step\":{0},\"elapsed\":{1:0.0},\"budget\":{2:0.0},\"idle\":{3:0.0},\"level\":{4},\"waiting\":{5}}}",
                    Quote(step), _elapsed, _budget, _idle, level, Quote(Wait())));
            }
            catch (IOException)
            {
                // heartbeat only: next tick
            }
        }

        private static string Quote(string _text) => "\"" + (_text ?? string.Empty).Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";

        private static float ParseFloat(string _text, float _fallback)
            => float.TryParse(_text, NumberStyles.Float, CultureInfo.InvariantCulture, out float _value) ? _value : _fallback;

        private static (Regex, float)[] ParseOverrides(string _spec)
        {
            if (string.IsNullOrWhiteSpace(_spec))
            {
                return Array.Empty<(Regex, float)>();
            }
            var _list = new System.Collections.Generic.List<(Regex, float)>();
            foreach (string _item in _spec.Split(';'))
            {
                int _colon = _item.LastIndexOf(':');
                if (_colon <= 0 || !float.TryParse(_item.Substring(_colon + 1), NumberStyles.Float, CultureInfo.InvariantCulture, out float _seconds))
                {
                    continue;
                }
                _list.Add((new Regex(_item.Substring(0, _colon), RegexOptions.IgnoreCase), _seconds));
            }
            return _list.ToArray();
        }
    }
}
#endif
