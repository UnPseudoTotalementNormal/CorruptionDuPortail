using System;
using GameLogic.Validation;
using NUnit.Framework;

namespace Tests.Editor
{
    public class ValidatorTests
    {
        [Test]
        public void Evaluate_NoRules_ReturnsTrue()
        {
            var validator = new Validator<int>();
            Assert.IsTrue(validator.Evaluate(10));
        }

        [Test]
        public void Evaluate_SinglePassingRule_ReturnsTrue()
        {
            var validator = new Validator<int>();
            validator.AddRule(x => x > 0);
            Assert.IsTrue(validator.Evaluate(5));
        }

        [Test]
        public void Evaluate_SingleFailingRule_ReturnsFalse()
        {
            var validator = new Validator<int>();
            validator.AddRule(x => x > 0);
            Assert.IsFalse(validator.Evaluate(-5));
        }

        [Test]
        public void Evaluate_MultiplePassingRules_ReturnsTrue()
        {
            var validator = new Validator<string>();
            validator.AddRule(s => !string.IsNullOrEmpty(s));
            validator.AddRule(s => s.Length > 3);
            Assert.IsTrue(validator.Evaluate("Unity"));
        }

        [Test]
        public void Evaluate_MixedRules_ReturnsFalseOnFirstFailure()
        {
            var validator = new Validator<int>();
            int callCount = 0;
            
            validator.AddRule(x => { callCount++; return x > 0; });
            validator.AddRule(x => { callCount++; return x < 10; }); // This should fail for 15
            validator.AddRule(x => { callCount++; return x % 2 == 0; }); // This should not be called

            bool result = validator.Evaluate(15);
            
            Assert.IsFalse(result);
            Assert.AreEqual(2, callCount, "Short-circuit evaluation: should stop at first failure");
        }

        [Test]
        public void Clear_RemovesAllRules()
        {
            var validator = new Validator<int>();
            validator.AddRule(x => x < 0); // Fails for 10
            
            validator.Clear();
            
            Assert.IsTrue(validator.Evaluate(10), "Should return true after rules are cleared");
        }
    }
}
