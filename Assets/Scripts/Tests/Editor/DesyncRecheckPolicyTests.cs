using CorruptionDuPortail.Domain;
using NUnit.Framework;

namespace Tests.Editor
{
    /// <summary>NET-00 — a mismatch is a desync only if it persists across a re-check, reported once per state.</summary>
    [Category("Desync")]
    public class DesyncRecheckPolicyTests
    {
        [Test]
        public void NoMismatch_NoRecheck()
        {
            var _policy = new DesyncRecheckPolicy();
            Assert.IsFalse(_policy.OnFirstCheck(new string[0]));
            Assert.IsFalse(_policy.HasPendingRecheck);
        }

        [Test]
        public void TransientMismatch_ClearsOnRecheck()
        {
            var _policy = new DesyncRecheckPolicy();
            Assert.IsTrue(_policy.OnFirstCheck(new[] { "Roster" }));
            Assert.IsEmpty(_policy.OnRecheck(new string[0], "3"));
            Assert.IsFalse(_policy.HasPendingRecheck);
        }

        [Test]
        public void PersistentMismatch_IsConfirmed()
        {
            var _policy = new DesyncRecheckPolicy();
            _policy.OnFirstCheck(new[] { "Roster", "GameState" });
            CollectionAssert.AreEqual(new[] { "Roster" }, _policy.OnRecheck(new[] { "Roster" }, "3"));
        }

        [Test]
        public void ComponentOnlyInRecheck_IsNotConfirmed()
        {
            var _policy = new DesyncRecheckPolicy();
            _policy.OnFirstCheck(new[] { "Roster" });
            Assert.IsEmpty(_policy.OnRecheck(new[] { "Characters" }, "3"));
        }

        [Test]
        public void SameComponentSameState_ReportedOnce()
        {
            var _policy = new DesyncRecheckPolicy();
            _policy.OnFirstCheck(new[] { "Roster" });
            Assert.AreEqual(1, _policy.OnRecheck(new[] { "Roster" }, "3").Count);
            _policy.OnFirstCheck(new[] { "Roster" });
            Assert.IsEmpty(_policy.OnRecheck(new[] { "Roster" }, "3"));
        }

        [Test]
        public void SameComponentNewState_ReportedAgain()
        {
            var _policy = new DesyncRecheckPolicy();
            _policy.OnFirstCheck(new[] { "Roster" });
            _policy.OnRecheck(new[] { "Roster" }, "3");
            _policy.OnFirstCheck(new[] { "Roster" });
            Assert.AreEqual(1, _policy.OnRecheck(new[] { "Roster" }, "4").Count);
        }
    }
}
