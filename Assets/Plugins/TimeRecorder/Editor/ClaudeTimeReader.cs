using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace Meaf75.Unity {

    /// <summary>
    /// Reads the Claude Code working-time ledger, span journal and corrections produced by the
    /// .claude/hooks PowerShell scripts and tools/timerecorder/tr.py.
    /// Read-only on the Unity side: the hooks own the JSON file (machine-local, gitignored), the
    /// calendar window only visualizes it. Mirrors how TimeRecorder keeps dev time in PlayerPrefs.
    /// </summary>
    public static class ClaudeTimeReader {

        private static ClaudeTimeData cached;
        private static List<ClaudeInterval> intervals = new List<ClaudeInterval>();
        private static List<ClaudeAdjustment> adjustments = new List<ClaudeAdjustment>();
        private static List<ClaudeInvoice> invoices = new List<ClaudeInvoice>();
        private static List<ClaudeInterval> devSpans = new List<ClaudeInterval>();
        private static Dictionary<string, double> factors = new Dictionary<string, double>();
        private static readonly Dictionary<DateTime, ClaudeDaySummary> daySummaries = new Dictionary<DateTime, ClaudeDaySummary>();

        /// <summary>
        /// Absolute path to the directory the hook scripts read/write. Always the MAIN checkout's,
        /// even from an editor opened on a git worktree: the hooks book every session there
        /// (Resolve-MainCheckout in .claude/hooks/_timerecorder_common.ps1), so the pause flag
        /// and the calendar must point at the same place.
        /// </summary>
        private static string LedgerDir {
            get {
                // Application.dataPath is the project's Assets folder
                string projectDir = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
                return Path.Combine(ResolveMainCheckout(projectDir), ".claude", "timerecorder");
            }
        }

        /// <summary>
        /// Map a git worktree to its main checkout: a worktree's .git is a file
        /// "gitdir: &lt;main&gt;/.git/worktrees/&lt;name&gt;" whose commondir points back to &lt;main&gt;/.git.
        /// Any failure falls back to the given directory.
        /// </summary>
        private static string ResolveMainCheckout(string projectDir) {
            try {
                string dotGit = Path.Combine(projectDir, ".git");
                if (!File.Exists(dotGit)) {
                    return projectDir;  // a real .git folder (main checkout) or no git at all
                }

                string line = File.ReadAllText(dotGit).Trim();
                if (!line.StartsWith("gitdir:", StringComparison.Ordinal)) {
                    return projectDir;
                }

                string gitDir = line.Substring("gitdir:".Length).Trim();
                if (!Path.IsPathRooted(gitDir)) {
                    gitDir = Path.Combine(projectDir, gitDir);
                }

                string commonFile = Path.Combine(gitDir, "commondir");
                if (!File.Exists(commonFile)) {
                    return projectDir;
                }

                string common = File.ReadAllText(commonFile).Trim();
                if (!Path.IsPathRooted(common)) {
                    common = Path.Combine(gitDir, common);
                }

                string main = Path.GetDirectoryName(Path.GetFullPath(common).TrimEnd('\\', '/'));
                return !string.IsNullOrEmpty(main) && Directory.Exists(main) ? main : projectDir;
            } catch (Exception) {
                return projectDir;
            }
        }

        /// <summary> Absolute path to the ledger maintained by the hook scripts </summary>
        public static string DataPath {
            get { return Path.Combine(LedgerDir, "claude_time.json"); }
        }

        /// <summary> Journal of booked spans (one JSON object per line), appended by the hooks </summary>
        public static string IntervalsPath {
            get { return Path.Combine(LedgerDir, "claude_intervals.jsonl"); }
        }

        /// <summary> Corrections over the journal, appended by tools/timerecorder/tr.py </summary>
        public static string AdjustmentsPath {
            get { return Path.Combine(LedgerDir, "claude_adjustments.jsonl"); }
        }

        /// <summary> Dev time spans (one line per 5-min period booked by TimeRecorder), append-only </summary>
        public static string DevIntervalsPath {
            get { return Path.Combine(LedgerDir, "dev_intervals.jsonl"); }
        }

        /// <summary> Billing marks ("Mark as invoiced" in the calendar), append-only </summary>
        public static string InvoicesPath {
            get { return Path.Combine(LedgerDir, "claude_invoices.jsonl"); }
        }

        /// <summary>
        /// Absolute path to the pause flag. When this file exists the hooks accrue no AI
        /// time. Unity only toggles the flag; the hooks own all ledger writes.
        /// </summary>
        public static string PausePath {
            get { return Path.Combine(LedgerDir, "ai_paused.flag"); }
        }

        /// <summary> Is AI time tracking currently paused? (presence of the flag file) </summary>
        public static bool IsPaused {
            get { return File.Exists(PausePath); }
        }

        /// <summary> Create or remove the pause flag the hooks check on every turn </summary>
        public static void SetPaused(bool paused) {
            try {
                Directory.CreateDirectory(LedgerDir);
                if (paused) {
                    if (!File.Exists(PausePath)) {
                        File.WriteAllText(PausePath, "AI time tracking paused from the Unity Time Calendar.");
                    }
                } else if (File.Exists(PausePath)) {
                    File.Delete(PausePath);
                }
            } catch (Exception e) {
                Debug.LogWarning($"[TimeRecorder] Could not toggle Claude pause flag: {e.Message}");
            }
        }

        /// <summary> Cached ledger, loaded lazily from disk </summary>
        public static ClaudeTimeData Data {
            get {
                if (cached == null) {
                    LoadFromDisk();
                }
                return cached;
            }
        }

        /// <summary> All-time AI seconds: every day of the ledger and of the journal, corrections applied </summary>
        public static long TotalSeconds {
            get {
                EnsureLoaded();
                var days = new HashSet<DateTime>();
                foreach (var d in Data.days)
                    days.Add(new DateTime(d.year, d.month, d.day));
                foreach (var s in intervals)
                    days.Add(ClaudeTimeline.EndDay(s));
                double total = 0;
                foreach (var d in days)
                    total += GetDaySummary(d).TotalSeconds;
                return (long) Math.Round(total);
            }
        }

        /// <summary> Force a re-read of the ledger, journal and corrections from disk (used on window repaint) </summary>
        public static ClaudeTimeData Reload() {
            LoadFromDisk();
            return cached;
        }

        private static void EnsureLoaded() {
            if (cached == null)
                LoadFromDisk();
        }

        private static void LoadFromDisk() {
            try {
                if (!File.Exists(DataPath)) {
                    cached = new ClaudeTimeData();
                } else {
                    string json = File.ReadAllText(DataPath);

                    cached = string.IsNullOrEmpty(json)
                        ? new ClaudeTimeData()
                        : JsonUtility.FromJson<ClaudeTimeData>(json) ?? new ClaudeTimeData();
                }
            } catch (Exception e) {
                Debug.LogWarning($"[TimeRecorder] Could not read Claude time ledger: {e.Message}");
                cached = new ClaudeTimeData();
            }

            if (cached.days == null) {
                cached.days = new List<ClaudeDayEntry>();
            }

            intervals = ReadJsonLines<ClaudeInterval>(IntervalsPath).FindAll(s => s.sec > 0 && s.endMs > s.startMs);
            adjustments = ReadJsonLines<ClaudeAdjustment>(AdjustmentsPath);
            devSpans = ReadJsonLines<ClaudeInterval>(DevIntervalsPath).FindAll(s => s.sec > 0 && s.endMs > s.startMs);
            var allInvoices = ReadJsonLines<ClaudeInvoice>(InvoicesPath);
            var revokedInvoices = new HashSet<string>();
            foreach (var i in allInvoices)
                if (!string.IsNullOrEmpty(i.revoke))
                    revokedInvoices.Add(i.revoke);
            invoices = allInvoices.FindAll(i => string.IsNullOrEmpty(i.revoke) && !revokedInvoices.Contains(i.id));
            invoices.Sort((a, b) => a.atMs.CompareTo(b.atMs));
            factors = ClaudeTimeline.EffectiveFactors(adjustments);
            daySummaries.Clear();
        }

        /// <summary>
        /// Read a JSON-lines file the hooks may be appending to right now: shared read, and a torn or
        /// unreadable line is skipped instead of failing the whole file.
        /// </summary>
        private static List<T> ReadJsonLines<T>(string path) where T : class {
            var list = new List<T>();
            if (!File.Exists(path))
                return list;
            try {
                using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                using (var reader = new StreamReader(fs, System.Text.Encoding.UTF8)) {
                    string line;
                    while ((line = reader.ReadLine()) != null) {
                        if (string.IsNullOrWhiteSpace(line))
                            continue;
                        try {
                            var item = JsonUtility.FromJson<T>(line);
                            if (item != null)
                                list.Add(item);
                        } catch (Exception) {
                            // torn last line (hook mid-append) or hand-edited garbage: skip it
                        }
                    }
                }
            } catch (Exception e) {
                Debug.LogWarning($"[TimeRecorder] Could not read {Path.GetFileName(path)}: {e.Message}");
            }
            return list;
        }

        private static int LedgerSecondsFor(DateTime day) {
            int sum = 0;
            foreach (var d in Data.days)
                if (d.year == day.Year && d.month == day.Month && d.day == day.Day)
                    sum += d.seconds;
            return sum;
        }

        /// <summary> Everything known about one day: totals, blocks of spans, time per branch and per tag </summary>
        public static ClaudeDaySummary GetDaySummary(DateTime day) {
            EnsureLoaded();
            day = day.Date;
            if (!daySummaries.TryGetValue(day, out var summary)) {
                summary = ClaudeTimeline.Summarize(day, LedgerSecondsFor(day), intervals, factors);
                daySummaries[day] = summary;
            }
            return summary;
        }

        /// <summary> AI spans of the journal </summary>
        public static List<ClaudeInterval> Intervals {
            get { EnsureLoaded(); return intervals; }
        }

        /// <summary> Dev spans (editor open), written by TimeRecorder </summary>
        public static List<ClaudeInterval> DevSpans {
            get { EnsureLoaded(); return devSpans; }
        }

        /// <summary> span id -> correction factor </summary>
        public static Dictionary<string, double> Factors {
            get { EnsureLoaded(); return factors; }
        }

        /// <summary>
        /// Journal a dev period ending at <paramref name="endLocal"/>, with the branch of this checkout. Called by
        /// TimeRecorder on every save; several editors (main + worktrees) may append, BillableTime merges them.
        /// </summary>
        public static void AppendDevSpan(DateTime endLocal, int seconds) {
            if (seconds <= 0)
                return;
            long endMs = ClaudeTimeline.ToMs(endLocal);
            string projectDir = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            var span = new ClaudeInterval {
                v = 1,
                id = Guid.NewGuid().ToString("N"),
                src = "dev",
                kind = "editor",
                trigger = "",
                startMs = endMs - seconds * 1000L,
                endMs = endMs,
                sec = seconds,
                branch = ReadBranch(projectDir),
                worktree = ResolveMainCheckout(projectDir) == projectDir ? "main" : Path.GetFileName(projectDir),
            };
            try {
                Directory.CreateDirectory(LedgerDir);
                File.AppendAllText(DevIntervalsPath, JsonUtility.ToJson(span) + "\n", new System.Text.UTF8Encoding(false));
            } catch (Exception e) {
                Debug.LogWarning($"[TimeRecorder] Could not journal the dev span: {e.Message}");
            }
            if (cached != null)
                devSpans.Add(span);
        }

        /// <summary> Current branch of a checkout, read from its HEAD (worktrees included) </summary>
        private static string ReadBranch(string projectDir) {
            try {
                string gitDir = Path.Combine(projectDir, ".git");
                if (File.Exists(gitDir)) {
                    string line = File.ReadAllText(gitDir).Trim();
                    if (line.StartsWith("gitdir:", StringComparison.Ordinal)) {
                        gitDir = line.Substring("gitdir:".Length).Trim();
                        if (!Path.IsPathRooted(gitDir))
                            gitDir = Path.Combine(projectDir, gitDir);
                    }
                }
                string head = File.ReadAllText(Path.Combine(gitDir, "HEAD")).Trim();
                const string prefix = "ref: refs/heads/";
                if (head.StartsWith(prefix, StringComparison.Ordinal))
                    return head.Substring(prefix.Length);
                return head.Length >= 10 ? "detached@" + head.Substring(0, 10) : "";
            } catch (Exception) {
                return "";
            }
        }

        /// <summary> Active billing marks, oldest first </summary>
        public static IReadOnlyList<ClaudeInvoice> Invoices {
            get { EnsureLoaded(); return invoices; }
        }

        /// <summary> Latest active billing mark, or null if nothing was ever invoiced </summary>
        public static ClaudeInvoice LastInvoice {
            get { EnsureLoaded(); return invoices.Count > 0 ? invoices[invoices.Count - 1] : null; }
        }

        /// <summary> AI seconds since the given instant up to now (corrections applied, parallel sessions once) </summary>
        public static double AiSecondsSince(long fromMs) {
            EnsureLoaded();
            long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            if (fromMs >= now)
                return 0;
            return ClaudeTimeline.AiSecondsBetween(fromMs, now, intervals, factors, d => GetDaySummary(d).legacySeconds);
        }

        /// <summary> Append a billing mark. The hooks never touch this file; the calendar is its only writer. </summary>
        public static void AddInvoice(ClaudeInvoice invoice) {
            invoice.v = 1;
            if (string.IsNullOrEmpty(invoice.id))
                invoice.id = Guid.NewGuid().ToString("N");
            AppendJsonLine(InvoicesPath, JsonUtility.ToJson(invoice));
        }

        /// <summary> Cancel a billing mark (appends a revocation, the original line stays for the record) </summary>
        public static void RevokeInvoice(string id) {
            var revoke = new ClaudeInvoice { v = 1, id = Guid.NewGuid().ToString("N"), atMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), revoke = id };
            AppendJsonLine(InvoicesPath, JsonUtility.ToJson(revoke));
        }

        private static void AppendJsonLine(string path, string json) {
            try {
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                File.AppendAllText(path, json + "\n", new System.Text.UTF8Encoding(false));
            } catch (Exception e) {
                Debug.LogError($"[TimeRecorder] Could not write {Path.GetFileName(path)}: {e.Message}");
            }
            Reload();
        }

        /// <summary> Correction factor applied to a span (1 = none) </summary>
        public static double FactorOf(ClaudeInterval span) {
            EnsureLoaded();
            return ClaudeTimeline.FactorOf(span, factors);
        }

        /// <summary> Active (non-revoked) corrections touching at least one of the given spans </summary>
        public static List<ClaudeAdjustment> AdjustmentsFor(IEnumerable<ClaudeInterval> spans) {
            EnsureLoaded();
            var ids = new HashSet<string>();
            foreach (var s in spans)
                ids.Add(s.id);
            var revoked = new HashSet<string>();
            foreach (var a in adjustments)
                if (!string.IsNullOrEmpty(a.revoke))
                    revoked.Add(a.revoke);
            return adjustments.FindAll(a => string.IsNullOrEmpty(a.revoke) && !revoked.Contains(a.id)
                                            && a.ids != null && Array.Exists(a.ids, ids.Contains));
        }

        /// <summary> Claude worked seconds for the given calendar day, corrections applied (0 if none) </summary>
        public static int GetSecondsForDate(int year, int month, int day) {
            return (int) Math.Round(GetDaySummary(new DateTime(year, month, day)).TotalSeconds);
        }
    }
}
