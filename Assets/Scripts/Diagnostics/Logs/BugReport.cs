using System;

namespace Diagnostics.Logs
{
    /// <summary>
    /// A playtest bug report (F8 window) or a crash alert, as stored locally (<c>logs/reports/&lt;id&gt;.json</c>, plus
    /// <c>&lt;id&gt;.jpg</c> for the capture) until it is online: Firestore <c>reports/&lt;id&gt;</c> + Storage
    /// <c>reports/&lt;id&gt;.jpg</c>. A Cloud Function posts each new report to the Discord channel « rapports-de-bug ».
    /// </summary>
    [Serializable]
    public sealed class BugReport
    {
        public const string KindReport = "report";
        public const string KindCrash = "crash";
        public const int MaxTextChars = 2000;
        public const long MinIntervalMs = 20_000;
        public const int MaxCaptureWidth = 1280;

        public string id;
        public string kind = KindReport;
        public string sid;
        public string gameId;
        public string player;
        public string seat;
        public string role;
        public string phase;
        public int day;
        public string text;
        public long t;
        public string version;
        public string flavor;
        public bool hasCapture;
        public bool captureSent;
        /// <summary>A capture upload was attempted: a later refusal (create-only rule) means the first one got through.</summary>
        public bool captureTried;
        public int attempts;
        public long queuedAt;

        /// <summary>Sortable, unique enough for one PC: <c>20261020-214012-a3f9c2</c> (UTC).</summary>
        public static string NewId(DateTime _utcNow) =>
            _utcNow.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N").Substring(0, 6);

        public static string CleanText(string _text)
        {
            string _clean = (_text ?? string.Empty).Trim();
            return _clean.Length > MaxTextChars ? _clean.Substring(0, MaxTextChars) : _clean;
        }

        /// <summary>Milliseconds the player still has to wait before the next report (0 = send now).</summary>
        public static long WaitBeforeNext(long _lastSentMs, long _nowMs) =>
            _lastSentMs <= 0 ? 0 : Math.Max(0, MinIntervalMs - (_nowMs - _lastSentMs));

        /// <summary>Capture size kept for upload: width at most <see cref="MaxCaptureWidth"/>, ratio kept, never upscaled.</summary>
        public static (int width, int height) CaptureSize(int _width, int _height)
        {
            if (_width <= MaxCaptureWidth || _width <= 0) return (_width, _height);
            return (MaxCaptureWidth, Math.Max(1, (int)Math.Round(_height * (double)MaxCaptureWidth / _width)));
        }

        /// <summary>The Firestore document (create-only, fields allowed by the rules).</summary>
        public FirestoreFields ToFields(string _uid, long _expireAtMs) =>
            new FirestoreFields()
                .String("uid", _uid)
                .String("id", id)
                .String("kind", kind)
                .String("sid", sid)
                .String("gameId", gameId)
                .String("player", player)
                .String("seat", seat)
                .String("role", role)
                .String("phase", phase)
                .Int("day", day)
                .String("text", text)
                .Timestamp("t", t)
                .String("version", version)
                .String("flavor", flavor)
                .String("capture", hasCapture ? "reports/" + id + ".jpg" : string.Empty)
                .Timestamp("expireAt", _expireAtMs);
    }
}
