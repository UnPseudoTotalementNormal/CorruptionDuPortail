using System.Collections.Generic;
using CorruptionDuPortail.Domain;
using NUnit.Framework;

namespace Tests.Editor
{
    /// <summary>
    /// EditMode property tests for the pure <see cref="GameLoopMachine"/> advance/rewind
    /// arithmetic (Story 2.11b). The live transition ordering stays pinned by the 2.11a
    /// PlayMode golden; these cover the index math + day-pass / first-loop detection.
    ///
    /// Reference layout used by most cases — isInGameLoop = [false, true, true, false]:
    ///   index 0 = pre-loop (e.g. lobby/intro), 1+2 = in the game loop, 3 = post-loop.
    /// </summary>
    [Category("GameLoopMachine")]
    public class GameLoopMachineTests
    {
        private static readonly List<bool> Layout = new() { false, true, true, false };

        private readonly GameLoopMachine _machine = new();

        // ───────────────────────────── Advance ─────────────────────────────

        [Test]
        public void Advance_IncrementsByOne_WithinTheLoop()
        {
            // current = 1 (in loop) → next = 2 (in loop): plain +1, no day-pass.
            var t = _machine.Advance(1, Layout, gameHasStartedFirstLoop: true, ignoreGameLoop: false, ignoreGameLoopThisCall: false);

            Assert.AreEqual(2, t.NewIndex);
            Assert.IsFalse(t.FireNewDayPassed);
            Assert.IsFalse(t.FireGameStarted);
        }

        [Test]
        public void Advance_WrapsToZero_WhenLeavingTheLastIndex_WhenNotInLoop()
        {
            // current = 3 (NOT in loop) → +1 wraps to 0 (NOT in loop). wasInGameLoop is
            // false, so no day-pass; lands on 0.
            var t = _machine.Advance(3, Layout, gameHasStartedFirstLoop: true, ignoreGameLoop: false, ignoreGameLoopThisCall: false);

            Assert.AreEqual(0, t.NewIndex);
            Assert.IsFalse(t.FireNewDayPassed);
        }

        [Test]
        public void Advance_DayPass_JumpsBackToFirstInLoopIndex_AndFiresNewDayPassed()
        {
            // current = 2 (last in-loop) → +1 = 3 (NOT in loop) while wasInGameLoop → day-pass:
            // jump to the FIRST in-loop index (1) and raise FireNewDayPassed.
            var t = _machine.Advance(2, Layout, gameHasStartedFirstLoop: true, ignoreGameLoop: false, ignoreGameLoopThisCall: false);

            Assert.AreEqual(1, t.NewIndex);
            Assert.IsTrue(t.FireNewDayPassed);
        }

        [Test]
        public void Advance_DayPass_Suppressed_ByCallParameter()
        {
            // Same boundary as above but ignoreGameLoopThisCall = true → no day-pass: lands on 3.
            var t = _machine.Advance(2, Layout, gameHasStartedFirstLoop: true, ignoreGameLoop: false, ignoreGameLoopThisCall: true);

            Assert.AreEqual(3, t.NewIndex);
            Assert.IsFalse(t.FireNewDayPassed);
        }

        [Test]
        public void Advance_DayPass_Suppressed_ByGlobalIgnoreGameLoop()
        {
            // Same boundary but global ignoreGameLoop = true → no day-pass: lands on 3.
            var t = _machine.Advance(2, Layout, gameHasStartedFirstLoop: true, ignoreGameLoop: true, ignoreGameLoopThisCall: false);

            Assert.AreEqual(3, t.NewIndex);
            Assert.IsFalse(t.FireNewDayPassed);
        }

        [Test]
        public void Advance_FirstLoop_FiresGameStarted_AndSetsFlag()
        {
            // current = 0 (pre-loop) → next = 1 (in loop) while !gameHasStartedFirstLoop →
            // first entry into the loop: raise FireGameStarted and set the flag.
            var t = _machine.Advance(0, Layout, gameHasStartedFirstLoop: false, ignoreGameLoop: false, ignoreGameLoopThisCall: false);

            Assert.AreEqual(1, t.NewIndex);
            Assert.IsTrue(t.FireGameStarted);
            Assert.IsTrue(t.GameHasStartedFirstLoop);
        }

        [Test]
        public void Advance_FirstLoop_DoesNotFire_WhenAlreadyStarted()
        {
            // Entering an in-loop state but gameHasStartedFirstLoop already true → no fire.
            var t = _machine.Advance(0, Layout, gameHasStartedFirstLoop: true, ignoreGameLoop: false, ignoreGameLoopThisCall: false);

            Assert.AreEqual(1, t.NewIndex);
            Assert.IsFalse(t.FireGameStarted);
            Assert.IsTrue(t.GameHasStartedFirstLoop);
        }

        [Test]
        public void Advance_FirstLoop_DoesNotFire_WhenLandingOutsideTheLoop()
        {
            // current = 3 → wraps to 0 (NOT in loop): even with !gameHasStartedFirstLoop, no fire.
            var t = _machine.Advance(3, Layout, gameHasStartedFirstLoop: false, ignoreGameLoop: false, ignoreGameLoopThisCall: false);

            Assert.AreEqual(0, t.NewIndex);
            Assert.IsFalse(t.FireGameStarted);
            Assert.IsFalse(t.GameHasStartedFirstLoop);
        }

        // ───────────────────────────── Rewind ─────────────────────────────

        [Test]
        public void Rewind_DecrementsByOne_WithinTheLoop()
        {
            // current = 2 (in loop) → prev = 1 (in loop): plain -1, no reverse day-pass.
            Assert.AreEqual(1, _machine.Rewind(2, Layout, ignoreGameLoop: false));
        }

        [Test]
        public void Rewind_WrapsToLastIndex_FromZero_WhenNotInLoop()
        {
            // current = 0 (NOT in loop) → -1 wraps to 3. wasInGameLoop false → no reverse day-pass.
            Assert.AreEqual(3, _machine.Rewind(0, Layout, ignoreGameLoop: false));
        }

        [Test]
        public void Rewind_ReverseDayPass_JumpsToLastInLoopIndex()
        {
            // current = 1 (first in-loop) → -1 = 0 (NOT in loop) while wasInGameLoop → reverse
            // day-pass: jump to the LAST in-loop index (2). No event, no first-loop on rewind.
            Assert.AreEqual(2, _machine.Rewind(1, Layout, ignoreGameLoop: false));
        }

        [Test]
        public void Rewind_ReverseDayPass_Suppressed_ByGlobalIgnoreGameLoop()
        {
            // Same boundary as above but global ignoreGameLoop = true → no reverse day-pass: lands on 0.
            Assert.AreEqual(0, _machine.Rewind(1, Layout, ignoreGameLoop: true));
        }
    }
}
