using System.Collections.Generic;
using System.Text;
using Diagnostics.Logs;
using NUnit.Framework;

namespace Tests.Editor.PlaytestLogs
{
    [TestFixture]
    public class PlaytestLogsTests
    {
        private static LogRecord Record(long _t, char _lv = 'I', string _c = "vote", string _e = "vote.cast", string _p = null) =>
            new LogRecord { t = _t, lv = _lv, c = _c, e = _e, p = _p, s = 3, ph = "VoteState", d = 2, g = "20261020-2140-a3f9" };

        // ---- LogBuffer ----

        [Test]
        public void Drain_SerializesOneCompactLinePerRecord()
        {
            var _buffer = new LogBuffer();
            _buffer.Add(Record(1000, _p: LogJson.Object(new object[] { "from", 3UL, "to", 5UL })));

            List<string> _lines = _buffer.Drain(1000);

            Assert.AreEqual(1, _lines.Count);
            Assert.AreEqual(
                "{\"t\":1000,\"lv\":\"I\",\"c\":\"vote\",\"e\":\"vote.cast\",\"s\":3,\"ph\":\"VoteState\",\"d\":2," +
                "\"g\":\"20261020-2140-a3f9\",\"p\":{\"from\":3,\"to\":5}}",
                _lines[0]);
            Assert.AreEqual(0, _buffer.Drain(1000).Count, "drained lines are gone");
        }

        [Test]
        public void IdenticalLinesWithinTheWindow_AreMergedWithARepeatCount()
        {
            var _buffer = new LogBuffer();
            for (int _i = 0; _i < 500; _i++)
            {
                _buffer.Add(Record(1000 + _i));
            }

            List<string> _lines = _buffer.Drain(2000);

            Assert.AreEqual(1, _lines.Count);
            StringAssert.EndsWith(",\"r\":500}", _lines[0]);
        }

        [Test]
        public void IdenticalLinesAfterTheWindow_StartANewLine()
        {
            var _buffer = new LogBuffer();
            _buffer.Add(Record(1000));
            _buffer.Add(Record(1000 + LogBuffer.RepeatWindowMs + 1));

            Assert.AreEqual(2, _buffer.Drain(10000).Count);
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

            List<string> _first = _buffer.Drain(3000);
            Assert.AreEqual(4, _first.Count, "3 vote lines + the chat line (other category, own budget)");

            List<string> _next = _buffer.Drain(61000);
            Assert.AreEqual(1, _next.Count);
            StringAssert.Contains("\"e\":\"log.dropped\"", _next[0]);
            StringAssert.Contains("\"p\":{\"cat\":\"vote\",\"n\":7}", _next[0]);
        }

        [Test]
        public void LongMessagesAndStacks_AreTruncated()
        {
            var _buffer = new LogBuffer();
            _buffer.Add(new LogRecord { t = 1, lv = 'E', c = "unity", m = new string('x', 10000), st = new string('y', 10000), s = -1 });

            string _line = _buffer.Drain(1)[0];

            Assert.Less(_line.Length, LogBuffer.MaxMessageChars + LogBuffer.MaxStackChars + 200);
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
        public void Config_Disabled_ParsesAsDisabled()
        {
            RemoteLogConfig _config = RemoteLogConfig.Parse("{\"enabled\":false}");

            Assert.IsNotNull(_config);
            Assert.IsFalse(_config.enabled);
            Assert.AreEqual(20, _config.flushSeconds);
        }

        [Test]
        public void Config_LevelsAndVerbosePlayers()
        {
            RemoteLogConfig _config = RemoteLogConfig.Parse(
                "{\"enabled\":true,\"minLevel\":\"I\",\"verbosePlayers\":[\"Wouh\"],\"debugCategories\":[\"net\"],\"flushSeconds\":1}");

            Assert.AreEqual(RemoteLogConfig.MinFlushSeconds, _config.flushSeconds, "flush floor");
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
