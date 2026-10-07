using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Meaf75.Unity{
    [Serializable]
    public class DateInfo{
        public int date;
        public int timeInSeconds;
    }

    [Serializable]
    public class MonthInfo{
        public int month;
        public List<DateInfo> dates;
    }

    [Serializable]
    public class YearInfo{
        public int year;
        public List<MonthInfo> months;
    }

    [Serializable]
    public class TimeRecorderInfo {
        public List<YearInfo> years;
        /// <summary> Worked time in seconds </summary>
        public long totalRecordedTime;

        public (DateInfo dayInfo, MonthInfo monthInfo, YearInfo yearInfo) VerifyByDatetime(DateTime dateTime) {
            // Save time
            if(years == null){
                years = new List<YearInfo>();
            }

            var yearInfoIdx = years.FindIndex(y => y.year == dateTime.Year);
            var yearInfo = new YearInfo();

            if(yearInfoIdx == -1){
                // Create & setup year
                yearInfo.year = dateTime.Year;
                yearInfo.months = new List<MonthInfo>();
                years.Add(yearInfo);
            } else{
                // Find year
                yearInfo = years[yearInfoIdx];
            }

            // Initialize year months
            if(yearInfo.months == null){
                yearInfo.months = new List<MonthInfo>();
            }

            var monthInfoIdx = yearInfo.months.FindIndex(m => m.month == dateTime.Month);
            var monthInfo = new MonthInfo();

            if(monthInfoIdx == -1){
                // Create & setup month
                monthInfo.month = dateTime.Month;
                monthInfo.dates = new List<DateInfo>();
                yearInfo.months.Add(monthInfo);
            } else{
                // Find moth
                monthInfo = yearInfo.months[monthInfoIdx];
            }

            // Initialize month dates
            if(monthInfo.dates == null){
                monthInfo.dates = new List<DateInfo>();
            }
            
            var dateInfoIdx = monthInfo.dates.FindIndex(m => m.date == dateTime.Day);
            var dateInfo = new DateInfo();

            if(dateInfoIdx == -1){
                // Create & setup day
                dateInfo.date = dateTime.Day;
                dateInfo.timeInSeconds = 0;
                monthInfo.dates.Add(dateInfo);
            } else{
                // Find day
                dateInfo = monthInfo.dates[dateInfoIdx];
            }

            return (dateInfo, monthInfo, yearInfo);
        }
    }

    [Serializable]
    public class ClaudeDayEntry {
        public int year;
        public int month;
        public int day;
        /// <summary> Claude worked time for this day, in seconds </summary>
        public int seconds;
    }

    [Serializable]
    public class ClaudeTimeData {
        public List<ClaudeDayEntry> days = new List<ClaudeDayEntry>();
        /// <summary> Total Claude worked time in seconds </summary>
        public long totalSeconds;
    }

    [Serializable]
    public class TimeTrackerWindowData{
        /// <summary> This variable cannot be serialized by the JsonUtility </summary>
        public DateTime selectedDate;
        public long selectedDateTicks;

        public TimeTrackerWindowData(){
            selectedDate = DateTime.Now;
        }

        public string GetJson(){
            selectedDateTicks = selectedDate.Ticks;
            return JsonUtility.ToJson(this);
        }
    }

    public static class TimeRecorderExtras {
        public const string TIME_RECORDER_REGISTRY = "time_recorder_registry";
        public const string NEXT_SAVE_TIME_PREF = "next_save_time_recorder";

        public const string CORRUPTED_JSON_BACKUP = "corrupted_time_recorder_json_{0}.json";
        public const string TIME_RECORDER_WINDOW_P_PREF = "time_recorder_window_player_pref";

        public const string TIME_RECORDER_PAUSE_P_PREF = "time_recorder_pause";

        public static string GetPauseButtonLabelForState(bool paused) {
            return  paused ? "Resume ▶" : "Pause ▯▯";
        }

        public static readonly string CALENDAR_TEMPLATE_PATH = "CalendarTemplate";
        public static readonly string CALENDAR_TEMPLATE_STYLE_PATH = "CalendarTemplateStyle";
    }
}

