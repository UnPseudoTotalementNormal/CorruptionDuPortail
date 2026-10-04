#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEngine.Rendering;

namespace Unpseudo.Autoplay
{
    /// <summary>One measured value per recorded frame (a position, a scale, an alpha, a probe…). NaN = not available.</summary>
    public sealed class AutoplayTrack
    {
        public string Name { get; }
        public Func<float> Read { get; }

        public AutoplayTrack(string _name, Func<float> _read)
        {
            Name = _name;
            Read = _read;
        }

        public static AutoplayTrack Value(string _name, Func<float> _read) => new(_name, _read);

        /// <summary>World position x/y/z and uniform scale of a transform resolved each frame (it may appear late).</summary>
        public static IEnumerable<AutoplayTrack> TransformOf(string _name, Func<Transform> _resolve)
        {
            yield return new AutoplayTrack(_name + ".x", () => _resolve() is Transform _t && _t ? _t.position.x : float.NaN);
            yield return new AutoplayTrack(_name + ".y", () => _resolve() is Transform _t && _t ? _t.position.y : float.NaN);
            yield return new AutoplayTrack(_name + ".z", () => _resolve() is Transform _t && _t ? _t.position.z : float.NaN);
            yield return new AutoplayTrack(_name + ".scale", () => _resolve() is Transform _t && _t ? _t.lossyScale.x : float.NaN);
        }
    }

    /// <summary>Optional: a game that can say what to measure while an animation triggered by an event plays.</summary>
    public interface IAutoplayAnimationSource
    {
        IEnumerable<AutoplayTrack> TracksFor(string _eventKind, string _detail);
    }

    /// <summary>
    /// Animation recorder: when a journal event matches a trigger (<c>-autoplay-record "kindRegex:seconds,…"</c>), records
    /// the next N game seconds frame by frame into <c>rec-NNN-&lt;label&gt;/</c>: one downscaled PNG per frame
    /// (<c>f000.png</c>…), <c>tracks.csv</c> (time + every track value per frame) and <c>manifest.json</c>.
    /// During the window game time advances by a fixed step per rendered frame (<see cref="Time.captureFramerate"/>,
    /// time scale 1), so the sequence is deterministic and independent of the machine's speed; the previous pacing is
    /// restored afterwards. Needs a rendering window (not batchmode). Tools~/contact_sheet.py turns a recording into one
    /// labelled grid image (+ GIF); scenario checks of type "animation" assert on tracks.csv.
    /// </summary>
    public sealed class AutoplayRecorder : MonoBehaviour
    {
        /// <summary>True while a recording owns the time pacing (the runner must not change the time scale then).</summary>
        public static bool IsRecording { get; private set; }

        private readonly List<(Regex kind, float seconds)> triggers = new();
        private AutoplayJournal journal;
        private Func<string, string, IEnumerable<AutoplayTrack>> tracksFor;
        private int fps = 20;
        private int width = 480;
        private int index;

        public void Begin(AutoplayJournal _journal, string _spec, int _fps, int _width,
            Func<string, string, IEnumerable<AutoplayTrack>> _tracksFor)
        {
            journal = _journal;
            fps = Mathf.Clamp(_fps, 5, 60);
            width = Mathf.Clamp(_width, 160, 1920);
            tracksFor = _tracksFor;
            foreach (string _part in _spec.Split(','))
            {
                int _colon = _part.LastIndexOf(':');
                string _kind = _colon > 0 ? _part.Substring(0, _colon) : _part;
                float _seconds = _colon > 0 && float.TryParse(_part.Substring(_colon + 1), NumberStyles.Float, CultureInfo.InvariantCulture, out float _s) ? _s : 1f;
                triggers.Add((new Regex("^(" + _kind.Trim() + ")$", RegexOptions.IgnoreCase), _seconds));
            }
            journal.Recorded += OnRecorded;
        }

        private void OnDestroy()
        {
            if (journal != null)
            {
                journal.Recorded -= OnRecorded;
            }
            IsRecording = false;
        }

        private void OnRecorded(AutoplayJournal.Entry _entry)
        {
            if (IsRecording || !isActiveAndEnabled)
            {
                return; // one recording at a time
            }

            foreach ((Regex _kind, float _seconds) in triggers)
            {
                if (_kind.IsMatch(_entry.kind))
                {
                    StartCoroutine(Record(_entry.kind, _entry.detail, _seconds));
                    return;
                }
            }
        }

