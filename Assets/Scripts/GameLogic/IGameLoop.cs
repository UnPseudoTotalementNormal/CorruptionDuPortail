using System;
using Cysharp.Threading.Tasks;
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

        /// <summary>Wait one frame, then advance to the next game state (server-authoritative). Verbatim lift from GameManager (Story 8.3 — completes the command slice for TakeDownThePortalState).</summary>
        UniTask WaitAFrameAndNextGameState();

        /// <summary>The current day number (1-based): gameLoopCount + 1.</summary>
        int currentDay { get; }

        /// <summary>True once the first game loop has started.</summary>
        bool hasGameStarted { get; }

        // get;set; (not get-only): NetworkAction subscription is `action += handler`, which the C# compiler
        // lowers to `action = action + handler` (operator+ mutates in place via AddListener and returns the
        // same instance). The set therefore writes the same reference back — behaviour-identical to the
        // concrete `onGameStarted += handler`, but a setter is required for the `+=`/`-=` idiom to compile
        // through the interface. Story 8.3 — the 8.1 get-only form predated any interface-side subscriber.
        /// <summary>Raised when the game starts (first loop). NetworkAction-backed (5.0b).</summary>
        NetworkAction onGameStarted { get; set; }

        /// <summary>Raised each time a new day passes. NetworkAction-backed (5.0b).</summary>
        NetworkAction onNewDayPassed { get; set; }
    }
}
