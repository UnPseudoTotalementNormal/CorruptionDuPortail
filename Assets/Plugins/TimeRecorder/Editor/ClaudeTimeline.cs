using System;
using System.Collections.Generic;
using System.Linq;

namespace Meaf75.Unity {

    /// <summary> One booked span of AI working time (a line of claude_intervals.jsonl, written by the hooks) </summary>
    [Serializable]
    public class ClaudeInterval {
        public int v;
        public string id;
        public string src;      // claude | codex
        public string kind;     // turn (the AI working) | gap (reading / thinking / typing between turns)
        public string trigger;  // prompt (a human opened the turn) | background (task notification, wake-up)
        public long startMs;
        public long endMs;
        public int sec;
        public bool clamped;
        public string session;
        public string branch;
        public string worktree;
        public string cwd;
        public string prompt;
        public string model;
        public string[] skills;
        public string[] tags;
        public string[] tools;
        public string[] commands;
        public string[] files;
    }

    /// <summary>
    /// A correction (a line of claude_adjustments.jsonl, written by tools/timerecorder/tr.py): a factor
    /// applied to a fixed list of span ids, or the revocation of an earlier correction.
    /// </summary>
    [Serializable]
    public class ClaudeAdjustment {
        public int v;
        public string id;
        public long atMs;
        public double factor;
        public string reason;
        public string filter;
        public string revoke;
        public string[] ids;
    }

    /// <summary>
    /// A billing mark (a line of claude_invoices.jsonl, written by the calendar's "Mark as invoiced"): the instant
    /// the dev told the client their hours, with the amounts billed for the period it closes, frozen at that time.
    /// A line with <see cref="revoke"/> set cancels an earlier mark.
    /// </summary>
    [Serializable]
    public class ClaudeInvoice {
        public int v;
        public string id;
        public long atMs;
        public double aiSeconds;
        public long devSeconds;
        /// <summary> AI and dev merged, an instant counted once: what was invoiced </summary>
        public double billableSeconds;
        /// <summary> Dev seconds of the mark's day at the mark: the next period counts that day's dev time above it </summary>
        public long devDaySnapshot;
        public string note;
        public string revoke;
    }

    /// <summary> A run of consecutive spans of one session on one branch, shown as one row </summary>
    public class ClaudeBlock {
        public DateTime start;
        public DateTime end;
        public string branch;
        public string src;
        public double rawSeconds;
        public double adjustedSeconds;
        public readonly List<ClaudeInterval> spans = new List<ClaudeInterval>();
    }

    public class ClaudeDaySummary {
        public DateTime day;
        /// <summary> Seconds booked that day before the journal existed (ledger minus journaled spans) </summary>
        public double legacySeconds;
        public double rawUnionSeconds;
        public double adjustedUnionSeconds;
        public double TotalSeconds => legacySeconds + adjustedUnionSeconds;
        public readonly List<ClaudeBlock> blocks = new List<ClaudeBlock>();
        public readonly List<KeyValuePair<string, double>> byBranch = new List<KeyValuePair<string, double>>();
        public readonly List<KeyValuePair<string, double>> byTag = new List<KeyValuePair<string, double>>();
    }

    /// <summary>
    /// Pure time math over the journal; mirrors tools/timerecorder/tr.py, keep both in sync.
    /// Day total = (ledger day - raw seconds journaled that day) + union of the day's spans, each instant
    /// weighted by the highest factor among the spans covering it: parallel sessions count once.
    /// </summary>
    public static class ClaudeTimeline {

        public static DateTime Local(long ms) {
            return DateTimeOffset.FromUnixTimeMilliseconds(ms).LocalDateTime;
        }

        public static long ToMs(DateTime local) {
            return new DateTimeOffset(DateTime.SpecifyKind(local, DateTimeKind.Local)).ToUnixTimeMilliseconds();
        }

        /// <summary> The hooks book a span into the local day it ends on </summary>
        public static DateTime EndDay(ClaudeInterval s) {
            return Local(s.endMs - 1).Date;
        }

        /// <summary> span id -> factor; corrections replay in order (last wins), revoked ones skipped </summary>
        public static Dictionary<string, double> EffectiveFactors(IEnumerable<ClaudeAdjustment> adjustments) {
            var list = adjustments.ToList();
            var revoked = new HashSet<string>(list.Where(a => !string.IsNullOrEmpty(a.revoke)).Select(a => a.revoke));
            var factors = new Dictionary<string, double>();
            foreach (var a in list) {
                if (!string.IsNullOrEmpty(a.revoke) || revoked.Contains(a.id) || a.ids == null)
                    continue;
                foreach (var id in a.ids)
                    factors[id] = a.factor;
            }
            return factors;
        }

        public static double FactorOf(ClaudeInterval s, Dictionary<string, double> factors) {
            return factors != null && s.id != null && factors.TryGetValue(s.id, out var f) ? f : 1.0;
        }

