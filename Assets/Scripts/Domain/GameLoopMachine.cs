using System.Collections.Generic;

namespace CorruptionDuPortail.Domain
{
    /// <summary>
    /// The decision an <see cref="GameLoopMachine.Advance"/> produces. Pure data — the
    /// adapter applies it (fires the events, calls SwitchGameState). Decision-only (NFR4).
    /// </summary>
    public readonly struct GameLoopTransition
    {
        /// <summary>The state index to switch to.</summary>
        public int NewIndex { get; }

        /// <summary>The advance crossed a loop boundary back to the first in-loop state (a new day).</summary>
        public bool FireNewDayPassed { get; }

        /// <summary>The advance entered the game loop for the very first time.</summary>
        public bool FireGameStarted { get; }

        /// <summary>The (possibly updated) first-loop flag the adapter must store back.</summary>
        public bool GameHasStartedFirstLoop { get; }

        public GameLoopTransition(int newIndex, bool fireNewDayPassed, bool fireGameStarted, bool gameHasStartedFirstLoop)
        {
            NewIndex = newIndex;
            FireNewDayPassed = fireNewDayPassed;
            FireGameStarted = fireGameStarted;
            GameHasStartedFirstLoop = gameHasStartedFirstLoop;
        }
    }

    /// <summary>
    /// Pure advance/rewind index arithmetic for the game-state loop (Story 2.11b — extracted from
    /// GameManager.NextGameState / PreviousGameState). Decision-only (NFR4): no NGO, no NetworkVariable,
    /// no event invoke. The adapter owns the index (the NetworkVariable write) and fires the events —
    /// the 2-phase split keeps full index ownership + the reflection RPC dispatch HELD to Epic 5.
    ///
    /// "In game loop" is per-state config (GameStateSettings.isInGameLoop), passed in as a positional
    /// list whose order matches the adapter's GetGameState(index). ignoreGameLoop is a per-call
    /// PARAMETER here, never a Domain field.
    /// </summary>
    public sealed class GameLoopMachine
    {
        /// <summary>
        /// NextGameState arithmetic: +1 (wrapping at count), day-pass detection (a loop boundary back
        /// to the first in-loop state), and first-loop detection. Returns a decision; raises nothing.
        /// </summary>
        public GameLoopTransition Advance(
            int currentIndex,
            IReadOnlyList<bool> isInGameLoop,
            bool gameHasStartedFirstLoop,
            bool ignoreGameLoop,
            bool ignoreGameLoopThisCall)
        {
            int count = isInGameLoop.Count;
            bool wasInGameLoop = isInGameLoop[currentIndex];

            int newIndex = currentIndex + 1;
            if (newIndex >= count)
            {
                newIndex = 0;
            }

            bool fireNewDayPassed = false;
            if (!ignoreGameLoopThisCall && wasInGameLoop && !ignoreGameLoop && !isInGameLoop[newIndex])
            {
                newIndex = FirstInGameLoopIndex(isInGameLoop);
                fireNewDayPassed = true;
            }

            bool fireGameStarted = false;
            bool newGameHasStartedFirstLoop = gameHasStartedFirstLoop;
            if (isInGameLoop[newIndex] && !gameHasStartedFirstLoop)
            {
                newGameHasStartedFirstLoop = true;
                fireGameStarted = true;
            }

            return new GameLoopTransition(newIndex, fireNewDayPassed, fireGameStarted, newGameHasStartedFirstLoop);
        }

        /// <summary>
        /// PreviousGameState arithmetic: -1 (wrapping at 0) and reverse day-pass (jump to the last
        /// in-loop state). Returns the new index only — rewind raises no event and does not touch the
        /// first-loop flag (mirrors the current PreviousGameState).
        /// </summary>
        public int Rewind(int currentIndex, IReadOnlyList<bool> isInGameLoop, bool ignoreGameLoop)
        {
            int count = isInGameLoop.Count;
            bool wasInGameLoop = isInGameLoop[currentIndex];

            int newIndex = currentIndex - 1;
            if (newIndex < 0)
            {
                newIndex = count - 1;
            }

            if (wasInGameLoop && !ignoreGameLoop && !isInGameLoop[newIndex])
            {
                newIndex = LastInGameLoopIndex(isInGameLoop);
            }

            return newIndex;
        }

        // Mirrors the live gameStates.ToList().FindIndex(pair => pair.Value.isInGameLoop):
        // first in-loop index, or -1 (the existing SwitchGameState assert fires identically).
        private static int FirstInGameLoopIndex(IReadOnlyList<bool> isInGameLoop)
        {
            for (int i = 0; i < isInGameLoop.Count; i++)
            {
                if (isInGameLoop[i])
                {
                    return i;
                }
            }
            return -1;
        }

        // Mirrors the live gameStates.ToList().FindLastIndex(pair => pair.Value.isInGameLoop).
        private static int LastInGameLoopIndex(IReadOnlyList<bool> isInGameLoop)
        {
            for (int i = isInGameLoop.Count - 1; i >= 0; i--)
            {
                if (isInGameLoop[i])
                {
                    return i;
                }
            }
            return -1;
        }
    }
}
