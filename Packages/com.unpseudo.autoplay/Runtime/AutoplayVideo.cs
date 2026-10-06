#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Experimental.Rendering;

namespace Unpseudo.Autoplay
{
    /// <summary>
    /// Films the run (<c>-autoplay-video</c>, <c>-autoplay-video-fps N</c>, default 10): the screen is grabbed at that
    /// real-time rate into <c>video/f000000.jpg…</c> (JPEG encoding off the main thread) with each frame's real time in
    /// <c>video/frames.txt</c> (ffmpeg concat list). Tools~/make_videos.py (run by the launchers after every run) turns
    /// the folder into <c>video.mp4</c> next to it, real-time paced, then removes the frames. A process killed on purpose
    /// (crash-at) leaves no list: the converter falls back to the fixed rate. Needs a rendering window (not batchmode:
    /// the end of frame never comes there).
    /// </summary>
    public sealed class AutoplayVideo : MonoBehaviour
    {
        private const int JpegQuality = 80;

        private readonly List<float> frameTimes = new();
        private readonly List<Task> pending = new();
        private AutoplayJournal journal;
        private string folder;
        private float period;
        private bool running;

        public void Begin(AutoplayJournal _journal, int _fps)
        {
            journal = _journal;
            int _rate = Mathf.Clamp(_fps, 1, 30);
            period = 1f / _rate;
            folder = Path.Combine(_journal.OutputDirectory, "video");
            Directory.CreateDirectory(folder);
            File.WriteAllText(Path.Combine(folder, "fps.txt"), _rate.ToString(CultureInfo.InvariantCulture));
            running = true;
            StartCoroutine(Film());
            journal.Record("video.begin", $"fps={_rate} folder=video");
        }

        private IEnumerator Film()
        {
            var _endOfFrame = new WaitForEndOfFrame();
            float _next = Time.realtimeSinceStartup;
            while (running)
            {
                yield return _endOfFrame;
                float _now = Time.realtimeSinceStartup;
                if (_now < _next)
                {
                    continue;
                }
                _next = Mathf.Max(_next + period, _now); // late: no burst to catch up, the list keeps real times
                Grab(_now);
            }
        }

        private void Grab(float _now)
        {
            Texture2D _shot = ScreenCapture.CaptureScreenshotAsTexture();
            if (_shot == null)
            {
                return;
            }
            byte[] _pixels = _shot.GetRawTextureData<byte>().ToArray();
            GraphicsFormat _format = _shot.graphicsFormat;
            uint _width = (uint)_shot.width;
            uint _height = (uint)_shot.height;
            Destroy(_shot);

            string _path = Path.Combine(folder, $"f{frameTimes.Count:000000}.jpg");
            frameTimes.Add(_now);
            pending.RemoveAll(_t => _t.IsCompleted);
            pending.Add(Task.Run(() =>
            {
                byte[] _jpg = ImageConversion.EncodeArrayToJPG(_pixels, _format, _width, _height, 0, JpegQuality);
                File.WriteAllBytes(_path, _jpg);
            }));
        }

        /// <summary>Stops filming and writes the frame list (waits a few seconds for the last encodes).</summary>
        public void End()
        {
            if (!running)
            {
                return;
            }
            running = false;
            Task.WaitAll(pending.ToArray(), 10000);

            var _list = new StringBuilder();
            for (int i = 0; i < frameTimes.Count; i++)
            {
                float _duration = i + 1 < frameTimes.Count ? frameTimes[i + 1] - frameTimes[i] : period;
                _list.Append("file 'f").Append(i.ToString("000000", CultureInfo.InvariantCulture)).Append(".jpg'\n");
                _list.Append("duration ").Append(_duration.ToString("0.000", CultureInfo.InvariantCulture)).Append('\n');
            }
            if (frameTimes.Count > 0)
            {
                // ffmpeg's concat demuxer ignores the last duration unless the last file is listed once more.
                _list.Append("file 'f").Append((frameTimes.Count - 1).ToString("000000", CultureInfo.InvariantCulture)).Append(".jpg'\n");
            }
            File.WriteAllText(Path.Combine(folder, "frames.txt"), _list.ToString());
            journal?.Record("video.end", string.Format(CultureInfo.InvariantCulture, "frames={0} seconds={1:0.0}",
                frameTimes.Count, frameTimes.Count > 1 ? frameTimes[frameTimes.Count - 1] - frameTimes[0] : 0f));
        }

        private void OnDestroy()
        {
            End();
        }
    }
}
#endif
