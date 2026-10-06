using System;
using System.Collections.Generic;

namespace Meaf75.Unity {

    /// <summary> Billable time of one day: AI and dev merged, an instant counted once </summary>
    public class BillableDay {
        public DateTime day;
        /// <summary> Union of the day's timestamped spans (AI with corrections, dev) </summary>
        public double spansUnion;
        /// <summary> AI seconds booked without timestamps (before the AI journal existed) </summary>
        public double aiLegacy;
        /// <summary> Dev seconds without timestamps (before dev spans existed, or a hand-edited day) </summary>
        public double devLegacy;
        public double ai;
        public long dev;
        /// <summary> Untimed AI and dev overlap in an unknown way: their larger part is counted (an estimate) </summary>
        public bool estimated;
        public double Total => spansUnion + Math.Max(aiLegacy, devLegacy);
    }

    /// <summary>
    /// What gets billed: AI time and dev time together, without counting an instant twice. Timestamped spans
    /// (AI journal, dev spans written by TimeRecorder every 5 min) merge exactly. Time without timestamps (days
    /// booked before the journals, a dev day edited by hand) can't be merged: the larger of its AI and dev parts
    /// counts, and the day is flagged as estimated when both exist.
    /// </summary>
    public static class BillableTime {

        private static TimeRecorderInfo devInfo;
        private static readonly Dictionary<DateTime, BillableDay> cache = new Dictionary<DateTime, BillableDay>();
        private static Dictionary<DateTime, long> devJournaledByDay;
        private static List<ClaudeInterval> trustedDevSpans;

        /// <summary> Call after the dev registry and ClaudeTimeReader were (re)loaded </summary>
        public static void Refresh(TimeRecorderInfo info) {
            devInfo = info;
            cache.Clear();
            devJournaledByDay = null;
            trustedDevSpans = null;
        }

        public static long DevDaySeconds(DateTime day) {
            var year = devInfo?.years?.Find(y => y.year == day.Year);
            var month = year?.months?.Find(m => m.month == day.Month);
            var date = month?.dates?.Find(d => d.date == day.Day);
            return date?.timeInSeconds ?? 0;
        }

        private static long DevJournaled(DateTime day) {
            if (devJournaledByDay == null) {
                devJournaledByDay = new Dictionary<DateTime, long>();
                foreach (var s in ClaudeTimeReader.DevSpans) {
                    var d = ClaudeTimeline.EndDay(s);
                    devJournaledByDay.TryGetValue(d, out var v);
                    devJournaledByDay[d] = v + s.sec;
                }
            }
            return devJournaledByDay.TryGetValue(day.Date, out var sec) ? sec : 0;
        }

        /// <summary> A day's dev spans are trusted unless the day total was edited below them by hand </summary>
        private static bool DevSpansTrusted(DateTime day) {
            return DevJournaled(day) <= DevDaySeconds(day);
        }

        private static List<ClaudeInterval> TrustedDevSpans() {
            if (trustedDevSpans == null)
                trustedDevSpans = ClaudeTimeReader.DevSpans.FindAll(s => DevSpansTrusted(ClaudeTimeline.EndDay(s)));
            return trustedDevSpans;
        }

        private static double DevLegacy(DateTime day) {
            long total = DevDaySeconds(day);
            return DevSpansTrusted(day) ? total - DevJournaled(day) : total;
        }

        public static BillableDay Day(DateTime day) {
            day = day.Date;
            if (cache.TryGetValue(day, out var cached))
                return cached;

            var aiSummary = ClaudeTimeReader.GetDaySummary(day);
            long lo = ClaudeTimeline.ToMs(day), hi = ClaudeTimeline.ToMs(day.AddDays(1));
            var spans = new List<ClaudeInterval>();
            foreach (var s in ClaudeTimeReader.Intervals)
                if (s.endMs > lo && s.startMs < hi) spans.Add(s);
            foreach (var s in TrustedDevSpans())
                if (s.endMs > lo && s.startMs < hi) spans.Add(s);

            var result = new BillableDay {
                day = day,
                spansUnion = ClaudeTimeline.WeightedUnionSeconds(spans, ClaudeTimeReader.Factors, lo, hi),
                aiLegacy = aiSummary.legacySeconds,
                devLegacy = DevLegacy(day),
                ai = aiSummary.TotalSeconds,
                dev = DevDaySeconds(day),
            };
            result.estimated = result.aiLegacy >= 60 && result.devLegacy >= 60;
            cache[day] = result;
            return result;
        }

        /// <summary>
        /// Billable, AI and dev seconds from an instant to now. Spans are clipped exactly at the instant; untimed
        /// time counts for the days strictly after the instant's day (it can't be placed within that day).
        /// </summary>
        public static void Since(long fromMs, out double billable, out double ai, out double dev) {
            long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            billable = ai = dev = 0;
            if (fromMs >= now)
                return;

            var devSpans = TrustedDevSpans();
            var all = new List<ClaudeInterval>(ClaudeTimeReader.Intervals);
            all.AddRange(devSpans);
            billable = ClaudeTimeline.WeightedUnionSeconds(all, ClaudeTimeReader.Factors, fromMs, now);
            dev = ClaudeTimeline.WeightedUnionSeconds(devSpans, null, fromMs, now);
            ai = ClaudeTimeReader.AiSecondsSince(fromMs);

            for (var d = ClaudeTimeline.Local(fromMs).Date.AddDays(1); d <= DateTime.Today; d = d.AddDays(1)) {
                var day = Day(d);
                billable += Math.Max(day.aiLegacy, day.devLegacy);
                dev += day.devLegacy;
            }
        }

        /// <summary> Billable seconds of all time (every day with dev or AI time) </summary>
        public static double AllTime() {
            var days = new HashSet<DateTime>();
            foreach (var d in ClaudeTimeReader.Data.days)
                days.Add(new DateTime(d.year, d.month, d.day));
            foreach (var s in ClaudeTimeReader.Intervals)
                days.Add(ClaudeTimeline.EndDay(s));
            if (devInfo?.years != null)
                foreach (var y in devInfo.years)
                    foreach (var m in y.months ?? new List<MonthInfo>())
                        foreach (var d in m.dates ?? new List<DateInfo>())
                            if (d.timeInSeconds > 0)
                                days.Add(new DateTime(y.year, m.month, d.date));
            double total = 0;
            foreach (var d in days)
                total += Day(d).Total;
            return total;
        }
    }
}
