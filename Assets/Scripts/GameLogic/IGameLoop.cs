using System;
using Network.Action;

namespace GameLogic
{
    /// <summary>
    /// Story 8.1 (Epic 8 / D2) — the game-loop COMMAND slice of GameManager's public surface
    /// (refactor-architecture-despaghetti.md §8). Consumers that drive or observe loop progression
    /// depend on this narrow intent instead of the whole GameManager God Object. Signatures are
    /// lifted verbatim from GameManager (the 7.5 public-member census) — no signature "improvement".
    /// Lives in the Game assembly, NOT Domain: it exposes engine/NGO types (GameState, NetworkAction),
    /// which Domain's noEngineReferences purity forbids (AC2, recorded).
    /// </summary>
    public interface IGameLoop
    {
        /// <summary>Advance to the next game state (server-authoritative). <paramref name="_ignoreGameLoop"/> bypasses the loop guard.</summary>
        void NextGameState(bool _ignoreGameLoop = false);

        /// <summary>Step back to the previous game state (server-authoritative).</summary>
        void PreviousGameState();

        /// <summary>Switch directly to the given game state instance (server-authoritative).</summary>
        void SetGameState(GameState _gameState);

        /// <summary>Switch directly to the first game state of the given type (server-authoritative).</summary>
        void SetGameState(Type _gameStateType);

        /// <summary>The current day number (1-based): gameLoopCount + 1.</summary>
        int currentDay { get; }

        /// <summary>True once the first game loop has started.</summary>
        bool hasGameStarted { get; }

        /// <summary>Raised when the game starts (first loop). NetworkAction-backed (5.0b).</summary>
        NetworkAction onGameStarted { get; }

        /// <summary>Raised each time a new day passes. NetworkAction-backed (5.0b).</summary>
        NetworkAction onNewDayPassed { get; }
    }
}
