#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.Rendering;

namespace Unpseudo.Autoplay
{
    /// <summary>
    /// Capture points: each one writes <c>NNN-label.json</c> (time, phase, probes, and the game's exported state under
    /// <c>"game"</c>) and, when something is rendered, <c>NNN-label.png</c>. The state file is written in every mode —
    /// it is the variable export; the PNG needs a rendering window (in batchmode the end of frame never comes).
    /// </summary>
    public sealed class AutoplayCapture : MonoBehaviour
    {
        [Serializable]
        private sealed class ProbeValue
        {
            public string name;
            public string value;
        }

        [Serializable]
        private sealed class Header
        {
            public string label;
            public float realTime;
            public float gameTime;
            public int frame;
            public float timeScale;
            public string phase;
            public ProbeValue[] probes;
        }

        private readonly Dictionary<string, Func<object>> probes = new();
        private AutoplayJournal journal;
        private Func<string> phase;
        private Func<string> exportGameState;
        private int index;
        private bool running;
        private bool screenshots = true;

        /// <param name="_screenshots">False keeps the state files but skips PNGs (e.g. secondary processes of a
        /// multi-process run: cheaper, the main window carries the visuals).</param>
        public void Begin(AutoplayJournal _journal, Func<string> _phase, Func<string> _exportGameState, bool _screenshots = true)
        {
            journal = _journal;
            phase = _phase;
            exportGameState = _exportGameState;
            screenshots = _screenshots;
            running = true;
        }

        public void End() => running = false;

        /// <summary>
        /// Registers a named value exported with every capture (e.g. a private field a scenario wants to assert on).
        /// The reader runs at capture time; an exception is exported as its message.
        /// </summary>
        public void AddProbe(string _name, Func<object> _read) => probes[_name] = _read;

        /// <summary>Capture point <paramref name="_delayGameSeconds"/> from now (game time, follows the time scale).</summary>
        public void Request(string _label, float _delayGameSeconds = 0f)
        {
            if (running && isActiveAndEnabled)
            {
                StartCoroutine(Routine(_label, _delayGameSeconds));
            }
        }

        /// <summary>Several capture points of the same moment (e.g. an animation): one per delay.</summary>
        public void RequestBurst(string _label, IEnumerable<float> _delaysGameSeconds)
        {
            foreach (float _delay in _delaysGameSeconds)
            {
                Request($"{_label}-{_delay.ToString("0.00", CultureInfo.InvariantCulture)}s", _delay);
            }
        }

        private IEnumerator Routine(string _label, float _delayGameSeconds)
        {
            if (_delayGameSeconds > 0f)
            {
                yield return new WaitForSeconds(_delayGameSeconds);
            }

            if (!running)
            {
                yield break;
            }

            string _baseName = $"{++index:000}-{Sanitize(_label)}";
            WriteState(_baseName, _label);

            if (!screenshots || Application.isBatchMode || SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null)
            {
                journal.Record("capture.state", $"{_baseName}.json{(screenshots ? " (no screenshot: batchmode/no graphics)" : string.Empty)}");
                yield break;
            }

            yield return new WaitForEndOfFrame();
            if (!running)
            {
                yield break;
            }

            Texture2D _texture = null;
            try
            {
                _texture = ScreenCapture.CaptureScreenshotAsTexture();
                string _file = $"{_baseName}.png";
                File.WriteAllBytes(Path.Combine(journal.OutputDirectory, _file), _texture.EncodeToPNG());
                journal.Record(AutoplayJournal.CaptureEvent, _file);
            }
            catch (Exception _exception)
            {
                journal.Record("capture.error", $"{_label}: {_exception.Message}");
            }
            finally
            {
                if (_texture)
                {
                    Destroy(_texture);
                }
            }
        }

        private void WriteState(string _baseName, string _label)
        {
            try
            {
                var _header = new Header
                {
                    label = _label,
                    realTime = Time.realtimeSinceStartup,
                    gameTime = Time.time,
                    frame = Time.frameCount,
                    timeScale = Time.timeScale,
                    phase = Safe(phase),
                    probes = probes.Select(_p => new ProbeValue { name = _p.Key, value = Safe(() => _p.Value()?.ToString() ?? "null") }).ToArray(),
                };

                string _game = Safe(exportGameState);
                if (string.IsNullOrWhiteSpace(_game) || !_game.TrimStart().StartsWith("{", StringComparison.Ordinal))
                {
                    _game = "{}";
                }

                // JsonUtility cannot nest an arbitrary object: splice the game's own JSON object in as "game".
                string _json = JsonUtility.ToJson(_header, true).TrimEnd();
                _json = _json.Substring(0, _json.Length - 1).TrimEnd() + ",\n    \"game\": " + _game + "\n}\n";
                File.WriteAllText(Path.Combine(journal.OutputDirectory, $"{_baseName}.json"), _json);
            }
            catch (Exception _exception)
            {
                journal.Record("capture.state.error", $"{_label}: {_exception.Message}");
            }
        }

        private static string Safe(Func<string> _read)
        {
            if (_read == null) return null;
            try
            {
                return _read();
            }
            catch (Exception _exception)
            {
                return $"<{_exception.GetType().Name}: {_exception.Message}>";
            }
        }

        public static string Sanitize(string _label)
            => new string(_label.Select(_ch => char.IsLetterOrDigit(_ch) || _ch == '-' || _ch == '.' ? _ch : '_').ToArray());
    }
}
#endif
