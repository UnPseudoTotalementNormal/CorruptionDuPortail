using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace Meaf75.Unity {

    /// <summary>
    /// Reads the Claude Code working-time ledger produced by the .claude/hooks PowerShell scripts.
    /// Read-only on the Unity side: the hooks own the JSON file (machine-local, gitignored), the
    /// calendar window only visualizes it. Mirrors how TimeRecorder keeps dev time in PlayerPrefs.
    /// </summary>
    public static class ClaudeTimeReader {

        private static ClaudeTimeData cached;

        /// <summary> Absolute path to the directory the hook scripts read/write </summary>
        private static string LedgerDir {
            get {
                // Application.dataPath is the project's Assets folder
                return Path.GetFullPath(Path.Combine(Application.dataPath, "..", ".claude", "timerecorder"));
            }
        }

        /// <summary> Absolute path to the ledger maintained by the hook scripts </summary>
        public static string DataPath {
            get { return Path.Combine(LedgerDir, "claude_time.json"); }
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

        public static long TotalSeconds {
            get { return Data.totalSeconds; }
        }

        /// <summary> Force a re-read of the ledger from disk (used on window repaint) </summary>
        public static ClaudeTimeData Reload() {
            LoadFromDisk();
            return cached;
        }

        private static void LoadFromDisk() {
            try {
                if (!File.Exists(DataPath)) {
                    cached = new ClaudeTimeData();
                    return;
                }

                string json = File.ReadAllText(DataPath);

                cached = string.IsNullOrEmpty(json)
                    ? new ClaudeTimeData()
                    : JsonUtility.FromJson<ClaudeTimeData>(json) ?? new ClaudeTimeData();
            } catch (Exception e) {
                Debug.LogWarning($"[TimeRecorder] Could not read Claude time ledger: {e.Message}");
                cached = new ClaudeTimeData();
            }

            if (cached.days == null) {
                cached.days = new List<ClaudeDayEntry>();
            }
        }

        /// <summary> Claude worked seconds reported for the given calendar day (0 if none) </summary>
        public static int GetSecondsForDate(int year, int month, int day) {
            var days = Data.days;
            if (days == null) {
                return 0;
            }

            var entry = days.Find(d => d.year == year && d.month == month && d.day == day);
            return entry != null ? entry.seconds : 0;
        }
    }
}
