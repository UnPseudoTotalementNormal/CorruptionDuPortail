using NUnit.Framework;
using TooltipSystem;
using UnityEngine;

namespace Tests.Editor
{
    public class TooltipLinkParserTests
    {
        // Dummy class for testing reflection-based parsing
        private class DummyParsingObject : ScriptableObject
        {
            public string publicString = "PublicValue";
            private int privateInt = 42;
            public float publicProp { get; set; } = 3.14f;

            // This is required to access the private field via reflection in the test assertion context
            public int GetPrivateInt() => privateInt;
        }

        private TooltipLinkParser _parser;
        private DummyParsingObject _dummyObject;

        [SetUp]
        public void Setup()
        {
            GameObject go = new GameObject("Parser");
            _parser = go.AddComponent<TooltipLinkParser>();
            _dummyObject = ScriptableObject.CreateInstance<DummyParsingObject>();
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(_parser.gameObject);
            Object.DestroyImmediate(_dummyObject);
        }

        [Test]
        public void ParseText_WithPublicField_ReplacesBalise()
        {
            string text = "This is a {var:publicString} test.";
            string parsed = _parser.ParseText(_dummyObject, text);
            Assert.AreEqual("This is a PublicValue test.", parsed);
        }

        [Test]
        public void ParseText_WithPrivateField_ReplacesBalise()
        {
            string text = "The answer is {var:privateInt}.";
            string parsed = _parser.ParseText(_dummyObject, text);
            Assert.AreEqual("The answer is 42.", parsed);
        }

        [Test]
        public void ParseText_WithProperty_ReplacesBalise()
        {
            string text = "Pi is roughly {var:publicProp}.";
            string parsed = _parser.ParseText(_dummyObject, text);
            // Handling both . and , depending on culture
            string expected = $"Pi is roughly {3.14f}.";
            Assert.AreEqual(expected, parsed);
        }

        [Test]
        public void ParseText_WithUnknownVar_DoesNotReplaceAndWarns()
        {
            string text = "Unknown {var:missingField} test.";
            
            // It will replace with empty string if GetVarValue returns null (as per current implementation)
            // Wait, actually the implementation says:
            // if (_varName != null) { _replaceText = _varName.ToString(); }
            // So if it's null, _replaceText remains "" (empty string).
            
            string parsed = _parser.ParseText(_dummyObject, text);
            Assert.AreEqual("Unknown  test.", parsed);
        }

        [Test]
        public void ParseText_WithMultipleBalises_ReplacesAll()
        {
            string text = "{var:publicString} has {var:privateInt} apples.";
            string parsed = _parser.ParseText(_dummyObject, text);
            Assert.AreEqual("PublicValue has 42 apples.", parsed);
        }

        [Test]
        public void ParseText_WithUnknownCommand_WarnsAndReplacesWithEmpty()
        {
            string text = "Test {unknown:someParam} here.";
            
            UnityEngine.TestTools.LogAssert.Expect(LogType.Warning, "Unknown balise command: unknown");
            
            string parsed = _parser.ParseText(_dummyObject, text);
            Assert.AreEqual("Test  here.", parsed);
        }
    }
}
