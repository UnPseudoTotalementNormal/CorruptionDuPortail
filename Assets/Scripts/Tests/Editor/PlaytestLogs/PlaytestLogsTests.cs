using System;
using System.Collections.Generic;
using System.Text;
using Diagnostics.Logs;
using NUnit.Framework;
using UnityEngine;

namespace Tests.Editor.PlaytestLogs
{
    [TestFixture]
    public class PlaytestLogsTests
    {
        private static LogRecord Record(long _t, char _lv = 'I', string _c = "vote", string _e = "vote.cast", string _p = null) =>
            new LogRecord { t = _t, lv = _lv, c = _c, e = _e, p = _p, s = 3, hasSeat = true, ro = "Abyss", ph = "VoteState", d = 2, g = "20261020-2140-a3f9" };

        private const long Later = 1_000_000; // far past every repeat window: nothing is held back

        // ---- LogBuffer ----

        [Test]
        public void Drain_SerializesOneCompactLinePerRecord()
        {
            var _buffer = new LogBuffer();
            _buffer.Add(Record(1000, _p: LogJson.Object(new object[] { "from", 3UL, "to", 5UL })));

            List<string> _lines = _buffer.Drain(Later);

            Assert.AreEqual(1, _lines.Count);
            Assert.AreEqual(
                "{\"t\":1000,\"lv\":\"I\",\"c\":\"vote\",\"e\":\"vote.cast\",\"s\":3,\"ro\":\"Abyss\",\"ph\":\"VoteState\",\"d\":2," +
                "\"g\":\"20261020-2140-a3f9\",\"p\":{\"from\":3,\"to\":5}}",
                _lines[0]);
            Assert.AreEqual(0, _buffer.Drain(Later).Count, "drained lines are gone");
        }

        [Serializable]
        private class LineDto
        {
            public long t;
            public string lv;
            public string c;
            public string e;
            public string ro;
            public string ph;
            public int d;
            public string g;
            public string m;
            public int r;
        }

        [Test]
        public void Lines_RoundTripThroughAJsonParser()
        {
            var _buffer = new LogBuffer();
            _buffer.Add(new LogRecord
            {
                t = 42, lv = 'W', c = "unity", e = "CHAT", m = "Élu \"Wouh\"\n\tvote ⏎ 😺", s = ulong.MaxValue - 2, hasSeat = true,
                ph = "AwakeningState", d = 3, g = "G",
            });
            _buffer.Add(new LogRecord { t = 43, lv = 'W', c = "unity", e = "CHAT", m = "x" });

            List<string> _lines = _buffer.Drain(Later);
            var _dto = JsonUtility.FromJson<LineDto>(_lines[0]);

            Assert.AreEqual(42, _dto.t);
            Assert.AreEqual("W", _dto.lv);
            Assert.AreEqual("Élu \"Wouh\"\n\tvote ⏎ 😺", _dto.m);
            StringAssert.Contains("\"s\":18446744073709551613,", _lines[0], "fake seats (near ulong.MaxValue) are written whole");
            Assert.AreEqual(3, _dto.d);
            StringAssert.DoesNotContain("\"s\"", _lines[1], "no seat known: no seat key");
        }

        [Test]
        public void IdenticalLinesWithinTheWindow_AreMergedWithARepeatCount()
        {
            var _buffer = new LogBuffer();
            for (int _i = 0; _i < 500; _i++)
            {
                _buffer.Add(Record(1000 + _i));
            }

            List<string> _lines = _buffer.Drain(Later);

            Assert.AreEqual(1, _lines.Count);
            StringAssert.EndsWith(",\"r\":500}", _lines[0]);
        }

        [Test]
        public void AStormSpreadOverSeveralDrains_StillGivesOneLine()
        {
            var _buffer = new LogBuffer();
            for (int _i = 0; _i < 250; _i++) _buffer.Add(Record(1000 + _i * 4));

            Assert.AreEqual(0, _buffer.Drain(2000).Count, "the last line is held back while it can still grow");

            for (int _i = 0; _i < 250; _i++) _buffer.Add(Record(2000 + _i * 4));
            List<string> _lines = _buffer.Drain(Later);

            Assert.AreEqual(1, _lines.Count);
            StringAssert.EndsWith(",\"r\":500}", _lines[0]);
        }