        private IEnumerator Record(string _kind, string _detail, float _seconds)
        {
            if (Application.isBatchMode || SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null)
            {
                journal.Record("record.skip", $"{_kind}: needs a rendering window");
                yield break;
            }

            IsRecording = true;
            string _label = AutoplayCapture.Sanitize($"{_kind}-{_detail}");
            if (_label.Length > 60) _label = _label.Substring(0, 60);
            string _dir = Path.Combine(journal.OutputDirectory, $"rec-{++index:000}-{_label}");
            Directory.CreateDirectory(_dir);

            List<AutoplayTrack> _tracks = SafeTracks(_kind, _detail);
            var _csv = new StringBuilder("frame,time," + string.Join(",", _tracks.Select(_t => _t.Name)) + "\n");

            int _previousCapture = Time.captureFramerate;
            float _previousScale = Time.timeScale;
            Time.timeScale = 1f;
            Time.captureFramerate = fps;
            int _frames = Mathf.CeilToInt(_seconds * fps) + 1;
            journal.Record("record.start", $"{_kind} {_detail} -> {Path.GetFileName(_dir)} ({_frames} frames @ {fps} fps)");

            float _t0 = Time.time;
            try
            {
                for (int _f = 0; _f < _frames; _f++)
                {
                    yield return new WaitForEndOfFrame();
                    SaveFrame(Path.Combine(_dir, $"f{_f:000}.png"));
                    _csv.Append(_f).Append(',').Append((Time.time - _t0).ToString("0.000", CultureInfo.InvariantCulture));
                    foreach (AutoplayTrack _track in _tracks)
                    {
                        float _v;
                        try { _v = _track.Read(); } catch { _v = float.NaN; }
                        _csv.Append(',').Append(float.IsNaN(_v) ? "" : _v.ToString("0.#####", CultureInfo.InvariantCulture));
                    }
                    _csv.Append('\n');
                }
            }
            finally
            {
                Time.captureFramerate = _previousCapture;
                Time.timeScale = _previousScale;
                IsRecording = false;
            }

            File.WriteAllText(Path.Combine(_dir, "tracks.csv"), _csv.ToString());
            File.WriteAllText(Path.Combine(_dir, "manifest.json"),
                "{\n  \"trigger\": " + Json(_kind) + ",\n  \"detail\": " + Json(_detail) + ",\n  \"fps\": " + fps +
                ",\n  \"frames\": " + _frames + ",\n  \"seconds\": " + _seconds.ToString(CultureInfo.InvariantCulture) +
                ",\n  \"tracks\": [" + string.Join(", ", _tracks.Select(_t => Json(_t.Name))) + "]\n}\n");
            journal.Record("record", $"{Path.GetFileName(_dir)} {_frames} frames");
        }

        private List<AutoplayTrack> SafeTracks(string _kind, string _detail)
        {
            try
            {
                return (tracksFor?.Invoke(_kind, _detail) ?? Enumerable.Empty<AutoplayTrack>()).ToList();
            }
            catch (Exception _exception)
            {
                journal.Record("record.tracks.error", _exception.Message);
                return new List<AutoplayTrack>();
            }
        }

        private void SaveFrame(string _path)
        {
            Texture2D _full = ScreenCapture.CaptureScreenshotAsTexture();
            try
            {
                int _height = Mathf.Max(1, Mathf.RoundToInt(_full.height * (width / (float)_full.width)));
                RenderTexture _rt = RenderTexture.GetTemporary(width, _height);
                Graphics.Blit(_full, _rt);
                RenderTexture _active = RenderTexture.active;
                RenderTexture.active = _rt;
                var _small = new Texture2D(width, _height, TextureFormat.RGB24, false);
                _small.ReadPixels(new Rect(0, 0, width, _height), 0, 0);
                _small.Apply();
                RenderTexture.active = _active;
                RenderTexture.ReleaseTemporary(_rt);
                File.WriteAllBytes(_path, _small.EncodeToPNG());
                Destroy(_small);
            }
            finally
            {
                Destroy(_full);
            }
        }

        private static string Json(string _s) => "\"" + (_s ?? "").Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
    }
}
#endif
