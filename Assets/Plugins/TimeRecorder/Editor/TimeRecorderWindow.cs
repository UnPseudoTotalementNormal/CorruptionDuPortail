using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Meaf75.Unity {

    /// <summary>
    /// Time Calendar: dev time (editor open, PlayerPrefs) and AI working time (hook ledger + span journal) per day.
    /// The UXML is a skeleton; the summary cards, the month grid and the day detail are built here, and rebuilt on
    /// every repaint while keeping the selected day, the detail scroll position and the expanded sessions.
    /// </summary>
    public class TimeRecorderWindow : EditorWindow, IHasCustomMenu {

        public static TimeRecorderWindow Instance;

        private static readonly CultureInfo Culture = CultureInfo.InvariantCulture;
        private const int MINUTES_PER_DAY = 1_440;

        /// <summary> Branch colors, picked by a stable hash of the branch name so a branch keeps its color </summary>
        private static readonly Color[] BranchPalette = {
            new Color32(0x4e, 0xc9, 0xb0, 0xff), new Color32(0xc5, 0x86, 0xc0, 0xff), new Color32(0x56, 0x9c, 0xd6, 0xff),
            new Color32(0xdc, 0xdc, 0xaa, 0xff), new Color32(0xce, 0x91, 0x78, 0xff), new Color32(0xb5, 0xce, 0xa8, 0xff),
            new Color32(0xd1, 0x69, 0x69, 0xff), new Color32(0x9c, 0xdc, 0xfe, 0xff),
        };

        private static TimeRecorderInfo info;

        /// <summary> First day of the displayed month </summary>
        private static DateTime displayedMonth = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);

        /// <summary> Day shown in the detail panel </summary>
        private static DateTime selectedDay = DateTime.Today;

        // UI state kept across rebuilds
        private Vector2 detailScroll;
        private readonly HashSet<string> expandedSessions = new HashSet<string>();
        private bool editingDevTime;
        private bool resetDetailScroll;
        private DateTime dataStamp;

        // Built elements, by day / session key
        private readonly Dictionary<DateTime, VisualElement> cellsByDay = new Dictionary<DateTime, VisualElement>();
        private readonly Dictionary<string, Foldout> sessionFoldouts = new Dictionary<string, Foldout>();

        [MenuItem("Tools/Time recorder/Time Calendar")]
        static void Init() {
            var window = GetWindow<TimeRecorderWindow>();
            window.minSize = new Vector2(1000, 620);
            displayedMonth = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
            selectedDay = DateTime.Today;
            window.UpdateTitle();
        }

        private void OnEnable() {
            Instance = this;
            DrawWindow();
        }

        private void OnFocus() {
            // The hooks keep writing while the window is open: pick up new AI time when coming back to it.
            // Only when the data changed: a rebuild during the focusing click would swallow that click.
            if (rootVisualElement.childCount > 0 && DataStamp() != dataStamp)
                RepaintWindow();
        }

        /// <summary> Latest write time of the AI data files, to skip rebuilds when nothing changed </summary>
        private static DateTime DataStamp() {
            var latest = DateTime.MinValue;
            foreach (var path in new[] { ClaudeTimeReader.DataPath, ClaudeTimeReader.IntervalsPath, ClaudeTimeReader.AdjustmentsPath }) {
                try {
                    if (System.IO.File.Exists(path)) {
                        var t = System.IO.File.GetLastWriteTimeUtc(path);
                        if (t > latest) latest = t;
                    }
                } catch (Exception) { }
            }
            return latest;
        }

        void IHasCustomMenu.AddItemsToMenu(GenericMenu menu) {
            menu.AddItem(new GUIContent("Refresh"), false, RepaintWindow);
            menu.AddItem(new GUIContent("Open AI data folder"), false,
                () => EditorUtility.RevealInFinder(ClaudeTimeReader.DataPath));
        }

        /// <summary> Rebuild everything from disk. Called by the recorder on each save, and on focus. </summary>
        public void RepaintWindow() {
            if (rootVisualElement.Q<ScrollView>("detail-scroll") is ScrollView scroll)
                detailScroll = scroll.scrollOffset;
            rootVisualElement.Clear();
            DrawWindow();
        }

        /// <summary> Called by TimeRecorderTools when the dev recording is paused / resumed </summary>
        public void UpdatePausedState(bool paused) {
            TimeRecorder.isPaused = paused;
            UpdateTitle();
            UpdateStateButtons();
        }

        private void UpdateTitle() {
            titleContent = new GUIContent($"Time Calendar{(TimeRecorder.isPaused ? " (dev paused)" : "")}");
        }

        // ================================================================ build

        private void DrawWindow() {
            var root = rootVisualElement;
            info = TimeRecorder.LoadTimeRecorderInfoFromRegistry();
            dataStamp = DataStamp();
            ClaudeTimeReader.Reload();
            BillableTime.Refresh(info);
            cellsByDay.Clear();

            var tree = Resources.Load<VisualTreeAsset>(TimeRecorderExtras.CALENDAR_TEMPLATE_PATH);
            var style = Resources.Load<StyleSheet>(TimeRecorderExtras.CALENDAR_TEMPLATE_STYLE_PATH);
            if (tree == null || style == null) {
                root.Add(new Label("Time Calendar: template not found in Resources."));
                return;
            }
            root.styleSheets.Add(style);
            tree.CloneTree(root);
            UpdateTitle();

            root.Q<Button>("btn-prev-month").clicked += () => ShowMonth(displayedMonth.AddMonths(-1));
            root.Q<Button>("btn-next-month").clicked += () => ShowMonth(displayedMonth.AddMonths(1));
            root.Q<Button>("btn-today").clicked += () => SelectDay(DateTime.Today);
            root.Q<Button>("btn-refresh").clicked += RepaintWindow;
            root.Q<Button>("btn-dev-state").clicked += () => TimeRecorderTools.ChangeTimeRecorderPauseState(!TimeRecorder.isPaused);
            root.Q<Button>("btn-ai-state").clicked += () => {
                ClaudeTimeReader.SetPaused(!ClaudeTimeReader.IsPaused);
                UpdateStateButtons();
            };
            root.Q<Label>("label-month").text = displayedMonth.ToString("MMMM yyyy", Culture);
            var detailScrollView = root.Q<ScrollView>("detail-scroll");
            detailScrollView.mode = ScrollViewMode.Vertical;
            detailScrollView.horizontalScrollerVisibility = ScrollerVisibility.Hidden;
            UpdateStateButtons();

            BuildSummary(root.Q("summary"));
            BuildGrid(root.Q("weekday-header"), root.Q("grid"));
            BuildLegend(root.Q("legend"));
            BuildDetail();
        }

        private void UpdateStateButtons() {
            SetStateButton(rootVisualElement.Q<Button>("btn-dev-state"), "Dev", TimeRecorder.isPaused, "tr-dot--dev",
                "Dev time: counts while the editor is open. Click to pause / resume.");
            SetStateButton(rootVisualElement.Q<Button>("btn-ai-state"), "AI", ClaudeTimeReader.IsPaused, "tr-dot--ai",
                "AI time: booked by the Claude / Codex hooks. Click to pause / resume (no AI time is booked while paused).");
        }

        private static void SetStateButton(Button button, string name, bool paused, string dotClass, string tooltip) {
            if (button == null)
                return;
            button.text = "";
            button.Clear();
            var dot = new VisualElement();
            dot.AddToClassList("tr-dot");
            dot.AddToClassList(paused ? "tr-dot--paused" : dotClass);
            button.Add(dot);
            button.Add(new Label(paused ? $"{name} paused" : $"{name} recording"));
            button.tooltip = tooltip;
        }

        // ---------------------------------------------------------------- summary cards

        private void BuildSummary(VisualElement summary) {
            int days = DateTime.DaysInMonth(displayedMonth.Year, displayedMonth.Month);
            var month = Sum(displayedMonth, days, out int workedDays);
            var monthCard = BillCard("BILLABLE THIS MONTH", month,
                workedDays > 0 ? $"{workedDays} day{(workedDays > 1 ? "s" : "")} · avg {Format((long) (month.bill / workedDays))} / day" : "nothing yet");
            monthCard.tooltip = $"All time billable {Format((long) BillableTime.AllTime())}\nDev {Format(info?.totalRecordedTime ?? 0)} · AI {Format(ClaudeTimeReader.TotalSeconds)}";
            summary.Add(monthCard);

            var week = StartOfWeek(DateTime.Today);
            summary.Add(BillCard("THIS WEEK", Sum(week, 7, out _), $"since {week.ToString("ddd d MMM", Culture)}"));
            summary.Add(BillCard("TODAY", Sum(DateTime.Today, 1, out _), DateTime.Today.ToString("dddd d MMM", Culture)));
            summary.Add(BillingCard());
        }

        private struct Totals {
            public double bill, ai;
            public long dev;
            public bool estimated;
        }

        private static Totals Sum(DateTime from, int days, out int workedDays) {
            var t = new Totals();
            workedDays = 0;
            for (int i = 0; i < days; i++) {
                var b = BillableTime.Day(from.AddDays(i));
                t.bill += b.Total;
                t.ai += b.ai;
                t.dev += b.dev;
                t.estimated |= b.estimated;
                if (b.Total >= 60) workedDays++;
            }
            return t;
        }

        /// <summary> A summary card: billable first, AI and dev below </summary>
        private static VisualElement BillCard(string title, Totals t, string sub) {
            var card = new VisualElement();
            card.AddToClassList("tr-card");
            card.AddToClassList("tr-card--bill");
            card.Add(Text(title, "tr-card-title"));
            card.Add(Text((t.estimated ? "\u2248 " : "") + FormatCard((long) t.bill), "tr-card-value", "tr-card-value--bill"));
            card.Add(BreakdownLine(t.ai, t.dev));
            card.Add(Text(sub, "tr-card-sub"));
            if (t.estimated)
                card.tooltip = "\u2248 includes days booked before timestamps existed: their AI and dev overlap is estimated (the larger counts).";
            return card;
        }

        /// <summary> "AI 2 h 05 · dev 3 h 10" with each part in its color </summary>
        private static VisualElement BreakdownLine(double ai, long dev) {
            var line = new VisualElement();
            line.AddToClassList("tr-breakdown");
            line.Add(Text("AI " + FormatCard((long) ai), "tr-breakdown-part", "tr-card-value--ai"));
            line.Add(Text("dev " + FormatCard(dev), "tr-breakdown-part", "tr-card-value--dev"));
            return line;
        }

        // ---------------------------------------------------------------- billing

        /// <summary> Billable time since the last "Mark invoiced" (or since the beginning), with the mark button </summary>
        private VisualElement BillingCard() {
            var last = ClaudeTimeReader.LastInvoice;
            ComputeSinceLastInvoice(out double bill, out double ai, out double dev);

            var card = new VisualElement();
            card.AddToClassList("tr-card");
            card.AddToClassList("tr-card--billing");
            var header = new VisualElement();
            header.AddToClassList("tr-tile-head");
            header.Add(Text("SINCE LAST INVOICE", "tr-card-title"));
            var history = new Button(ShowInvoiceHistory) { text = "History", tooltip = "Past invoice marks, undo the last one" };
            history.AddToClassList("tr-link-btn");
            history.SetEnabled(ClaudeTimeReader.Invoices.Count > 0);
            header.Add(history);
            card.Add(header);

            var value = new VisualElement();
            value.AddToClassList("tr-card-pair");
            value.Add(Text(FormatCard((long) bill), "tr-card-value", "tr-card-value--bill"));
            value.Add(Text(Hours(bill), "tr-card-hours"));
            card.Add(value);
            card.Add(BreakdownLine(ai, (long) dev));
            card.Add(Text(last != null
                ? $"since {ClaudeTimeline.Local(last.atMs).ToString("ddd d MMM, HH:mm", Culture)}"
                : "never marked yet: counts everything", "tr-card-sub"));

            var actions = new VisualElement();
            actions.AddToClassList("tr-card-actions");
            var mark = new Button(MarkAsInvoiced) { text = "Mark invoiced", tooltip = "You just gave your hours to the client: restart this counter from now" };
            mark.AddToClassList("tr-btn");
            mark.AddToClassList("tr-mark-btn");
            actions.Add(mark);
            card.Add(actions);
            return card;
        }

        private static void ComputeSinceLastInvoice(out double bill, out double ai, out double dev) {
            var last = ClaudeTimeReader.LastInvoice;
            if (last == null) {
                bill = BillableTime.AllTime();
                ai = ClaudeTimeReader.TotalSeconds;
                dev = info?.totalRecordedTime ?? 0;
                return;
            }
            BillableTime.Since(last.atMs, out bill, out ai, out dev);
        }

        private void MarkAsInvoiced() {
            ComputeSinceLastInvoice(out double bill, out double ai, out double dev);
            if (!EditorUtility.DisplayDialog("Mark as invoiced",
                    $"Mark this period as given to the client?\n\nBillable: {Format((long) bill)}  ({Hours(bill)})\n    AI {Format((long) ai)}, dev {Format((long) dev)}, overlap counted once\n\nThe counter restarts from now. You can undo it from History.",
                    "Mark as invoiced", "Cancel"))
                return;

            ClaudeTimeReader.AddInvoice(new ClaudeInvoice {
                atMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                billableSeconds = bill,
                aiSeconds = ai,
                devSeconds = (long) dev,
                devDaySnapshot = GetDevSeconds(DateTime.Today),
            });
            Debug.Log($"[TimeRecorder] Marked as invoiced: {Hours(bill)} (AI {Hours(ai)}, dev {Hours(dev)})");
            RepaintWindow();
        }

        private void ShowInvoiceHistory() {
            var menu = new GenericMenu();
            var marks = ClaudeTimeReader.Invoices;
            for (int i = marks.Count - 1; i >= 0; i--) {
                var m = marks[i];
                string when = ClaudeTimeline.Local(m.atMs).ToString("yyyy-MM-dd HH:mm", Culture);
                // A '/' would open a submenu: the label avoids it
                menu.AddDisabledItem(new GUIContent($"{when}    {Format((long) m.billableSeconds)} ({Hours(m.billableSeconds)})    AI {Format((long) m.aiSeconds)} · dev {Format(m.devSeconds)}"));
            }
            if (marks.Count > 0) {
                var lastMark = marks[marks.Count - 1];
                menu.AddSeparator("");
                menu.AddItem(new GUIContent($"Undo last mark ({ClaudeTimeline.Local(lastMark.atMs).ToString("d MMM HH:mm", Culture)})"), false, () => {
                    if (!EditorUtility.DisplayDialog("Undo last invoice mark",
                            "Cancel the last mark? The counter goes back to counting from the previous mark.", "Undo mark", "Keep it"))
                        return;
                    ClaudeTimeReader.RevokeInvoice(lastMark.id);
                    RepaintWindow();
                });
            }
            menu.ShowAsContext();
        }

        // ---------------------------------------------------------------- month grid

        private void BuildGrid(VisualElement header, VisualElement grid) {
            foreach (var name in new[] { "MON", "TUE", "WED", "THU", "FRI", "SAT", "SUN" })
                header.Add(Text(name, "tr-weekday"));
            header.Add(Text("WEEK", "tr-weekday", "tr-weekday--week"));

            var first = StartOfWeek(displayedMonth);
            var lastOfMonth = displayedMonth.AddMonths(1).AddDays(-1);
            int weeks = (int) Math.Ceiling(((lastOfMonth - first).TotalDays + 1) / 7.0);

            // One scale for every bar of the month view (billable >= AI and dev), so they compare
            long max = 1;
            for (int i = 0; i < weeks * 7; i++)
                max = Math.Max(max, (long) BillableTime.Day(first.AddDays(i)).Total);

            for (int w = 0; w < weeks; w++) {
                var row = new VisualElement();
                row.AddToClassList("tr-grid-row");
                for (int d = 0; d < 7; d++) {
                    var day = first.AddDays(w * 7 + d);
                    row.Add(BuildCell(day, GetDevSeconds(day), GetAiSeconds(day), max));
                }

                var weekTotals = Sum(first.AddDays(w * 7), 7, out _);
                var weekCell = new VisualElement();
                weekCell.AddToClassList("tr-week");
                if (weekTotals.bill >= 1) {
                    weekCell.Add(Text((weekTotals.estimated ? "\u2248" : "") + FormatShort((long) weekTotals.bill), "tr-week-value", "tr-week-value--bill"));
                    weekCell.Add(Text(weekTotals.ai >= 1 ? "AI " + FormatShort((long) weekTotals.ai) : "", "tr-week-sub", "tr-card-value--ai"));
                    weekCell.Add(Text(weekTotals.dev >= 1 ? "dev " + FormatShort(weekTotals.dev) : "", "tr-week-sub", "tr-card-value--dev"));
                }
                weekCell.tooltip = $"Week of {first.AddDays(w * 7).ToString("d MMM", Culture)}\nBillable {Format((long) weekTotals.bill)} ({Hours(weekTotals.bill)})\nAI {Format((long) weekTotals.ai)} · dev {Format(weekTotals.dev)}";
                row.Add(weekCell);

                grid.Add(row);
            }
        }

        private VisualElement BuildCell(DateTime day, long dev, long ai, long max) {
            var cell = new VisualElement();
            cell.AddToClassList("tr-cell");
            if (day.Month != displayedMonth.Month) cell.AddToClassList("tr-cell--out");
            if (day == DateTime.Today) cell.AddToClassList("tr-cell--today");
            if (day == selectedDay) cell.AddToClassList("tr-cell--selected");
            if (day.DayOfWeek == DayOfWeek.Saturday || day.DayOfWeek == DayOfWeek.Sunday) cell.AddToClassList("tr-cell--weekend");

            var summary = ClaudeTimeReader.GetDaySummary(day);
            double corrected = summary.rawUnionSeconds - summary.adjustedUnionSeconds;

            var bill = BillableTime.Day(day);

            var head = new VisualElement();
            head.AddToClassList("tr-cell-head");
            head.Add(Text(day.Day.ToString(Culture), "tr-cell-day"));
            var headRight = new VisualElement();
            headRight.AddToClassList("tr-cell-head-right");
            if (corrected >= 1) {
                var mark = new VisualElement();
                mark.AddToClassList("tr-cell-mark");
                headRight.Add(mark);
            }
            if (bill.Total >= 1)
                headRight.Add(Text((bill.estimated ? "\u2248" : "") + FormatShort((long) bill.Total), "tr-cell-bill"));
            head.Add(headRight);
            cell.Add(head);

            // Heat tint on a layer behind the content (an inline background would override hover / selection)
            if (day.Month == displayedMonth.Month && bill.Total >= 1) {
                var heat = new VisualElement { pickingMode = PickingMode.Ignore };
                heat.AddToClassList("tr-cell-heat");
                heat.style.opacity = 0.15f + 0.85f * Mathf.Clamp01((float) bill.Total / max);
                cell.Insert(0, heat);
            }

            var bars = new VisualElement();
            bars.AddToClassList("tr-cell-bars");
            if (dev > 0) bars.Add(Metric(dev, max, "dev"));
            if (ai > 0) bars.Add(Metric(ai, max, "ai"));
            cell.Add(bars);

            string tip = day.ToString("dddd d MMMM", Culture);
            tip += $"\nBillable {Format((long) bill.Total)} ({Hours(bill.Total)}){(bill.estimated ? ", estimated overlap" : "")}";
            tip += dev > 0 ? $"\nDev {Format(dev)}" : "\nNo dev time";
            tip += ai > 0 ? $"\nAI {Format(ai)}" : "\nNo AI time";
            if (corrected >= 1) tip += $"\nAI corrections: -{Format((long) corrected)}";
            cell.tooltip = tip;

            cell.RegisterCallback<ClickEvent>(_ => SelectDay(day));
            cellsByDay[day] = cell;
            return cell;
        }

        private static VisualElement Metric(long seconds, long max, string kind) {
            var row = new VisualElement();
            row.AddToClassList("tr-metric");
            var track = new VisualElement();
            track.AddToClassList("tr-metric-track");
            var fill = new VisualElement();
            fill.AddToClassList("tr-metric-fill");
            fill.AddToClassList("tr-metric-fill--" + kind);
            fill.style.width = Length.Percent(Mathf.Clamp(100f * seconds / max, 3f, 100f));
            track.Add(fill);
            row.Add(Text(FormatShort(seconds), "tr-metric-value", "tr-metric-value--" + kind));
            row.Add(track);
            return row;
        }

        private static void BuildLegend(VisualElement legend) {
            legend.Add(LegendItem("var-bill", "Billable: AI + dev, an instant counted once"));
            legend.Add(LegendItem("var-dev", "Dev: editor open"));
            legend.Add(LegendItem("var-ai", "AI: Claude / Codex"));
            legend.Add(LegendItem("mark", "AI corrected"));
            legend.Add(LegendItem("none", "\u2248 estimated (before timestamps)"));
        }

        private static VisualElement LegendItem(string kind, string label) {
            var item = new VisualElement();
            item.AddToClassList("tr-legend-item");
            var swatch = new VisualElement();
            switch (kind) {
                case "var-dev": swatch.AddToClassList("tr-legend-swatch"); swatch.AddToClassList("tr-metric-fill--dev"); break;
                case "var-ai": swatch.AddToClassList("tr-legend-swatch"); swatch.AddToClassList("tr-metric-fill--ai"); break;
                case "var-bill": swatch.AddToClassList("tr-legend-swatch"); swatch.AddToClassList("tr-metric-fill--bill"); break;
                case "none": swatch.style.display = DisplayStyle.None; break;
                default: swatch.AddToClassList("tr-cell-mark"); swatch.style.marginRight = 5; break;
            }
            item.Add(swatch);
            item.Add(Text(label, "tr-legend-label"));
            return item;
        }

        // ---------------------------------------------------------------- navigation

        private void ShowMonth(DateTime month) {
            displayedMonth = new DateTime(month.Year, month.Month, 1);
            RepaintWindow();
        }

        private void SelectDay(DateTime day) {
            day = day.Date;
            var month = new DateTime(day.Year, day.Month, 1);
            bool monthChanged = month != displayedMonth;
            if (day != selectedDay) {
                detailScroll = Vector2.zero;
                resetDetailScroll = true;
                expandedSessions.Clear();
                editingDevTime = false;
            }

            if (cellsByDay.TryGetValue(selectedDay, out var previous))
                previous.RemoveFromClassList("tr-cell--selected");
            selectedDay = day;

            if (monthChanged) {
                displayedMonth = month;
                RepaintWindow();
                return;
            }
            if (cellsByDay.TryGetValue(day, out var cell))
                cell.AddToClassList("tr-cell--selected");
            BuildDetail();
        }

        // ================================================================ day detail

        private void BuildDetail() {
            var scroll = rootVisualElement.Q<ScrollView>("detail-scroll");
            if (scroll == null)
                return;
            // Rebuilding the same day (edit dev time, ...): keep the scroll position
            if (!resetDetailScroll && scroll.contentContainer.childCount > 0)
                detailScroll = scroll.scrollOffset;
            resetDetailScroll = false;
            scroll.Clear();
            sessionFoldouts.Clear();

            var day = selectedDay;
            var summary = ClaudeTimeReader.GetDaySummary(day);
            long dev = GetDevSeconds(day);

            // Header with day navigation
            var head = new VisualElement();
            head.AddToClassList("tr-detail-head");
            var prev = new Button(() => SelectDay(day.AddDays(-1))) { text = "←", tooltip = "Previous day" };
            prev.AddToClassList("tr-btn");
            prev.AddToClassList("tr-icon-btn");
            var next = new Button(() => SelectDay(day.AddDays(1))) { text = "→", tooltip = "Next day" };
            next.AddToClassList("tr-btn");
            next.AddToClassList("tr-icon-btn");
            var titles = new VisualElement();
            titles.AddToClassList("tr-detail-titles");
            string relative = day == DateTime.Today ? "TODAY" : day == DateTime.Today.AddDays(-1) ? "YESTERDAY" : day.ToString("dddd", Culture).ToUpperInvariant();
            titles.Add(Text(relative, "tr-detail-weekday"));
            titles.Add(Text(day.ToString("d MMMM yyyy", Culture), "tr-detail-date"));
            head.Add(titles);
            head.Add(prev);
            head.Add(next);
            scroll.Add(head);

            scroll.Add(BuildBillableTile(BillableTime.Day(day)));

            // Dev / AI tiles
            var tiles = new VisualElement();
            tiles.AddToClassList("tr-tiles");
            tiles.Add(BuildDevTile(day, dev));
            tiles.Add(BuildAiTile(summary));
            scroll.Add(tiles);

            if (summary.blocks.Count == 0) {
                scroll.Add(Text(summary.legacySeconds >= 1
                    ? "This day was booked before AI spans were journaled: only its total is known."
                    : "No AI time this day.", "tr-empty"));
                RestoreScroll(scroll);
                return;
            }

            var branchColors = summary.byBranch.ToDictionary(kv => kv.Key, kv => BranchColor(kv.Key));

            scroll.Add(Text("TIMELINE", "tr-section"));
            scroll.Add(BuildTimeline(summary, branchColors));

            scroll.Add(Text("PER BRANCH", "tr-section"));
            double branchMax = summary.byBranch.Count > 0 ? summary.byBranch.Max(kv => kv.Value) : 1;
            foreach (var kv in summary.byBranch)
                scroll.Add(BarRow(kv.Key, kv.Value, branchMax, branchColors[kv.Key]));

            scroll.Add(Text("PER ACTIVITY", "tr-section"));
            double tagMax = summary.byTag.Count > 0 ? summary.byTag.Max(kv => kv.Value) : 1;
            foreach (var kv in summary.byTag)
                scroll.Add(BarRow(kv.Key, kv.Value, tagMax, null));
            scroll.Add(Text("A span can carry several activities: rows overlap.", "tr-note"));

            var corrections = ClaudeTimeReader.AdjustmentsFor(summary.blocks.SelectMany(b => b.spans));
            if (corrections.Count > 0) {
                scroll.Add(Text("CORRECTIONS", "tr-section"));
                foreach (var a in corrections) {
                    var box = new VisualElement();
                    box.AddToClassList("tr-correction");
                    box.Add(Text($"x{a.factor.ToString("0.##", Culture)}  {a.reason}", "tr-warn"));
                    box.Add(Text($"{a.ids?.Length ?? 0} spans · {a.filter} · {ClaudeTimeline.Local(a.atMs).ToString("d MMM HH:mm", Culture)}", "tr-note"));
                    scroll.Add(box);
                }
            }

            scroll.Add(Text($"SESSIONS ({summary.blocks.Count})", "tr-section"));
            foreach (var block in summary.blocks)
                scroll.Add(BuildSession(block, branchColors));

            RestoreScroll(scroll);
        }

        private void RestoreScroll(ScrollView scroll) {
            var target = detailScroll;
            if (target == Vector2.zero)
                return;
            // The content has no size until laid out: restore once it has one
            scroll.schedule.Execute(() => scroll.scrollOffset = target).StartingIn(0);
        }

        private static VisualElement BuildBillableTile(BillableDay bill) {
            var tile = new VisualElement();
            tile.AddToClassList("tr-tile");
            tile.AddToClassList("tr-tile--bill");
            var head = new VisualElement();
            head.AddToClassList("tr-tile-head");
            head.Add(Text("BILLABLE", "tr-tile-title"));
            head.Add(Text(Hours(bill.Total), "tr-card-hours"));
            tile.Add(head);
            tile.Add(Text(bill.Total >= 1 ? (bill.estimated ? "\u2248 " : "") + FormatCard((long) bill.Total) : "-", "tr-tile-value", "tr-card-value--bill"));
            double overlap = bill.ai + bill.dev - bill.Total;
            string sub = overlap >= 60 ? $"AI and dev merged: {Format((long) overlap)} of overlap counted once" : "AI and dev merged, an instant counted once";
            if (bill.estimated)
                sub += ". Part of the day has no timestamps: its overlap is estimated.";
            tile.Add(Text(sub, "tr-tile-sub"));
            return tile;
        }

        private VisualElement BuildDevTile(DateTime day, long dev) {
            var tile = new VisualElement();
            tile.AddToClassList("tr-tile");
            tile.AddToClassList("tr-tile--dev");

            var head = new VisualElement();
            head.AddToClassList("tr-tile-head");
            head.Add(Text("DEV", "tr-tile-title"));
            if (!editingDevTime) {
                var edit = new Button(() => { editingDevTime = true; BuildDetail(); }) { text = "Edit", tooltip = "Correct the dev time of this day" };
                edit.AddToClassList("tr-link-btn");
                head.Add(edit);
            }
            tile.Add(head);
            tile.Add(Text(dev > 0 ? FormatCard(dev) : "-", "tr-tile-value", "tr-card-value--dev"));

            if (editingDevTime) {
                var row = new VisualElement();
                row.AddToClassList("tr-edit-row");
                var field = new IntegerField { value = (int) (dev / 60), tooltip = "Minutes" };
                var save = new Button(() => SaveDevMinutes(day, field.value)) { text = "Save" };
                save.AddToClassList("tr-link-btn");
                var cancel = new Button(() => { editingDevTime = false; BuildDetail(); }) { text = "Cancel" };
                cancel.AddToClassList("tr-link-btn");
                row.Add(field);
                row.Add(Text("min", "tr-tile-sub"));
                row.Add(save);
                row.Add(cancel);
                tile.Add(row);
            } else {
                tile.Add(Text("time with the Unity editor open", "tr-tile-sub"));
            }
            return tile;
        }

        private static VisualElement BuildAiTile(ClaudeDaySummary summary) {
            var tile = new VisualElement();
            tile.AddToClassList("tr-tile");
            tile.AddToClassList("tr-tile--ai");
            var head = new VisualElement();
            head.AddToClassList("tr-tile-head");
            head.Add(Text("AI", "tr-tile-title"));
            tile.Add(head);
            tile.Add(Text(summary.TotalSeconds >= 1 ? FormatCard((long) summary.TotalSeconds) : "-", "tr-tile-value", "tr-card-value--ai"));

            var notes = new List<string>();
            if (summary.blocks.Count > 0)
                notes.Add($"{summary.blocks.Count} session{(summary.blocks.Count > 1 ? "s" : "")}");
            if (summary.legacySeconds >= 1)
                notes.Add($"{Format((long) summary.legacySeconds)} without detail");
            double corrected = summary.rawUnionSeconds - summary.adjustedUnionSeconds;
            tile.Add(Text(notes.Count > 0 ? string.Join(" · ", notes) : "nothing booked", "tr-tile-sub"));
            if (corrected >= 1)
                tile.Add(Text($"corrected -{Format((long) corrected)}", "tr-tile-sub", "tr-warn"));
            return tile;
        }

        private void SaveDevMinutes(DateTime day, int minutes) {
            minutes = Mathf.Clamp(minutes, 0, MINUTES_PER_DAY);
            if (info == null)
                info = new TimeRecorderInfo();
            var dateInfo = info.VerifyByDatetime(day);
            int seconds = minutes * 60;
            info.totalRecordedTime = info.totalRecordedTime - dateInfo.dayInfo.timeInSeconds + seconds;
            dateInfo.dayInfo.timeInSeconds = seconds;
            editingDevTime = false;

            TimeRecorder.SaveTimeRecorded(info);    // repaints this window
            TimeRecorder.ReCalculateNextSave();
            Debug.Log($"[TimeRecorder] Dev time of {day:yyyy-MM-dd} set to {minutes} min");
        }

        // ---------------------------------------------------------------- timeline

        private VisualElement BuildTimeline(ClaudeDaySummary summary, Dictionary<string, Color> branchColors) {
            var container = new VisualElement();
            container.AddToClassList("tr-timeline-box");
            var spans = summary.blocks.SelectMany(b => b.spans).ToList();
            long dayStart = ClaudeTimeline.ToMs(summary.day), dayEnd = ClaudeTimeline.ToMs(summary.day.AddDays(1));
            long first = Math.Max(dayStart, spans.Min(s => s.startMs));
            long last = Math.Min(dayEnd, spans.Max(s => s.endMs));

            // Whole hours around the activity, at least 2 h wide
            var from = ClaudeTimeline.Local(first);
            from = new DateTime(from.Year, from.Month, from.Day, from.Hour, 0, 0);
            var to = ClaudeTimeline.Local(last);
            to = new DateTime(to.Year, to.Month, to.Day, to.Hour, 0, 0).AddHours(to.Minute > 0 || to.Second > 0 ? 1 : 0);
            if ((to - from).TotalHours < 2)
                to = from.AddHours(2);
            long lo = ClaudeTimeline.ToMs(from), hi = ClaudeTimeline.ToMs(to);
            double range = hi - lo;

            var ticks = new VisualElement();
            ticks.AddToClassList("tr-timeline-ticks");
            int hours = (int) Math.Round((to - from).TotalHours);
            int step = hours <= 6 ? 1 : hours <= 12 ? 2 : 3;
            for (int h = 0; h <= hours; h += step) {
                var tick = Text(from.AddHours(h).ToString("HH:mm", Culture), "tr-timeline-tick");
                tick.style.left = Length.Percent((float) (100.0 * h / hours));
                ticks.Add(tick);
            }
            container.Add(ticks);

            var strip = new VisualElement();
            strip.AddToClassList("tr-timeline");
            foreach (var block in summary.blocks) {
                foreach (var s in block.spans.OrderBy(x => x.kind == "gap" ? 0 : 1)) {
                    long a = Math.Max(s.startMs, lo), b = Math.Min(s.endMs, hi);
                    if (b <= a)
                        continue;
                    var el = new VisualElement();
                    el.AddToClassList("tr-timeline-span");
                    if (s.kind == "gap")
                        el.AddToClassList("tr-timeline-span--gap");
                    el.style.left = Length.Percent((float) (100.0 * (a - lo) / range));
                    el.style.width = Length.Percent((float) (100.0 * (b - a) / range));
                    el.style.backgroundColor = branchColors.TryGetValue(BranchName(s.branch), out var c) ? c : Color.gray;
                    el.tooltip = $"{ClaudeTimeline.Local(s.startMs).ToString("HH:mm", Culture)} - {ClaudeTimeline.Local(s.endMs).ToString("HH:mm", Culture)}  {Format(s.sec)}\n"
                                 + $"{BranchName(s.branch)} · {KindLabel(s)}"
                                 + (string.IsNullOrEmpty(s.prompt) ? "" : $"\n“{Shorten(s.prompt, 120)}”");
                    string key = SessionKey(block);
                    el.RegisterCallback<ClickEvent>(_ => RevealSession(key));
                    strip.Add(el);
                }
            }
            container.Add(strip);

            // Dev row: editor-open periods, so the overlap with AI is visible
            var devStrip = new VisualElement();
            devStrip.AddToClassList("tr-timeline-dev");
            foreach (var s in ClaudeTimeReader.DevSpans) {
                long a = Math.Max(s.startMs, lo), b = Math.Min(s.endMs, hi);
                if (b <= a)
                    continue;
                var el = new VisualElement();
                el.AddToClassList("tr-timeline-span");
                el.AddToClassList("tr-metric-fill--dev");
                el.style.left = Length.Percent((float) (100.0 * (a - lo) / range));
                el.style.width = Length.Percent((float) (100.0 * (b - a) / range));
                el.tooltip = $"Dev (editor open) {ClaudeTimeline.Local(s.startMs).ToString("HH:mm", Culture)} - {ClaudeTimeline.Local(s.endMs).ToString("HH:mm", Culture)}  {s.branch}";
                devStrip.Add(el);
            }
            container.Add(devStrip);
            container.Add(Text("Top: AI (darker = working, lighter = between turns; click to open the session). Bottom: dev, editor open.", "tr-note"));
            return container;
        }

        private void RevealSession(string key) {
            if (!sessionFoldouts.TryGetValue(key, out var foldout))
                return;
            foldout.value = true;
            var scroll = rootVisualElement.Q<ScrollView>("detail-scroll");
            scroll?.schedule.Execute(() => scroll.ScrollTo(foldout)).StartingIn(10);
        }

        // ---------------------------------------------------------------- bars

        private static VisualElement BarRow(string name, double seconds, double max, Color? swatchColor) {
            var row = new VisualElement();
            row.AddToClassList("tr-bar-row");
            var swatch = new VisualElement();
            swatch.AddToClassList("tr-bar-swatch");
            if (swatchColor.HasValue)
                swatch.style.backgroundColor = swatchColor.Value;
            else
                swatch.AddToClassList("tr-metric-fill--ai");
            row.Add(swatch);
            var label = Text(name, "tr-bar-name");
            label.tooltip = name;
            row.Add(label);
            var track = new VisualElement();
            track.AddToClassList("tr-bar-track");
            var fill = new VisualElement();
            fill.AddToClassList("tr-bar-fill");
            fill.style.width = Length.Percent(Mathf.Clamp((float) (100.0 * seconds / Math.Max(1, max)), 2f, 100f));
            if (swatchColor.HasValue)
                fill.style.backgroundColor = swatchColor.Value;
            track.Add(fill);
            row.Add(track);
            row.Add(Text(Format((long) seconds), "tr-bar-value"));
            return row;
        }

        // ---------------------------------------------------------------- sessions

        private VisualElement BuildSession(ClaudeBlock block, Dictionary<string, Color> branchColors) {
            string key = SessionKey(block);
            var color = branchColors.TryGetValue(BranchName(block.branch), out var c) ? c : Color.gray;
            string corrected = Math.Abs(block.rawSeconds - block.adjustedSeconds) >= 1 ? $"  (raw {Format((long) block.rawSeconds)})" : "";
            var foldout = new Foldout {
                text = $"{block.start.ToString("HH:mm", Culture)} - {block.end.ToString("HH:mm", Culture)}   ·   {Format((long) block.adjustedSeconds)}{corrected}",
                value = expandedSessions.Contains(key),
            };
            foldout.AddToClassList("tr-session");
            foldout.style.borderLeftColor = color;
            foldout.RegisterValueChangedCallback(evt => {
                if (evt.target != foldout) return;
                if (evt.newValue) expandedSessions.Add(key); else expandedSessions.Remove(key);
            });

            var tags = block.spans.Where(s => s.tags != null).SelectMany(s => s.tags).Distinct().OrderBy(t => t).ToList();
            var meta = $"{BranchName(block.branch)} · {block.src} · {block.spans.Count} span{(block.spans.Count > 1 ? "s" : "")}";
            foldout.Add(Text(tags.Count > 0 ? $"{meta} · {string.Join(", ", tags)}" : meta, "tr-session-tags"));

            foreach (var span in block.spans)
                foldout.Add(BuildSpan(span));

            sessionFoldouts[key] = foldout;
            return foldout;
        }

        private static VisualElement BuildSpan(ClaudeInterval span) {
            var box = new VisualElement();
            box.AddToClassList("tr-span");

            double factor = ClaudeTimeReader.FactorOf(span);
            var head = new VisualElement();
            head.AddToClassList("tr-span-head");
            string factorTxt = Math.Abs(factor - 1.0) > 1e-9 ? $"  x{factor.ToString("0.##", Culture)}" : "";
            var time = Text($"{ClaudeTimeline.Local(span.startMs).ToString("HH:mm", Culture)} - {ClaudeTimeline.Local(span.endMs).ToString("HH:mm", Culture)}   {Format(span.sec)}{factorTxt}", "tr-span-time");
            if (factorTxt.Length > 0)
                time.AddToClassList("tr-warn");
            head.Add(time);
            head.Add(Text(KindLabel(span) + (span.clamped ? " · capped" : ""), "tr-span-kind"));
            box.Add(head);

            if (!string.IsNullOrEmpty(span.prompt)) {
                var prompt = Text($"“{Shorten(span.prompt, 180)}”", "tr-span-prompt");
                if (span.trigger == "background")
                    prompt.AddToClassList("tr-span-meta");
                box.Add(prompt);
            }

            if (span.tags != null && span.tags.Length > 0) {
                var chips = new VisualElement();
                chips.AddToClassList("tr-chip-row");
                foreach (var t in span.tags)
                    chips.Add(Text(t, "tr-chip"));
                box.Add(chips);
            }
            if (span.skills != null && span.skills.Length > 0)
                box.Add(Text("skills: " + string.Join(", ", span.skills), "tr-span-meta"));
            if (span.files != null && span.files.Length > 0)
                box.Add(Text("files: " + string.Join(", ", span.files), "tr-span-meta"));

            var tip = new System.Text.StringBuilder();
            tip.AppendLine($"{span.src} · {span.worktree} · {span.model}");
            tip.AppendLine($"session {span.session}");
            if (span.tools != null && span.tools.Length > 0)
                tip.AppendLine("tools: " + string.Join(", ", span.tools));
            if (span.commands != null)
                foreach (var cmd in span.commands)
                    tip.AppendLine("$ " + cmd);
            box.tooltip = tip.ToString().TrimEnd();
            return box;
        }

        // ================================================================ data & formatting helpers

        private static long GetDevSeconds(DateTime day) {
            var year = info?.years?.Find(y => y.year == day.Year);
            var month = year?.months?.Find(m => m.month == day.Month);
            var date = month?.dates?.Find(d => d.date == day.Day);
            return date?.timeInSeconds ?? 0;
        }

        private static long GetAiSeconds(DateTime day) {
            return ClaudeTimeReader.GetSecondsForDate(day.Year, day.Month, day.Day);
        }

        private static DateTime StartOfWeek(DateTime day) {
            int offset = ((int) day.DayOfWeek + 6) % 7;    // Monday first
            return day.Date.AddDays(-offset);
        }

        private static string SessionKey(ClaudeBlock block) {
            return $"{block.start.Ticks}|{block.branch}|{block.spans.FirstOrDefault()?.session}";
        }

        private static string BranchName(string branch) {
            return string.IsNullOrEmpty(branch) ? "(no branch)" : branch;
        }

        private static string KindLabel(ClaudeInterval s) {
            if (s.kind == "gap") return "between turns";
            return s.trigger == "background" ? "AI turn (background)" : "AI turn";
        }

        private static Color BranchColor(string branch) {
            unchecked {
                uint hash = 2166136261;
                foreach (char ch in branch)
                    hash = (hash ^ ch) * 16777619;
                return BranchPalette[hash % (uint) BranchPalette.Length];
            }
        }

        private static string Shorten(string text, int max) {
            return text.Length <= max ? text : text.Substring(0, max) + "...";
        }

        /// <summary> "3 h 20 min", "45 min", "30 sec" </summary>
        private static string Format(long seconds) {
            if (seconds < 60)
                return seconds <= 0 ? "0 min" : $"{seconds} sec";
            var t = TimeSpan.FromSeconds(seconds);
            if (t.TotalMinutes < 60)
                return $"{(int) t.TotalMinutes} min";
            return t.Minutes > 0 ? $"{(int) t.TotalHours} h {t.Minutes:00} min" : $"{(int) t.TotalHours} h";
        }

        /// <summary> Card / tile values: "62 h 15", "45 min", "0 min" </summary>
        private static string FormatCard(long seconds) {
            if (seconds < 3600)
                return $"{Math.Max(0, seconds) / 60} min";
            var t = TimeSpan.FromSeconds(seconds);
            return t.Minutes > 0 ? $"{(int) t.TotalHours} h {t.Minutes:00}" : $"{(int) t.TotalHours} h";
        }

        /// <summary> Decimal hours for invoicing: "12.25 h" </summary>
        private static string Hours(double seconds) {
            return (Math.Max(0, seconds) / 3600.0).ToString("0.00", Culture) + " h";
        }

        /// <summary> Compact form for the grid: "3h20", "45m", "<1m" </summary>
        private static string FormatShort(long seconds) {
            if (seconds < 60)
                return "<1m";
            var t = TimeSpan.FromSeconds(seconds);
            if (t.TotalMinutes < 60)
                return $"{(int) t.TotalMinutes}m";
            return $"{(int) t.TotalHours}h{t.Minutes:00}";
        }

        private static Label Text(string text, params string[] classes) {
            var label = new Label(text);
            foreach (var c in classes)
                label.AddToClassList(c);
            return label;
        }
    }
}