        [Test]
        public void DrainAll_ReleasesTheHeldBackLine()
        {
            var _buffer = new LogBuffer();
            _buffer.Add(Record(1000));

            Assert.AreEqual(0, _buffer.Drain(1001).Count);
            Assert.AreEqual(1, _buffer.Drain(1001, _all: true).Count);
        }

        [Test]
        public void IdenticalLinesAfterTheWindow_OrBackInTime_StartANewLine()
        {
            var _buffer = new LogBuffer();
            _buffer.Add(Record(10000));
            _buffer.Add(Record(10000 + LogBuffer.RepeatWindowMs + 1));
            _buffer.Add(Record(9000)); // clock set back

            Assert.AreEqual(3, _buffer.Drain(Later).Count);
        }

        [Test]
        public void CategoryCap_DropsTheExcessAndReportsItNextMinute()
        {
            var _buffer = new LogBuffer(_capPerMinute: 3);
            for (int _i = 0; _i < 10; _i++)
            {
                _buffer.Add(Record(1000 + _i, _p: "{\"i\":" + _i + "}"));
            }
            _buffer.Add(Record(2000, _c: "chat", _e: "chat.message"));

            List<string> _first = _buffer.Drain(3000, _all: true);
            Assert.AreEqual(4, _first.Count, "3 vote lines + the chat line (other category, own budget)");

            List<string> _next = _buffer.Drain(61000, _all: true);
            Assert.AreEqual(1, _next.Count);
            StringAssert.Contains("\"e\":\"log.dropped\"", _next[0]);
            StringAssert.Contains("\"p\":{\"cat\":\"vote\",\"n\":7}", _next[0]);
        }

        [Test]
        public void Errors_AreNeverCapped()
        {
            var _buffer = new LogBuffer(_capPerMinute: 2);
            for (int _i = 0; _i < 5; _i++) _buffer.Add(Record(1000 + _i, _c: "unity", _e: "INFO", _p: "{\"i\":" + _i + "}"));
            _buffer.Add(Record(1100, 'E', "unity", "NullReferenceException"));

            List<string> _lines = _buffer.Drain(1200, _all: true);

            Assert.AreEqual(3, _lines.Count, "2 info lines (cap) + the error");
            StringAssert.Contains("NullReferenceException", _lines[2]);
        }

        [Test]
        public void ALateRecordFromAnotherThread_NeverRollsTheMinuteBack()
        {
            var _buffer = new LogBuffer(_capPerMinute: 1);
            _buffer.Add(Record(61000, _p: "{\"i\":1}"));
            _buffer.Add(Record(59000, _p: "{\"i\":2}")); // stamped before the minute rolled
            _buffer.Add(Record(61500, _p: "{\"i\":3}"));

            Assert.AreEqual(1, _buffer.Drain(Later).Count - 1, "one line kept in minute 1, the rest dropped and reported");
        }

        [Test]
        public void LongMessagesStacksAndParameters_AreTruncated()
        {
            var _buffer = new LogBuffer();
            _buffer.Add(new LogRecord
            {
                t = 1, lv = 'E', c = "unity", m = new string('x', 10000), st = new string('y', 10000),
                p = "{\"big\":\"" + new string('z', 50000) + "\"}",
            });

            string _line = _buffer.Drain(Later)[0];

            Assert.Less(_line.Length, LogBuffer.MaxMessageChars + LogBuffer.MaxStackChars + LogBuffer.MaxParamsChars + 400);
            StringAssert.Contains("\"p\":{\"_truncated\":\"{\\\"big\\\":", _line, "still a JSON object");
            Assert.AreEqual(1, _buffer.Errors);
        }

        // ---- LogJson ----