        /// <summary> Union of the spans clipped to [lo, hi), each instant weighted by the max factor covering it </summary>
        public static double WeightedUnionSeconds(IEnumerable<ClaudeInterval> spans, Dictionary<string, double> factors,
                                                  long lo = long.MinValue, long hi = long.MaxValue) {
            var events = new List<(long t, int kind, double f)>();
            foreach (var s in spans) {
                long a = Math.Max(s.startMs, lo), b = Math.Min(s.endMs, hi);
                if (b <= a)
                    continue;
                double f = FactorOf(s, factors);
                events.Add((a, 1, f));
                events.Add((b, -1, f));
            }
            // Ends before starts at the same instant: touching spans don't overlap
            events.Sort((x, y) => x.t != y.t ? x.t.CompareTo(y.t) : x.kind.CompareTo(y.kind));

            var active = new SortedDictionary<double, int>();
            double totalMs = 0;
            long prev = 0;
            bool hasPrev = false;
            foreach (var e in events) {
                if (hasPrev && active.Count > 0)
                    totalMs += (e.t - prev) * active.Keys.Last();
                if (e.kind == 1) {
                    active.TryGetValue(e.f, out var c);
                    active[e.f] = c + 1;
                } else if (active.TryGetValue(e.f, out var c)) {
                    if (c <= 1) active.Remove(e.f); else active[e.f] = c - 1;
                }
                prev = e.t;
                hasPrev = true;
            }
            return totalMs / 1000.0;
        }

        /// <summary>
        /// AI seconds booked in [fromMs, toMs): the spans clipped to that window (union, corrections applied), plus the
        /// pre-journal day totals of the days strictly after the day of fromMs (those days have no timestamps; the day
        /// of fromMs itself only counts its spans).
        /// </summary>
        public static double AiSecondsBetween(long fromMs, long toMs, IReadOnlyList<ClaudeInterval> spans,
                                              Dictionary<string, double> factors, Func<DateTime, double> legacyOfDay) {
            double total = WeightedUnionSeconds(spans, factors, fromMs, toMs);
            var firstFullDay = Local(fromMs).Date.AddDays(1);
            var lastDay = Local(toMs - 1).Date;
            for (var d = firstFullDay; d <= lastDay; d = d.AddDays(1))
                total += legacyOfDay(d);
            return total;
        }

        public static ClaudeDaySummary Summarize(DateTime day, int ledgerSeconds, IReadOnlyList<ClaudeInterval> spans,
                                                 Dictionary<string, double> factors) {
            day = day.Date;
            long lo = ToMs(day), hi = ToMs(day.AddDays(1));
            var summary = new ClaudeDaySummary { day = day };

            int journaledEndingToday = 0;
            var inDay = new List<ClaudeInterval>();
            foreach (var s in spans) {
                if (EndDay(s) == day)
                    journaledEndingToday += s.sec;
                if (s.endMs > lo && s.startMs < hi)
                    inDay.Add(s);
            }

            summary.legacySeconds = Math.Max(0, ledgerSeconds - journaledEndingToday);
            summary.rawUnionSeconds = WeightedUnionSeconds(inDay, null, lo, hi);
            summary.adjustedUnionSeconds = WeightedUnionSeconds(inDay, factors, lo, hi);

            foreach (var g in inDay.GroupBy(s => string.IsNullOrEmpty(s.branch) ? "(no branch)" : s.branch))
                summary.byBranch.Add(new KeyValuePair<string, double>(g.Key, WeightedUnionSeconds(g, factors, lo, hi)));
            summary.byBranch.Sort((a, b) => b.Value.CompareTo(a.Value));

            var tagged = new Dictionary<string, List<ClaudeInterval>>();
            foreach (var s in inDay) {
                var tags = s.tags != null && s.tags.Length > 0 ? s.tags : new[] { s.kind == "gap" ? "between turns" : "(none)" };
                foreach (var t in tags) {
                    if (!tagged.TryGetValue(t, out var l))
                        tagged[t] = l = new List<ClaudeInterval>();
                    l.Add(s);
                }
            }
            foreach (var kv in tagged)
                summary.byTag.Add(new KeyValuePair<string, double>(kv.Key, WeightedUnionSeconds(kv.Value, factors, lo, hi)));
            summary.byTag.Sort((a, b) => b.Value.CompareTo(a.Value));

            // Blocks: consecutive spans of one session on one branch (a few seconds of slack between hooks)
            ClaudeBlock current = null;
            ClaudeInterval last = null;
            foreach (var s in inDay.OrderBy(s => s.session).ThenBy(s => s.startMs)) {
                bool joins = current != null && last.session == s.session && last.branch == s.branch && s.startMs - last.endMs <= 5000;
                if (!joins) {
                    current = new ClaudeBlock { branch = s.branch, src = s.src, start = Local(Math.Max(s.startMs, lo)) };
                    summary.blocks.Add(current);
                }
                current.spans.Add(s);
                current.end = Local(Math.Min(s.endMs, hi));
                last = s;
            }
            foreach (var b in summary.blocks) {
                b.rawSeconds = WeightedUnionSeconds(b.spans, null, lo, hi);
                b.adjustedSeconds = WeightedUnionSeconds(b.spans, factors, lo, hi);
            }
            summary.blocks.Sort((a, b) => a.start.CompareTo(b.start));
            return summary;
        }
    }
}