        [Test]
        public void Object_EscapesTextAndWritesNumbersInvariant()
        {
            string _json = LogJson.Object(new object[] { "text", "a \"b\"\n", "f", 1.5f, "ok", true, "ids", new List<ulong> { 1, 2 }, "none", null });

            Assert.AreEqual("{\"text\":\"a \\\"b\\\"\\n\",\"f\":1.5,\"ok\":true,\"ids\":[1,2],\"none\":null}", _json);
        }

        [Test]
        public void Object_OddTrailingValue_IsKeptUnderUnderscore()
        {
            Assert.AreEqual("{\"a\":1,\"_\":\"lost\"}", LogJson.Object(new object[] { "a", 1, "lost" }));
            Assert.IsNull(LogJson.Object(new object[0]));
        }

        // ---- RemoteLogConfig ----

        [Test]
        public void Config_UnreadableOrEmpty_IsNull()
        {
            Assert.IsNull(RemoteLogConfig.Parse(null));
            Assert.IsNull(RemoteLogConfig.Parse(""));
            Assert.IsNull(RemoteLogConfig.Parse("not json"));
        }

        [Test]
        public void Config_WithoutEnabled_IsOff()
        {
            Assert.IsFalse(RemoteLogConfig.Parse("{\"minLevel\":\"I\"}").enabled);
            Assert.IsFalse(RemoteLogConfig.Parse("{\"enabled\":false}").enabled);
        }

        [Test]
        public void Config_ValuesAreClamped()
        {
            RemoteLogConfig _low = RemoteLogConfig.Parse("{\"enabled\":true,\"flushSeconds\":1,\"capPerMinute\":-4}");
            RemoteLogConfig _high = RemoteLogConfig.Parse("{\"enabled\":true,\"flushSeconds\":2147483647,\"capPerMinute\":99999999}");

            Assert.AreEqual(RemoteLogConfig.MinFlushSeconds, _low.flushSeconds);
            Assert.AreEqual(300, _low.capPerMinute);
            Assert.AreEqual(RemoteLogConfig.MaxFlushSeconds, _high.flushSeconds, "no overflow in the upload delay");
            Assert.AreEqual(5000, _high.capPerMinute);
        }

        [Test]
        public void Config_LevelsAndVerbosePlayers()
        {
            RemoteLogConfig _config = RemoteLogConfig.Parse(
                "{\"enabled\":true,\"minLevel\":\"I\",\"verbosePlayers\":[\"Wouh\"],\"debugCategories\":[\"net\"]}");

            Assert.IsTrue(_config.IsVerbose("wouh", null));
            Assert.IsFalse(_config.IsVerbose("Poyo", "76561198000000000"));
            Assert.IsFalse(_config.Uploads('D', "ui", false), "debug hidden by default");
            Assert.IsTrue(_config.Uploads('D', "ui", true), "debug of a verbose player");
            Assert.IsTrue(_config.Uploads('D', "net", false), "debug category");
            Assert.IsTrue(_config.Uploads('I', "ui", false));
            Assert.IsTrue(RemoteLogConfig.Parse("{\"enabled\":true,\"minLevel\":\"W\"}").Uploads('E', "x", false));
            Assert.IsFalse(RemoteLogConfig.Parse("{\"enabled\":true,\"minLevel\":\"W\"}").Uploads('I', "x", false));
        }

        // ---- LogChunk ----

        [Test]
        public void CompleteLinesLength_StopsAtTheLastNewlineUnderTheCap()
        {
            byte[] _bytes = Encoding.UTF8.GetBytes("aaaa\nbbbb\ncc");

            Assert.AreEqual(10, LogChunk.CompleteLinesLength(_bytes, _bytes.Length));
            Assert.AreEqual(5, LogChunk.CompleteLinesLength(_bytes, _bytes.Length, 7));
            Assert.AreEqual(0, LogChunk.CompleteLinesLength(Encoding.UTF8.GetBytes("partial"), 7));
        }

        [Test]
        public void CompleteLinesLength_ALineLongerThanTheCap_IsTakenAlone()
        {
            byte[] _bytes = Encoding.UTF8.GetBytes("aaaaaaaaaa\nbb\n");

            Assert.AreEqual(11, LogChunk.CompleteLinesLength(_bytes, _bytes.Length, 4));
        }

        [Test]
        public void NextRange_ALostAnswer_ResendsExactlyTheSameRange()
        {
            byte[] _bytes = Encoding.UTF8.GetBytes("a\nb\nc\nd\n"); // the file grew since the first try (was "a\nb\n")

            Assert.AreEqual(LogChunk.RangeKind.Lines, LogChunk.NextRange(_bytes, _bytes.Length, false, 4, out int _length));
            Assert.AreEqual(4, _length, "same bytes as the first try, so ALREADY_EXISTS really covers them");

            Assert.AreEqual(LogChunk.RangeKind.Lines, LogChunk.NextRange(_bytes, _bytes.Length, false, 0, out _length));
            Assert.AreEqual(8, _length);
        }

        [Test]
        public void NextRange_PartialLineWaits_GiantLineIsSkipped()
        {
            byte[] _partial = Encoding.UTF8.GetBytes("no newline yet");
            Assert.AreEqual(LogChunk.RangeKind.None, LogChunk.NextRange(_partial, _partial.Length, false, 0, out _));
            Assert.AreEqual(LogChunk.RangeKind.Skip, LogChunk.NextRange(_partial, _partial.Length, true, 0, out int _skip),
                "a full window without a newline is one giant line: never blocks the cursor");
            Assert.AreEqual(_partial.Length, _skip);

            byte[] _giant = new byte[LogChunk.MaxChunkBytes + 11];
            _giant[LogChunk.MaxChunkBytes + 9] = (byte)'\n';
            Assert.AreEqual(LogChunk.RangeKind.Skip, LogChunk.NextRange(_giant, _giant.Length, false, 0, out int _length));
            Assert.AreEqual(LogChunk.MaxChunkBytes + 10, _length);
        }

        [Test]
        public void Build_FiltersAndSummarizes()
        {
            string _text =
                "{\"t\":2000,\"lv\":\"D\",\"c\":\"ui\",\"e\":\"screen.show\"}\n" +
                "{\"t\":1000,\"lv\":\"I\",\"c\":\"vote\",\"g\":\"G1\"}\n" +
                "{\"t\":3000,\"lv\":\"E\",\"c\":\"unity\",\"g\":\"G2\"}\n" +
                "{\"t\":2500,\"lv\":\"W\",\"c\":\"net\",\"g\":\"G1\"}\n";

            LogChunk _chunk = LogChunk.Build(_text, (_lv, _cat) => _lv != 'D');

            Assert.AreEqual(3, _chunk.lineCount);
            Assert.AreEqual(1000, _chunk.t0);
            Assert.AreEqual(3000, _chunk.t1);
            Assert.AreEqual(1, _chunk.errors);
            Assert.AreEqual(1, _chunk.warnings);
            CollectionAssert.AreEqual(new[] { "G1", "G2" }, _chunk.gameIds);
            StringAssert.DoesNotContain("screen.show", _chunk.lines);
        }

        // ---- FirestoreFields ----

        [Test]
        public void FirestoreFields_WritesTypedValuesAndTheUpdateMask()
        {
            var _fields = new FirestoreFields()
                .String("player", "Wo\"uh")
                .Int("lines", 42)
                .Bool("crashed", true)
                .Timestamp("startedAt", 0)
                .StringArray("gameIds", new[] { "G1" });

            Assert.AreEqual(
                "{\"fields\":{\"player\":{\"stringValue\":\"Wo\\\"uh\"},\"lines\":{\"integerValue\":\"42\"}," +
                "\"crashed\":{\"booleanValue\":true},\"startedAt\":{\"timestampValue\":\"1970-01-01T00:00:00.000Z\"}," +
                "\"gameIds\":{\"arrayValue\":{\"values\":[{\"stringValue\":\"G1\"}]}}}}",
                _fields.ToJson());
            Assert.AreEqual(
                "?updateMask.fieldPaths=player&updateMask.fieldPaths=lines&updateMask.fieldPaths=crashed" +
                "&updateMask.fieldPaths=startedAt&updateMask.fieldPaths=gameIds",
                _fields.UpdateMaskQuery());
        }
    }
}
